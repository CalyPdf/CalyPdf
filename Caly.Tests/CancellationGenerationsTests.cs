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

using Caly.Core.Utilities;

namespace Caly.Tests;

public class CancellationGenerationsTests
{
    [Fact]
    public async Task SupersededTokenStaysUsable()
    {
        using var generations = new CancellationGenerations(CancellationToken.None);

        CancellationToken superseded = await generations.BeginAsync();
        await generations.BeginAsync();

        Assert.True(superseded.IsCancellationRequested);

        // Render requests already queued still carry the superseded token. Its source must not
        // have been disposed out from under them.
        Assert.True(superseded.WaitHandle.WaitOne(0));
    }

    [Fact]
    public async Task GenerationBegunAfterDisposeIsAlreadyCancelled()
    {
        var generations = new CancellationGenerations(CancellationToken.None);
        generations.Dispose();

        // A refresh already in flight when the service is torn down still calls in. It must get a
        // dead token rather than a live generation nothing will ever cancel.
        CancellationToken token = await generations.BeginAsync();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task BeginAsyncCancelsThePreviousGeneration()
    {
        using var generations = new CancellationGenerations(CancellationToken.None);

        CancellationToken first = await generations.BeginAsync();
        Assert.False(first.IsCancellationRequested);

        CancellationToken second = await generations.BeginAsync();

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
    }

    [Fact]
    public async Task MainTokenCancellationCancelsTheCurrentGeneration()
    {
        using var mainCts = new CancellationTokenSource();
        using var generations = new CancellationGenerations(mainCts.Token);

        CancellationToken token = await generations.BeginAsync();
        Assert.False(token.IsCancellationRequested);

        await mainCts.CancelAsync();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task BeginAsyncReturnsCancelledTokenWhenMainTokenAlreadyCancelled()
    {
        using var mainCts = new CancellationTokenSource();
        await mainCts.CancelAsync();

        using var generations = new CancellationGenerations(mainCts.Token);

        CancellationToken token = await generations.BeginAsync();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task DisposeCancelsTheCurrentGeneration()
    {
        var generations = new CancellationGenerations(CancellationToken.None);

        CancellationToken token = await generations.BeginAsync();
        Assert.False(token.IsCancellationRequested);

        generations.Dispose();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task CancelCurrentAsyncCancelsWithoutStartingANewGeneration()
    {
        using var generations = new CancellationGenerations(CancellationToken.None);

        CancellationToken token = await generations.BeginAsync();

        await generations.CancelCurrentAsync();
        Assert.True(token.IsCancellationRequested);

        // Still usable afterwards: the next refresh gets a live generation.
        CancellationToken next = await generations.BeginAsync();
        Assert.False(next.IsCancellationRequested);
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var generations = new CancellationGenerations(CancellationToken.None);

        generations.Dispose();
        generations.Dispose();
    }

    [Fact]
    public async Task ConcurrentBeginAsyncLeavesExactlyOneLiveGeneration()
    {
        using var generations = new CancellationGenerations(CancellationToken.None);

        CancellationToken[] tokens = await Task.WhenAll(
            Enumerable.Range(0, 64).Select(_ => Task.Run(async () => await generations.BeginAsync())));

        // Whoever wins the race, every other generation must have been cancelled - none may be
        // left live with nothing to cancel it - and every token must still be usable.
        Assert.Equal(1, tokens.Count(t => !t.IsCancellationRequested));
        Assert.All(tokens, t => t.WaitHandle.WaitOne(0));
    }

    [Fact]
    public async Task CancelCurrentAsyncAfterDisposeIsSafe()
    {
        var generations = new CancellationGenerations(CancellationToken.None);
        CancellationToken token = await generations.BeginAsync();

        generations.Dispose();

        // CancelAndClear can run after the service was torn down. This used to need an
        // ObjectDisposedException catch; it must simply be safe now.
        await generations.CancelCurrentAsync();

        Assert.True(token.IsCancellationRequested);
        Assert.True(token.WaitHandle.WaitOne(0));
    }
}
