using Caly.Avalonia.Scene3D.Internal;

namespace Caly.Avalonia.Scene3D.Tests;

public class AnimationClockTests
{
    // One tick is one millisecond here.
    private static AnimationClock Clock() => new(frequency: 1000);

    [Fact]
    public void ANewClockStandsAtZero()
    {
        AnimationClock clock = Clock();

        Assert.False(clock.IsPlaying);
        Assert.Equal(0, clock.Elapsed(5000));
    }

    [Fact]
    public void ThePlayingClockAdvancesWithTheTimestamps()
    {
        AnimationClock clock = Clock();

        clock.Play(1000);

        Assert.True(clock.IsPlaying);
        Assert.Equal(0, clock.Elapsed(1000), 6);
        Assert.Equal(2.5, clock.Elapsed(3500), 6);
    }

    [Fact]
    public void PausingKeepsTheTimeAndPlayingContinuesFromIt()
    {
        AnimationClock clock = Clock();
        clock.Play(0);

        clock.Pause(2000);

        Assert.False(clock.IsPlaying);
        Assert.Equal(2, clock.Elapsed(9000), 6);

        clock.Play(10000);
        Assert.Equal(3, clock.Elapsed(11000), 6);
    }

    [Fact]
    public void PlayingAPlayingClockChangesNothing()
    {
        AnimationClock clock = Clock();
        clock.Play(0);

        clock.Play(5000);

        Assert.Equal(6, clock.Elapsed(6000), 6);
    }

    [Fact]
    public void PausingAPausedClockChangesNothing()
    {
        AnimationClock clock = Clock();
        clock.Play(0);
        clock.Pause(1000);

        clock.Pause(4000);

        Assert.Equal(1, clock.Elapsed(4000), 6);
    }

    [Fact]
    public void RestartingGoesBackToZeroAndPlays()
    {
        AnimationClock clock = Clock();
        clock.Play(0);

        clock.Restart(7000);

        Assert.True(clock.IsPlaying);
        Assert.Equal(0, clock.Elapsed(7000), 6);
        Assert.Equal(1, clock.Elapsed(8000), 6);
    }

    [Fact]
    public void ResettingGoesBackToZeroAndStops()
    {
        AnimationClock clock = Clock();
        clock.Play(0);

        clock.Reset();

        Assert.False(clock.IsPlaying);
        Assert.Equal(0, clock.Elapsed(9000));
    }

    [Fact]
    public void SeekingMovesThePlayhead()
    {
        AnimationClock clock = Clock();
        clock.Play(0);

        clock.Seek(10, 3000);

        Assert.True(clock.IsPlaying);
        Assert.Equal(10, clock.Elapsed(3000), 6);
        Assert.Equal(11, clock.Elapsed(4000), 6);
    }

    [Fact]
    public void ATimestampBeforeThePlayStartDoesNotRunBackwards()
    {
        AnimationClock clock = Clock();
        clock.Play(5000);

        Assert.Equal(0, clock.Elapsed(1000));
    }
}
