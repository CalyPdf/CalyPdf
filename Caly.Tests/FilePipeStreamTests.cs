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
    [Fact]
    public async Task CanReadCoalesced()
    {
        var server = new FilePipeStream();

        using var cts = new CancellationTokenSource();
        var received = new ConcurrentQueue<string>();

        var enumerable = server.ReceivePathAsync(cts.Token);

        var consumer = Task.Run(async () =>
        {
            try
            {
                await foreach (var path in enumerable)
                {
                    if (path is not null)
                    {
                        received.Enqueue(path);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, CancellationToken.None);

        await Task.Delay(300, CancellationToken.None); // let the server start listening

        await SendCoalesced("coalesced.pdf");
        await Task.Delay(500, CancellationToken.None);
        
        await SendChunkedWithDelays("chunked.pdf");
        await Task.Delay(500, CancellationToken.None);
        
        await cts.CancelAsync();
        await Task.WhenAny(consumer, Task.Delay(2000, CancellationToken.None));

        Assert.Contains("coalesced.pdf", received);
        Assert.Contains("chunked.pdf", received);

        async Task SendCoalesced(string filePath)
        {
            await using var client = new NamedPipeClientStream(".", "caly_pdf_files.pipe", PipeDirection.Out, PipeOptions.CurrentUserOnly);
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
            await using var client = new NamedPipeClientStream(".", "caly_pdf_files.pipe", PipeDirection.Out, PipeOptions.CurrentUserOnly);
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