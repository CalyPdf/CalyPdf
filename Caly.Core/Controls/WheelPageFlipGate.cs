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

namespace Caly.Core.Controls;

/// <summary>
/// Decides whether a mouse-wheel event at the top or bottom edge of a page turns the page in
/// single-page view.
/// </summary>
internal sealed class WheelPageFlipGate
{
    /// <summary>
    /// Minimum time between two page turns, measured from the previous turn. Milliseconds.
    /// </summary>
    public const ulong FlipCooldownMs = 250;

    /// <summary>
    /// How far to keep scrolling past the page edge before the page turns, in wheel notches
    /// (a mouse notch is a delta of 1; trackpads send fractions that add up).
    /// </summary>
    public const double OverscrollThreshold = 3;

    /// <summary>
    /// A pause longer than this between two wheel events at the edge starts the overscroll
    /// again from zero. Milliseconds.
    /// </summary>
    public const ulong OverscrollResetGapMs = 400;

    /// <summary>
    /// Absorbs floating-point error when fractional deltas add up to exactly the threshold.
    /// </summary>
    private const double ThresholdTolerance = 1e-9;

    private ulong? _lastFlipTimestamp;

    private double _overscroll;
    private bool _overscrollForward;
    private ulong _lastOverscrollTimestamp;

    /// <param name="timestamp">The wheel event's timestamp, in milliseconds.</param>
    /// <param name="atBoundary">Whether the page is already scrolled to its edge in the wheel's direction.</param>
    /// <param name="delta">The size of the wheel event, in notches (absolute value).</param>
    /// <param name="forward">Whether the wheel scrolls towards the next page.</param>
    public bool ShouldFlip(ulong timestamp, bool atBoundary, double delta, bool forward)
    {
        if (!atBoundary)
        {
            _overscroll = 0;
            return false;
        }

        // A timestamp going backwards (clock reset) starts afresh, like a pause does.
        bool continuesOverscroll = _overscroll > 0 &&
                                   forward == _overscrollForward &&
                                   timestamp >= _lastOverscrollTimestamp &&
                                   timestamp - _lastOverscrollTimestamp <= OverscrollResetGapMs;
        if (!continuesOverscroll)
        {
            _overscroll = 0;
        }

        _overscroll += delta;
        _overscrollForward = forward;
        _lastOverscrollTimestamp = timestamp;

        if (_overscroll < OverscrollThreshold - ThresholdTolerance)
        {
            return false;
        }

        // Enough overscroll, but too soon after the last turn: keep it, so the turn happens as
        // soon as the cooldown ends if the reader is still scrolling.
        if (_lastFlipTimestamp is { } last && timestamp >= last && timestamp - last < FlipCooldownMs)
        {
            return false;
        }

        _lastFlipTimestamp = timestamp;
        _overscroll = 0;
        return true;
    }

    public void Reset()
    {
        _lastFlipTimestamp = null;
        _overscroll = 0;
    }
}
