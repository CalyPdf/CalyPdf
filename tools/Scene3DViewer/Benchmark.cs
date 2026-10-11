using System.Diagnostics;
using System.Globalization;
using Caly.Avalonia.Scene3D;

namespace Scene3DViewer;

/// <summary>
/// The <c>--benchmark</c> mode: once the model is on screen, turns it a little every frame, as a steady
/// drag would, and reports how long frames take.
/// <para>
/// "Frame" is the time spent in the control's render callback with the GPU made to finish before it
/// returns, so it covers issuing the draws and executing them. "Interval" is the time between
/// successive frames as the compositor paces them; it cannot go below the display's refresh period.
/// </para>
/// </summary>
internal sealed class Benchmark
{
    private const int WarmUpFrames = 30;

    private readonly Scene3DControl _control;
    private readonly int _frames;
    private readonly List<double> _frameTimes = [];
    private readonly List<double> _intervals = [];
    private long _allocated, _lastTick;
    private int _seen = -1;

    public Benchmark(Scene3DControl control, int frames)
    {
        _control = control;
        _frames = frames;
    }

    public bool IsDone { get; private set; }

    /// <summary>Call for every frame rendered once the scene is ready.</summary>
    public void OnFrame()
    {
        if (IsDone)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (_seen < 0)
        {
            _control.FinishEachFrame = true;
        }
        else if (_seen >= WarmUpFrames)
        {
            _frameTimes.Add(_control.Statistics.FrameMilliseconds);
            _intervals.Add(Stopwatch.GetElapsedTime(_lastTick, now).TotalMilliseconds);
            _allocated += _control.LastFrameAllocatedBytes;
        }

        _lastTick = now;
        _seen++;
        if (_frameTimes.Count >= _frames)
        {
            IsDone = true;
            _control.FinishEachFrame = false;
            return;
        }

        // A drag of a few pixels per frame, mostly sideways: every draw's matrices change each frame.
        _control.RotateBy(4, _seen % 120 < 60 ? 1 : -1);
    }

    public string Report(string model)
    {
        Scene3DStatistics s = _control.Statistics;
        _frameTimes.Sort();
        _intervals.Sort();
        double P(List<double> values, double q) => values[Math.Min(values.Count - 1, (int)(values.Count * q))];

        return string.Create(CultureInfo.InvariantCulture,
            $"| {model} | {s.DrawCalls} | {s.Triangles} | {s.Samples}x | {P(_frameTimes, 0.5):F1} | {P(_frameTimes, 0.95):F1} | {_frameTimes[^1]:F1} | {P(_intervals, 0.5):F1} | {P(_intervals, 0.95):F1} | {_allocated / (double)_frameTimes.Count:F0} | {s.TotalBytes / 1048576.0:F0} |");
    }

    public const string Header =
        "| model | draws | triangles | AA | frame p50 ms | frame p95 ms | frame max ms | interval p50 ms | interval p95 ms | managed bytes/frame | GPU MB |\n|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|";
}
