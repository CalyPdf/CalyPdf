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
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.Services;

internal partial class PdfPigDocumentService
{
    private long _isDisposed;
    private long _resourcesReleased;
    private int _activeOperations;

    // PdfPig only allow to read 1 page at a time for now
    // NB: Initial count set to 0 to make sure the document is opened before anything else starts.
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(0, 1);
    private readonly CancellationTokenSource _mainCts = new();
    private readonly CancellationToken _mainToken;
    
    private async Task<T?> ExecuteWithLockAsync<T>(Func<CancellationToken, T> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (IsDisposed())
        {
            return default;
        }

        bool hasLock = false;
        try
        {
            await _semaphore.WaitAsync(token).ConfigureAwait(false);
            hasLock = true;

            if (IsDisposed())
            {
                return default;
            }

            token.ThrowIfCancellationRequested();
            return action(token);
        }
        finally
        {
            if (hasLock && !IsDisposed())
            {
                _semaphore.Release();
            }
        }
    }

    private async Task GuardDispose(Func<CancellationToken, Task> action, CancellationToken token)
    {
        Interlocked.Increment(ref _activeOperations);
        try
        {
            if (IsDisposed())
            {
                return;
            }

            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _mainToken))
            {
                linkedCts.Token.ThrowIfCancellationRequested();
                await action(linkedCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        { }
        finally
        {
            if (Interlocked.Decrement(ref _activeOperations) == 0 && IsDisposed())
            {
                // Dispose gave up waiting for us and deferred the teardown.
                await ReleaseResourcesAsync().ConfigureAwait(false);
            }
        }
    }

    private Task<T?> GuardDispose<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        return GuardDispose<T>(action, () => default!, () => default!, token);
    }

    private async Task<T?> GuardDispose<T>(Func<CancellationToken, Task<T>> action, Func<T> disposed, Func<T> canceled, CancellationToken token)
    {
        Interlocked.Increment(ref _activeOperations);
        try
        {
            if (IsDisposed())
            {
                return disposed();
            }

            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _mainToken))
            {
                linkedCts.Token.ThrowIfCancellationRequested();
                return await action(linkedCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        { }
        finally
        {
            if (Interlocked.Decrement(ref _activeOperations) == 0 && IsDisposed())
            {
                // Dispose gave up waiting for us and deferred the teardown.
                await ReleaseResourcesAsync().ConfigureAwait(false);
            }
        }

        return canceled();
    }

    private bool IsDisposed()
    {
        return Interlocked.Read(ref _isDisposed) != 0;
    }

    /// <summary>
    /// Releases the document's resources, but only once no operation is still using them.
    /// <para>
    /// If an operation is in flight this does nothing: the last one out calls
    /// <see cref="ReleaseResourcesAsync"/> from its own finally block. Tearing the stream down
    /// under a running parse is what produced
    /// <c>ObjectDisposedException: Cannot access a closed file</c> - <see cref="PdfDocument.Open"/>
    /// is a long synchronous parse that cancellation cannot interrupt, so it can easily outlive
    /// any bounded wait.
    /// </para>
    /// </summary>
    internal async ValueTask ReleaseResourcesIfIdleAsync()
    {
        if (Interlocked.CompareExchange(ref _activeOperations, 0, 0) > 0)
        {
            return;
        }

        await ReleaseResourcesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Tears down the stream, storage file and parsed document. Idempotent: whichever of the
    /// dispose path or the last in-flight operation gets here first does the work.
    /// </summary>
    private async ValueTask ReleaseResourcesAsync()
    {
        if (Interlocked.CompareExchange(ref _resourcesReleased, 1, 0) != 0)
        {
            return;
        }

        _semaphore.Dispose();

        // Document before stream: the document reads through the stream, so closing the stream
        // first is exactly the ordering that caused the bug this guards against.
        if (_document is not null)
        {
            _document.Dispose();
            _document = null;
        }

        if (_fileStream is not null)
        {
            await _fileStream.DisposeAsync().ConfigureAwait(false);
            _fileStream = null;
        }

        _storageFile?.Dispose();
        _storageFile = null;

        _mainCts.Dispose();
    }
}
