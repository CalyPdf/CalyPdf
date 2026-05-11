// Copyright (c) BobLd
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

    private readonly MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;
    private readonly NamedPipeServerStream _pipeServer;

    public FilePipeStream(bool singleInstance)
    {
        if (singleInstance)
        {
            _pipeServer = new(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
            return;
        }

        _pipeServer = new($"caly_pdf_files_{Guid.NewGuid()}.pipe", PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.CurrentUserOnly);
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

                ushort len = 0;
                using (var lengthMemoryOwner = _memoryPool.Rent(Math.Max(KeyPhrase.Length, len)))
                {
                    Memory<byte> lengthBuffer = lengthMemoryOwner.Memory;
                    if (await _pipeServer.ReadAsync(lengthBuffer, token) != 2)
                    {
                        // TODO - Log
                        continue;
                    }

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
                    if (await _pipeServer.ReadAsync(buffer.Slice(0, KeyPhrase.Length), token) != KeyPhrase.Length)
                    {
                        // TODO - Log
                        continue;
                    }

                    // Check key phrase
                    if (!buffer.Span.Slice(0, KeyPhrase.Length).SequenceEqual(KeyPhrase))
                    {
                        // TODO - Log
                        continue;
                    }

                    // Read message type
                    if (await _pipeServer.ReadAsync(buffer.Slice(0, 1), token) != 1)
                    {
                        // TODO - Log
                        continue;
                    }

                    PipeMessageType messageType = (PipeMessageType)buffer.Span[0];
                    switch (messageType)
                    {
                        case PipeMessageType.FilePath:
                            {
                                // Read file path
                                if (await _pipeServer.ReadAsync(buffer.Slice(0, len), token) != len)
                                {
                                    // TODO - Log
                                    continue;
                                }
                            }
                            break;

                        case PipeMessageType.Command:
                            {
                                if (await _pipeServer.ReadAsync(buffer.Slice(0, 1), token) != 1)
                                {
                                    // TODO - Log
                                    continue;
                                }

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
                // No op
            }
            catch (Exception e)
            {
                Debug.WriteExceptionToFile(e);
                throw;
            }
            finally
            {
                // We are not connected if operation was canceled
                if (_pipeServer.IsConnected)
                {
                    _pipeServer.Disconnect();
                }
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
