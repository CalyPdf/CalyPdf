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

namespace Caly.Core.Utilities;

/// <summary>
/// A sequence of cancellable generations where starting one cancels the previous.
/// </summary>
/// <remarks>
/// <para>
/// Used for "supersede the work I asked for last time" hand-offs: each caller gets a token that
/// stays live until the next caller arrives, is cancelled when it does, and is cancelled by the
/// main token or by <see cref="Dispose"/>.
/// </para>
/// <para>
/// Tokens are handed to work that outlives the call that created them - queued render requests
/// carry theirs until they are picked up - so a superseded source is deliberately never disposed.
/// Disposing one leaves work holding a token whose source can no longer be cancelled and whose
/// <see cref="CancellationToken.WaitHandle"/> throws. The sources are standalone rather than linked,
/// so an undisposed one holds no registration on anything else and the GC reclaims it once the last
/// request carrying its token is gone. The main token is honoured through a single registration
/// held for the lifetime of this object instead.
/// </para>
/// <para>
/// The swap happens under a lock rather than through separate reads of a field, so a generation can
/// never be replaced between being read and being cancelled - which is how one could previously end
/// up live with nothing left to cancel it.
/// </para>
/// </remarks>
internal sealed class CancellationGenerations : IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationToken _mainToken;
    private readonly CancellationTokenRegistration _mainRegistration;

    private CancellationTokenSource? _current;
    private bool _disposed;

    public CancellationGenerations(CancellationToken mainToken)
    {
        _mainToken = mainToken;
        _mainRegistration = mainToken.UnsafeRegister(
            static state => ((CancellationGenerations)state!).CancelCurrent(), this);
    }

    /// <summary>
    /// Cancels the current generation and starts a new one.
    /// </summary>
    /// <returns>
    /// The new generation's token, or an already cancelled token once this object is disposed or
    /// the main token has been cancelled.
    /// </returns>
    public async Task<CancellationToken> BeginAsync()
    {
        var next = new CancellationTokenSource();
        CancellationTokenSource? previous;

        lock (_gate)
        {
            if (_disposed || _mainToken.IsCancellationRequested)
            {
                next.Cancel();
                return next.Token;
            }

            previous = _current;
            _current = next;
        }

        // Outside the lock: cancellation callbacks run synchronously and must not hold it.
        if (previous is not null)
        {
            await previous.CancelAsync().ConfigureAwait(false);
        }

        // Dispose may have run while we were cancelling the previous generation. It takes the lock
        // and cancels whatever it finds, and it can only have found this one, so nothing is missed.
        return next.Token;
    }

    /// <summary>
    /// Cancels the current generation without starting a new one.
    /// </summary>
    public void CancelCurrent()
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            current = _current;
        }

        current?.Cancel();
    }

    /// <inheritdoc cref="CancelCurrent"/>
    public Task CancelCurrentAsync()
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            current = _current;
        }

        return current?.CancelAsync() ?? Task.CompletedTask;
    }

    public void Dispose()
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            current = _current;
            _current = null;
        }

        _mainRegistration.Dispose();
        current?.Cancel();
    }
}
