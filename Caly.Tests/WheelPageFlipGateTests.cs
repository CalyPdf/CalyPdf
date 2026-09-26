using Caly.Core.Controls;

namespace Caly.Tests;

public class WheelPageFlipGateTests
{
    private const ulong Cooldown = WheelPageFlipGate.FlipCooldownMs;
    private const ulong ResetGap = WheelPageFlipGate.OverscrollResetGapMs;
    private const double Threshold = WheelPageFlipGate.OverscrollThreshold;

    /// <summary>
    /// Sends whole wheel notches (delta 1) 10 ms apart and returns whether the last one flipped.
    /// </summary>
    private static bool Notches(WheelPageFlipGate gate, int count, ulong start, bool forward = true,
        bool atBoundary = true)
    {
        bool flipped = false;
        for (int i = 0; i < count; ++i)
        {
            flipped = gate.ShouldFlip(start + (ulong)(10 * i), atBoundary, 1, forward);
        }

        return flipped;
    }

    [Fact]
    public void NotAtBoundary_DoesNotFlip()
    {
        Assert.False(Notches(new WheelPageFlipGate(), 10, 1000, atBoundary: false));
    }

    [Fact]
    public void OneNotchAtTheBoundary_DoesNotFlip()
    {
        Assert.False(new WheelPageFlipGate().ShouldFlip(1000, atBoundary: true, 1, forward: true));
    }

    [Fact]
    public void EnoughOverscrollAtTheBoundary_Flips()
    {
        var gate = new WheelPageFlipGate();
        int notches = (int)Threshold;

        Assert.False(Notches(gate, notches - 1, 1000));
        Assert.True(gate.ShouldFlip(1000 + (ulong)(10 * notches), atBoundary: true, 1, forward: true));
    }

    [Fact]
    public void FractionalTrackpadDeltas_AddUp()
    {
        var gate = new WheelPageFlipGate();
        int events = (int)Math.Round(Threshold / 0.1);

        for (int i = 0; i < events - 1; ++i)
        {
            Assert.False(gate.ShouldFlip(1000 + (ulong)(5 * i), atBoundary: true, 0.1, forward: true));
        }

        // Floating-point sums of 0.1 fall just short of the threshold: it must still flip.
        Assert.True(gate.ShouldFlip(1000 + (ulong)(5 * events), atBoundary: true, 0.1, forward: true));
    }

    [Fact]
    public void LeavingTheBoundary_ResetsTheOverscroll()
    {
        var gate = new WheelPageFlipGate();
        int notches = (int)Threshold;

        Assert.False(Notches(gate, notches - 1, 1000));
        Assert.False(gate.ShouldFlip(1100, atBoundary: false, 1, forward: true)); // Scrolled back into the page
        Assert.False(Notches(gate, notches - 1, 1110));
        Assert.True(Notches(gate, 1, 1200));
    }

    [Fact]
    public void ReversingDirection_ResetsTheOverscroll()
    {
        var gate = new WheelPageFlipGate();
        int notches = (int)Threshold;

        Assert.False(Notches(gate, notches - 1, 1000, forward: true));
        Assert.False(Notches(gate, notches - 1, 1100, forward: false));
        Assert.True(Notches(gate, 1, 1200, forward: false));
    }

    [Fact]
    public void APause_ResetsTheOverscroll()
    {
        var gate = new WheelPageFlipGate();
        int notches = (int)Threshold;

        Assert.False(Notches(gate, notches - 1, 1000));
        ulong afterPause = 1000 + (ulong)(10 * (notches - 2)) + ResetGap + 1;
        Assert.False(Notches(gate, notches - 1, afterPause));
    }

    [Fact]
    public void AFlip_ResetsTheOverscroll_SoEachPageNeedsTheSameExtraScroll()
    {
        var gate = new WheelPageFlipGate();
        int notches = (int)Threshold;
        Assert.True(Notches(gate, notches, 1000));

        ulong afterCooldown = 1000 + Cooldown + 100;
        Assert.False(Notches(gate, notches - 1, afterCooldown));
        Assert.True(Notches(gate, 1, afterCooldown + (ulong)(10 * notches)));
    }

    [Fact]
    public void Cooldown_HoldsBackAFlip_UntilItEnds()
    {
        // A fling on a page that fits the viewport piles up overscroll far past the threshold:
        // the cooldown still limits it to one page per cooldown.
        var gate = new WheelPageFlipGate();
        int notches = (int)Threshold;
        Assert.True(Notches(gate, notches, 1000));
        ulong flippedAt = 1000 + (ulong)(10 * (notches - 1));

        Assert.False(Notches(gate, notches + 5, flippedAt + 10));
        Assert.True(gate.ShouldFlip(flippedAt + Cooldown, atBoundary: true, 1, forward: true));
    }

    [Fact]
    public void TimestampGoingBackwards_StartsAfresh()
    {
        var gate = new WheelPageFlipGate();
        int notches = (int)Threshold;
        Assert.True(Notches(gate, notches, 5000));

        // Clock reset: the cooldown ends, and the overscroll starts again from zero.
        Assert.False(Notches(gate, notches - 1, 10));
        Assert.True(Notches(gate, 1, 10 + (ulong)(10 * notches)));
    }

    [Fact]
    public void Reset_ClearsOverscrollAndCooldown()
    {
        var gate = new WheelPageFlipGate();
        int notches = (int)Threshold;
        Assert.True(Notches(gate, notches, 1000));
        Assert.False(Notches(gate, notches - 1, 1100));

        gate.Reset();

        Assert.False(Notches(gate, notches - 1, 1200));
        Assert.True(Notches(gate, 1, 1200 + (ulong)(10 * notches)));
    }
}
