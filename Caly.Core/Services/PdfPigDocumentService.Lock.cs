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
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.Services;

internal partial class PdfPigDocumentService
{
    private long _isDisposed;
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
            Interlocked.Decrement(ref _activeOperations);
        }
    }

    private async Task<T?> GuardDispose<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        Interlocked.Increment(ref _activeOperations);
        try
        {
            if (IsDisposed())
            {
                return default;
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
            Interlocked.Decrement(ref _activeOperations);
        }

        return default;
    }

    private bool IsDisposed()
    {
        return Interlocked.Read(ref _isDisposed) != 0;
    }
}
