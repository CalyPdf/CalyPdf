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

using Avalonia.Threading;
using System.Diagnostics;

namespace Caly.UiTests.Support;

internal static class Pump
{
    /// <summary>
    /// Repeatedly drains the Avalonia dispatcher and yields control while polling
    /// <paramref name="predicate"/>. Fails the test if the predicate has not become
    /// true within <paramref name="timeout"/>.
    /// </summary>
    public static async Task UntilAsync(Func<bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = timeout ?? TimeSpan.FromSeconds(5);

        var sw = Stopwatch.StartNew();
        while (!predicate())
        {
            if (sw.Elapsed > deadline)
            {
                throw new Xunit.Sdk.XunitException($"Pump.UntilAsync timed out after {deadline.TotalSeconds:F1}s waiting for predicate.");
            }

            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Dispatcher.UIThread.RunJobs();
    }
}
