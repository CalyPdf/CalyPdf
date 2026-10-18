using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Caly.Scene3D.Avalonia;
using Caly.Rendering.Scene3D;
using SkiaSharp;

namespace Scene3DViewer;

internal sealed class MainWindow : Window
{
    private readonly Options _options;
    private readonly Scene3DControl _view3D = new();
    private readonly ZoomedViewHost _host = new();
    private readonly ComboBox _views = new() { MinWidth = 220, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _statistics = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gainsboro, FontFamily = new FontFamily("Consolas, monospace") };
    private LoadedModel? _model;
    private bool _captureRequested, _firstFrameLogged, _texturesLogged, _scenarioStarted;
    private long _readyAt;
    private Benchmark? _benchmark;

    public MainWindow(Options options)
    {
        _options = options;
        Title = "Scene3DViewer";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(48, 48, 48));

        if (!Load())
        {
            return;
        }

        // The 3D view has the proportions the document gives it, unless a size was asked for.
        double height = options.Height ?? 700;
        double width = options.Width ?? Math.Round(height * (_model!.AspectRatio > 0 ? Math.Clamp(_model.AspectRatio, 0.4, 2.5) : 4.0 / 3));
        _host.Width = width;
        _host.Height = height;
        _host.Zoom = options.Zoom;
        _host.Centre = options.Centre;
        _host.Children.Add(_view3D);
        if (options.Transparent)
        {
            // Stripes behind the control: with a transparent background they must be seen around the model.
            _view3D.DefaultBackground = Colors.Transparent;
            _host.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(40, 40, RelativeUnit.Absolute),
                SpreadMethod = GradientSpreadMethod.Repeat,
                GradientStops = { new GradientStop(Colors.Gold, 0), new GradientStop(Colors.Gold, 0.5), new GradientStop(Colors.SteelBlue, 0.5), new GradientStop(Colors.SteelBlue, 1) }
            };
        }

        _view3D.FrameRendered += OnFrameRendered;
        _view3D.PropertyChanged += (_, e) =>
        {
            if (e.Property == Scene3DControl.StatusProperty)
            {
                OnStatusChanged();
            }
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (_model.Poster is { } poster)
        {
            // The document's own picture, at the size of the live view, for comparing the two.
            content.Children.Add(new Image { Source = poster, Width = width, Height = height, Stretch = Stretch.Uniform });
        }

        content.Children.Add(_host);

        var zoom = new Slider { Minimum = 1, Maximum = 8, Value = options.Zoom, Width = 160, VerticalAlignment = VerticalAlignment.Center };
        zoom.ValueChanged += (_, e) => _host.Zoom = e.NewValue;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(8) };
        bar.Children.Add(_views);
        bar.Children.Add(new TextBlock { Text = "page zoom", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gainsboro });
        bar.Children.Add(zoom);
        bar.Children.Add(_statistics);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(content);
        Content = root;

        // Views: the document's, then the default framing.
        foreach (SceneView view in _model.Views)
        {
            _views.Items.Add(string.IsNullOrEmpty(view.Name) ? "(unnamed view)" : view.Name);
        }

        _views.Items.Add("(whole scene)");
        int viewIndex = options.View ?? _model.DefaultView;
        _views.SelectedIndex = (uint)viewIndex < (uint)_model.Views.Count ? viewIndex : _model.Views.Count;
        _views.SelectionChanged += (_, _) => ApplyView();

        ApplyView();
        _view3D.Scene = _model.Scene;
        if (_model.Scene.Animations.Count > 0)
        {
            Program.Log(string.Create(CultureInfo.InvariantCulture, $"animations: {_model.Scene.Animations.Count}, {_model.Scene.AnimationDuration:0.###} s, {(_model.Scene.AutoPlayAnimations ? "plays at once" : "waits for play")}"));
        }

        if (options.AnimationTime is { } animationTime)
        {
            _view3D.SeekAnimation(animationTime);
            _view3D.PauseAnimation();
        }

        Closed += (_, _) => _model?.Scene.Dispose();
        if (options.ExitAfterSeconds > 0)
        {
            DispatcherTimer.RunOnce(Close, TimeSpan.FromSeconds(options.ExitAfterSeconds));
        }
    }

    private bool Load()
    {
        try
        {
            _model = ModelSource.Load(_options.File!, _options.Artwork, _options.Poster ? 1200 : 0);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or Scene3DException)
        {
            Program.Log("cannot load: " + e.Message);
            Program.ExitCode = 2;
            Opened += (_, _) => Dispatcher.UIThread.Post(Close);
            return false;
        }

        Scene scene = _model.Scene;
        Program.Log(string.Create(CultureInfo.InvariantCulture,
            $"loaded {_model.Title} in {_model.LoadMilliseconds:F0} ms: {scene.Nodes.Length} nodes, {scene.Meshes.Length} meshes, {scene.Indices.Length / 3} triangles, {scene.Materials.Length} materials, {scene.Textures.Count} textures, {_model.Views.Count} views (default {_model.DefaultView}), poster {(_model.Poster is null ? "none" : "drawn")}"));
        Title = $"Scene3DViewer — {_model.Title}";
        return true;
    }

    private void ApplyView()
    {
        int index = _views.SelectedIndex;
        SceneView? view = _model is not null && (uint)index < (uint)_model.Views.Count ? _model.Views[index] : null;
        if (view is not null && _options.Transparent)
        {
            // The same view without its background colour, so that the control's transparent default applies.
            view = new SceneView
            {
                Name = view.Name, CameraToWorld = view.CameraToWorld, CameraNodePath = view.CameraNodePath, OrbitDistance = view.OrbitDistance,
                Projection = view.Projection, Lighting = view.Lighting, NodeOverrides = view.NodeOverrides
            };
        }

        Program.Log(view is null ? "view: whole scene" : $"view {index}: '{view.Name}', lighting {view.Lighting}, {view.NodeOverrides.Count} node overrides, camera {(view.CameraNodePath.Count > 0 ? "node " + string.Join('/', view.CameraNodePath) : view.CameraToWorld is null ? "none" : "matrix")}");
        _view3D.View = view;
    }

    private void OnStatusChanged()
    {
        Program.Log($"status: {_view3D.Status}{(_view3D.StatusMessage is { } message ? " — " + message : "")}");

        // The control lost its GPU resources after the scene had given up its geometry: decode again,
        // as Caly will when a page with an active view comes back.
        if (_view3D.Status == Scene3DStatus.NeedsScene && _model is not null)
        {
            Scene old = _model.Scene;
            Dispatcher.UIThread.Post(() =>
            {
                if (_view3D.Status == Scene3DStatus.NeedsScene && Load())
                {
                    _view3D.Scene = _model.Scene;
                    old.Dispose();
                }
            });
        }
    }

    private void OnFrameRendered(object? sender, EventArgs e)
    {
        Scene3DStatistics s = _view3D.Statistics;
        if (_view3D.Status != Scene3DStatus.Ready)
        {
            return;
        }

        if (!_firstFrameLogged)
        {
            _firstFrameLogged = true;
            _texturesLogged = false;
            _readyAt = System.Diagnostics.Stopwatch.GetTimestamp();
            Program.Log("OpenGL: " + _view3D.GlDescription);
            Program.Log(string.Create(CultureInfo.InvariantCulture,
                $"first frame: {s.DrawCalls} draws, {s.Triangles} triangles, {s.CulledDraws} culled, {s.Samples}x samples via {_view3D.ResolvePath}, {s.FrameMilliseconds:F1} ms, GPU {s.TotalBytes / 1048576.0:F1} MB, {s.LiveGpuObjects} GPU objects"));
        }

        // Textures arrive after the first frame; everything below waits until the model is complete.
        if (s.TexturesPending > 0)
        {
            return;
        }

        if (!_texturesLogged)
        {
            _texturesLogged = true;
            Program.Log(string.Create(CultureInfo.InvariantCulture,
                $"textures: {s.TexturesLoaded} loaded, {s.TexturesFailed} failed, {s.TextureBytes / 1048576.0:F1} MB, complete {System.Diagnostics.Stopwatch.GetElapsedTime(_readyAt).TotalMilliseconds:F0} ms after the first frame; frame {s.FramesRendered}"));
        }

        if (_options.BenchmarkFrames > 0)
        {
            _benchmark ??= new Benchmark(_view3D, _options.BenchmarkFrames);
            _benchmark.OnFrame();
            if (_benchmark.IsDone)
            {
                Program.Log(Benchmark.Header);
                Program.Log(_benchmark.Report(Path.GetFileNameWithoutExtension(_options.File!)));
                _options.BenchmarkFrames = 0;
                Dispatcher.UIThread.Post(Close);
            }

            return;
        }

        _statistics.Text = string.Create(CultureInfo.InvariantCulture,
            $"{s.DrawCalls} draws  {s.Triangles / 1000.0:F0}k tris  {s.CulledDraws} culled  {s.Samples}x  {s.FrameMilliseconds:F1} ms  GPU {s.TotalBytes / 1048576.0:F0} MB  {s.TexturesLoaded} textures  frame {s.FramesRendered}");

        if (_options.Scenario == "recycle" && !_scenarioStarted)
        {
            _scenarioStarted = true;
            RunRecycleScenario();
        }

        if (_options.Capture is { } path && !_captureRequested)
        {
            // The frame after this one is read back: by then the scene is certainly on screen.
            _captureRequested = true;
            _view3D.CaptureNextFrame = (pixels, width, height) =>
            {
                SavePng(path, pixels, width, height);

                // With --poster, the document's own picture goes beside the capture, for comparing the two.
                _model?.Poster?.Save(Path.ChangeExtension(path, ".poster.png"));
                Program.Log(string.Create(CultureInfo.InvariantCulture,
                    $"captured {width}x{height} to {path} (page zoom {_host.Zoom:0.##}, region {_view3D.VisibleRegion})"));
                if (_options.ExitAfterSeconds <= 0)
                {
                    Dispatcher.UIThread.Post(Close);
                }
            };
            _view3D.RequestNextFrameRendering();
        }
    }

    /// <summary>
    /// What happens to a 3D view on a page that scrolls out of the realised range and back: the control
    /// leaves the visual tree and returns.
    /// </summary>
    private void RunRecycleScenario()
    {
        void At(double seconds, Action action) => DispatcherTimer.RunOnce(action, TimeSpan.FromSeconds(seconds));

        At(0.8, () =>
        {
            int before = _view3D.Statistics.LiveGpuObjects;
            _host.Children.Remove(_view3D);
            Program.Log($"scenario: control detached; GPU objects {before} -> {_view3D.LiveGpuObjectsAfterRelease} after release; scene still has geometry: {_model!.Scene.HasGeometry}");
        });
        At(1.8, () =>
        {
            _firstFrameLogged = false;
            _host.Children.Add(_view3D);
            Program.Log("scenario: control re-attached");
        });
        At(3.6, () =>
        {
            Program.Log($"scenario: end; status {_view3D.Status}, {_view3D.Statistics.DrawCalls} draws in the last frame, {_view3D.Statistics.LiveGpuObjects} GPU objects");
            Program.ExitCode = _view3D.Status == Scene3DStatus.Ready && _view3D.LiveGpuObjectsAfterRelease == 0 ? 0 : 3;
            Close();
        });
    }

    /// <summary>Saves RGBA pixels (premultiplied, rows top first) as a PNG.</summary>
    private static unsafe void SavePng(string path, byte[] pixels, int width, int height)
    {
        fixed (byte* source = pixels)
        {
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var pixmap = new SKPixmap(info, (IntPtr)source);
            using SKData data = pixmap.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream output = File.Create(path);
            data.SaveTo(output);
        }
    }
}
