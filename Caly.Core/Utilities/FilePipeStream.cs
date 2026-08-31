// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.Utilities;

/// <summary>
/// Pipe stream to communicate between application instances on the machine.
/// </summary>
public sealed class FilePipeStream : IDisposable, IAsyncDisposable
{
    // https://googleprojectzero.blogspot.com/2019/09/windows-exploitation-tricks-spoofing.html

    private const string PipeName = "caly_pdf_files.pipe";

    private static ReadOnlySpan<byte> KeyPhrase => "ca1y k3y pa$$"u8;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(5);

    private readonly MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;
    private readonly NamedPipeServerStream _pipeServer;
    private readonly TimeSpan _receiveTimeout;

    public FilePipeStream() : this(PipeName)
    {
    }

    /// <summary>
    /// Creates the server on a specific pipe name, with an optional per-message receive
    /// deadline override (used by tests).
    /// </summary>
    internal FilePipeStream(string pipeName, TimeSpan? receiveTimeout = null)
    {
#if DEBUG
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            pipeName = Guid.NewGuid().ToString();
        }
#endif
        // Unix maps a named pipe onto a Unix domain socket at '<temp>/CoreFxPipe_<pipeName>',
        // and the whole path must fit in sun_path: 104 characters on macOS/BSD, 108 on Linux.
        //
        // A macOS temp path ('/var/folders/xx/<29 chars>/T/') takes about 49 of those and
        // 'CoreFxPipe_' 11 more, leaving 44 for the name.
        System.Diagnostics.Debug.Assert(pipeName.Length <= 44);

        _receiveTimeout = receiveTimeout ?? ReceiveTimeout;
        _pipeServer = new(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
    }

    public async IAsyncEnumerable<string?> ReceivePathAsync([EnumeratorCancellation] CancellationToken token)
    {
        while (true)
        {
            string? path = null;

            try
            {
                token.ThrowIfCancellationRequested();

                // https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-use-named-pipes-for-network-interprocess-communication
                await _pipeServer.WaitForConnectionAsync(token);

                using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                receiveCts.CancelAfter(_receiveTimeout);
                var receiveToken = receiveCts.Token;

                ushort len;
                using (var lengthMemoryOwner = _memoryPool.Rent(sizeof(ushort)))
                {
                    Memory<byte> lengthBuffer = lengthMemoryOwner.Memory.Slice(0, sizeof(ushort));
                    await _pipeServer.ReadExactlyAsync(lengthBuffer, receiveToken);
                    len = BitConverter.ToUInt16(lengthBuffer.Span);
                }

                if (len == 0)
                {
                    // TODO - Log
                    continue;
                }

                using (var memoryOwner = _memoryPool.Rent(Math.Max(KeyPhrase.Length, len)))
                {
                    Memory<byte> buffer = memoryOwner.Memory;

                    // Read key phrase
                    await _pipeServer.ReadExactlyAsync(buffer.Slice(0, KeyPhrase.Length), receiveToken);

                    // Check key phrase
                    if (!buffer.Span.Slice(0, KeyPhrase.Length).SequenceEqual(KeyPhrase))
                    {
                        // TODO - Log
                        continue;
                    }

                    // Read message type
                    await _pipeServer.ReadExactlyAsync(buffer.Slice(0, 1), receiveToken);

                    PipeMessageType messageType = (PipeMessageType)buffer.Span[0];
                    switch (messageType)
                    {
                        case PipeMessageType.FilePath:
                        {
                            // Read file path
                            await _pipeServer.ReadExactlyAsync(buffer.Slice(0, len), receiveToken);
                        }
                            break;

                        case PipeMessageType.Command:
                        {
                            await _pipeServer.ReadExactlyAsync(buffer.Slice(0, 1), receiveToken);
                            ProcessMessageCommand((PipeCommandMessageType)buffer.Span[0]);
                        }
                            break;

                        default:
                            // TODO - Log
                            break;
                    }

                    path = messageType == PipeMessageType.FilePath
                        ? Encoding.UTF8.GetString(buffer.Span.Slice(0, len))
                        : null;
                }
            }
            catch (OperationCanceledException)
            {
                // Handled below: cancellation ends the enumeration
            }
            catch (EndOfStreamException)
            {
                // Client closed mid-message. Drop it and keep listening
            }
            catch (Exception e)
            {
                Debug.WriteExceptionToFile(e);
                throw;
            }
            finally
            {
                // Reset the server for the next client. Checking IsConnected is not
                // enough: a client that broke the pipe mid-message leaves the server
                // in the Broken state (IsConnected == false), yet the OS pipe still
                // needs the disconnect before WaitForConnectionAsync can accept a
                // new client.
                try
                {
                    _pipeServer.Disconnect();
                }
                catch (InvalidOperationException)
                {
                    // Never connected (e.g. cancelled while waiting for a connection)
                }
            }

            if (token.IsCancellationRequested)
            {
                yield break;
            }

            if (!string.IsNullOrEmpty(path))
            {
                yield return path;
            }
        }
    }

    private static void ProcessMessageCommand(PipeCommandMessageType commandType)
    {
        switch (commandType)
        {
            case PipeCommandMessageType.BringToFront:
                App.Current?.TryBringToFront();
                break;

            default:
                // TODO - Log
                break;
        }
    }

    public void Dispose()
    {
        _pipeServer.Dispose();
        _memoryPool.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _pipeServer.DisposeAsync();
        _memoryPool.Dispose();
    }

    public static bool SendBringToFront()
    {
        try
        {
            using (var pipeClient = new NamedPipeClientStream(".", PipeName,
                       PipeDirection.Out, PipeOptions.CurrentUserOnly,
                       TokenImpersonationLevel.Identification))
            {
                pipeClient.Connect(ConnectTimeout); // If you are getting a timeout in debug mode, just re-run Caly

                Memory<byte> lengthBytes = BitConverter.GetBytes((ushort)1);
                pipeClient.Write(lengthBytes.Span);
                pipeClient.Write(KeyPhrase);
                pipeClient.WriteByte((byte)PipeMessageType.Command);
                pipeClient.WriteByte((byte)PipeCommandMessageType.BringToFront);

                pipeClient.Flush();
            }

            return true;
        }
        catch (UnauthorizedAccessException uae)
        {
            // Server must be running in admin, but not the client
            // Handle the case and display error message
            Debug.WriteExceptionToFile(uae);
            throw;
        }
        catch (TimeoutException toe)
        {
            // Could not connect to the running instance of Caly
            // probably because it is actually not running, i.e. the 
            // lock file was not properly deleted after close
            CalyFileMutex.ForceReleaseMutex();
            throw ThrowOnTimeoutException(toe);
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
            throw;
        }
    }

    public static bool SendPath(string filePath)
    {
        try
        {
            using (var pipeClient = new NamedPipeClientStream(".", PipeName,
                       PipeDirection.Out, PipeOptions.CurrentUserOnly,
                       TokenImpersonationLevel.Identification))
            {
                pipeClient.Connect(ConnectTimeout);

                Memory<byte> pathBytes = Encoding.UTF8.GetBytes(filePath);
                if (pathBytes.Length > ushort.MaxValue)
                {
                    throw new PathTooLongException($"The pdf file path passed to Caly is too long. Received {pathBytes.Length} bytes, and maximum size is {ushort.MaxValue}.");
                }

                Memory<byte> lengthBytes = BitConverter.GetBytes((ushort)pathBytes.Length);
                pipeClient.Write(lengthBytes.Span);
                pipeClient.Write(KeyPhrase);
                pipeClient.WriteByte((byte)PipeMessageType.FilePath);
                pipeClient.Write(pathBytes.Span);

                pipeClient.Flush();
            }

            return true;
        }
        catch (UnauthorizedAccessException uae)
        {
            // Server must be running in admin, but not the client
            // Handle the case and display error message
            Debug.WriteExceptionToFile(uae);
            throw;
        }
        catch (TimeoutException toe)
        {
            // Could not connect to the running instance of Caly
            // probably because it is actually not running, i.e. the 
            // lock file was not properly deleted after close
            CalyFileMutex.ForceReleaseMutex();
            throw ThrowOnTimeoutException(toe);
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
            throw;
        }
    }

    private static CalyCriticalException ThrowOnTimeoutException(TimeoutException toe)
    {
        return new CalyCriticalException("Could not connect to the running instance of Caly," +
                                        " probably because it is actually not running, i.e. the" +
                                        " Caly lock was not properly removed after close.", toe)
        {
            TryRestartApp = true
        };
    }

    private enum PipeMessageType : byte
    {
        None = 0,
        FilePath = 1,
        Command = 2
    }

    private enum PipeCommandMessageType : byte
    {
        None = 0,
        BringToFront = 1
    }
}
