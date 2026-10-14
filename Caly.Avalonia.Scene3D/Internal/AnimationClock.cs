using System.Diagnostics;

namespace Caly.Avalonia.Scene3D.Internal;

/// <summary>
/// The playhead of a scene's animations: seconds that run while playing and hold while paused. It reads
/// no clock of its own; every call is given the timestamp it is to work with, which keeps it testable.
/// </summary>
internal sealed class AnimationClock
{
    private readonly double _ticksPerSecond;
    private double _seconds;
    private long _startedAt;

    public AnimationClock(long frequency = 0)
    {
        _ticksPerSecond = frequency > 0 ? frequency : Stopwatch.Frequency;
    }

    public bool IsPlaying { get; private set; }

    /// <summary>The playhead, in seconds, at <paramref name="timestamp"/>.</summary>
    public double Elapsed(long timestamp)
    {
        return IsPlaying ? _seconds + Math.Max(0, timestamp - _startedAt) / _ticksPerSecond : _seconds;
    }

    /// <summary>Runs the clock from where it stands.</summary>
    public void Play(long timestamp)
    {
        if (!IsPlaying)
        {
            IsPlaying = true;
            _startedAt = timestamp;
        }
    }

    /// <summary>Holds the clock where it is.</summary>
    public void Pause(long timestamp)
    {
        if (IsPlaying)
        {
            _seconds = Elapsed(timestamp);
            IsPlaying = false;
        }
    }

    /// <summary>Back to zero, running.</summary>
    public void Restart(long timestamp)
    {
        _seconds = 0;
        _startedAt = timestamp;
        IsPlaying = true;
    }

    /// <summary>Back to zero, stopped.</summary>
    public void Reset()
    {
        _seconds = 0;
        IsPlaying = false;
    }

    /// <summary>Puts the playhead at <paramref name="seconds"/>, running or not as it was.</summary>
    public void Seek(double seconds, long timestamp)
    {
        _seconds = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        _startedAt = timestamp;
    }
}
