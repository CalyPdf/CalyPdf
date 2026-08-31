using Caly.Core.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Caly.Tests;

public class FilePipeStreamTests
{
    /// <summary>
    /// Unique per-test pipe name: the production name is single-instance, so tests
    /// (and the concurrently running net9.0/net10.0 test processes) must not share it.
    /// Kept short: Unix maps named pipes onto domain sockets under TMPDIR, and the full
    /// socket path is capped at 104 characters (macOS TMPDIR alone eats about 60).
    /// </summary>
    private static string NewPipeName() => $"caly_t_{Guid.NewGuid():N}"[..23];

    /// <summary>
    /// Consumes <see cref="FilePipeStream.ReceivePathAsync"/> into <paramref name="received"/>
    /// until the token is cancelled.
    /// </summary>
    private static Task ConsumeAsync(FilePipeStream server, ConcurrentQueue<string> received, CancellationToken token)
    {
        return Task.Run(async () =>
        {
            await foreach (var path in server.ReceivePathAsync(token))
            {
                if (path is not null)
                {
                    received.Enqueue(path);
                }
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Sends a complete, well-formed FilePath message in a single write
    /// (the same bytes SendPath produces).
    /// </summary>
    private static async Task SendFullMessageAsync(string pipeName, string filePath)
    {
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(2000, CancellationToken.None);

        var path = Encoding.UTF8.GetBytes(filePath);
        var key = "ca1y k3y pa$$"u8.ToArray();
        var msg = new List<byte>();
        msg.AddRange(BitConverter.GetBytes((ushort)path.Length));
        msg.AddRange(key);
        msg.Add(1); // PipeMessageType.FilePath
        msg.AddRange(path);

        client.Write([.. msg]);
        await client.FlushAsync(CancellationToken.None);
    }

    /// <summary>
    /// Waits for the consumer to complete after cancellation and surfaces its outcome.
    /// </summary>
    private static async Task AssertConsumerCompletes(Task consumer)
    {
        var finished = await Task.WhenAny(consumer, Task.Delay(2000, CancellationToken.None));
        Assert.Same(consumer, finished);
        await consumer;
    }

    [Fact]
    public async Task ClientDisconnectingMidMessage_DoesNotKillListener()
    {
        string pipeName = NewPipeName();
        await using var server = new FilePipeStream(pipeName);

        using var cts = new CancellationTokenSource();
        var received = new ConcurrentQueue<string>();
        var consumer = ConsumeAsync(server, received, cts.Token);

        await Task.Delay(300, CancellationToken.None); // let the server start listening

        // Half a message, then disconnect: ReadExactlyAsync sees end-of-stream. The
        // listener must drop the message and keep serving, not terminate.
        await using (var broken = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly))
        {
            await broken.ConnectAsync(2000, CancellationToken.None);
            broken.WriteByte(0x42); // 1 byte of the 2-byte length prefix
            await broken.FlushAsync(CancellationToken.None);
        }

        await Task.Delay(300, CancellationToken.None);

        // A well-behaved client must still get through afterwards.
        await SendFullMessageAsync(pipeName, "after-disconnect.pdf");
        await Task.Delay(500, CancellationToken.None);

        await cts.CancelAsync();
        await AssertConsumerCompletes(consumer);

        Assert.Contains("after-disconnect.pdf", received);
    }

    [Fact]
    public async Task StalledClient_TimesOutAndListenerRecovers()
    {
        string pipeName = NewPipeName();
        // Short receive deadline so the test doesn't sit through the production 5s.
        await using var server = new FilePipeStream(pipeName, receiveTimeout: TimeSpan.FromMilliseconds(250));

        using var cts = new CancellationTokenSource();
        var received = new ConcurrentQueue<string>();
        var consumer = ConsumeAsync(server, received, cts.Token);

        await Task.Delay(300, CancellationToken.None); // let the server start listening

        // Partial message and then silence, while staying connected. Without the
        // receive deadline this would block the single-instance listener forever.
        await using var staller = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
        await staller.ConnectAsync(2000, CancellationToken.None);
        staller.Write(BitConverter.GetBytes((ushort)7)); // length only; the rest never comes
        await staller.FlushAsync(CancellationToken.None);

        // Wait past the receive deadline: the server must disconnect the staller
        // and resume listening.
        await Task.Delay(800, CancellationToken.None);

        await SendFullMessageAsync(pipeName, "after-stall.pdf");
        await Task.Delay(500, CancellationToken.None);

        await cts.CancelAsync();
        await AssertConsumerCompletes(consumer);

        Assert.Contains("after-stall.pdf", received);
    }

    [Fact]
    public async Task CancellationEndsEnumeration()
    {
        await using var server = new FilePipeStream(NewPipeName());

        using var cts = new CancellationTokenSource();

        var consumer = Task.Run(async () =>
        {
            await foreach (var _ in server.ReceivePathAsync(cts.Token))
            {
            }
        }, CancellationToken.None);

        await Task.Delay(300, CancellationToken.None); // let the server start listening

        await cts.CancelAsync();

        // The enumeration must end promptly and cleanly once the token is cancelled —
        // it must not keep looping (previously the OperationCanceledException was
        // swallowed inside the loop, leaving a hot-spinning task behind).
        var finished = await Task.WhenAny(consumer, Task.Delay(2000, CancellationToken.None));
        Assert.Same(consumer, finished);
        await consumer;
    }

    [Fact]
    public async Task CanReadCoalesced()
    {
        string pipeName = NewPipeName();
        await using var server = new FilePipeStream(pipeName);

        using var cts = new CancellationTokenSource();
        var received = new ConcurrentQueue<string>();

        var enumerable = server.ReceivePathAsync(cts.Token);

        var consumer = Task.Run(async () =>
        {
            await foreach (var path in enumerable)
            {
                if (path is not null)
                {
                    received.Enqueue(path);
                }
            }
        }, CancellationToken.None);

        await Task.Delay(300, CancellationToken.None); // let the server start listening

        await SendCoalesced("coalesced.pdf");
        await Task.Delay(500, CancellationToken.None);

        await SendChunkedWithDelays("chunked.pdf");
        await Task.Delay(500, CancellationToken.None);

        await cts.CancelAsync();

        // The consumer itself must finish once cancelled, not just be abandoned.
        var finished = await Task.WhenAny(consumer, Task.Delay(2000, CancellationToken.None));
        Assert.Same(consumer, finished);
        await consumer;

        Assert.Contains("coalesced.pdf", received);
        Assert.Contains("chunked.pdf", received);

        async Task SendCoalesced(string filePath)
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(2000, CancellationToken.None);
            // Same bytes SendPath produces, but delivered in one write; the same thing
            // the kernel presents when SendPath's four writes land before the server reads.

            var path = Encoding.UTF8.GetBytes(filePath);
            var key = "ca1y k3y pa$$"u8.ToArray();
            var msg = new List<byte>();
            msg.AddRange(BitConverter.GetBytes((ushort)path.Length));
            msg.AddRange(key);
            msg.Add(1); // PipeMessageType.FilePath
            msg.AddRange(path);

            client.Write([.. msg]);
            await client.FlushAsync(CancellationToken.None);
        }

        async Task SendChunkedWithDelays(string filePath)
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(2000, CancellationToken.None);
            var path = Encoding.UTF8.GetBytes(filePath);
            client.Write(BitConverter.GetBytes((ushort)path.Length));
            await Task.Delay(80, CancellationToken.None);
            client.Write("ca1y k3y pa$$"u8);
            await Task.Delay(80, CancellationToken.None);
            client.WriteByte(1); // PipeMessageType.FilePath
            await Task.Delay(80, CancellationToken.None);
            client.Write(path);
            await client.FlushAsync(CancellationToken.None);
        }
    }
}