using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Caly.Avalonia.Scene3D;
using Scene3D;
using SkiaSharp;

namespace Scene3DViewer;

internal sealed class MainWindow : Window
{
    private readonly Options _options;
    private readonly Scene3DControl _view3D;
    private LoadedModel? _model;
    private bool _captureRequested, _firstFrameLogged;
    private Benchmark? _benchmark;

    public MainWindow(Options options)
    {
        _options = options;
        Title = "Scene3DViewer";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.DimGray;

        _view3D = new Scene3DControl { Width = options.Width, Height = options.Height };
        _view3D.FrameRendered += OnFrameRendered;
        _view3D.PropertyChanged += (_, e) =>
        {
            if (e.Property == Scene3DControl.StatusProperty)
            {
                Program.Log($"status: {_view3D.Status}{(_view3D.StatusMessage is { } message ? " — " + message : "")}");
            }
        };

        Content = _view3D;
        Load();
        Closed += (_, _) => _model?.Scene.Dispose();
    }

    private void Load()
    {
        try
        {
            _model = ModelSource.Load(_options.File!, _options.Artwork);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or Scene3DException)
        {
            Program.Log("cannot load: " + e.Message);
            Program.ExitCode = 2;
            Opened += (_, _) => Close();
            return;
        }

        Scene scene = _model.Scene;
        Program.Log(string.Create(CultureInfo.InvariantCulture,
            $"loaded {_model.Title} in {_model.LoadMilliseconds:F0} ms: {scene.Nodes.Length} nodes, {scene.Meshes.Length} meshes, {scene.Indices.Length / 3} triangles, {scene.Materials.Length} materials, {scene.Textures.Count} textures, {_model.Views.Count} views (default {_model.DefaultView})"));

        int viewIndex = _options.View ?? _model.DefaultView;
        SceneView? view = (uint)viewIndex < (uint)_model.Views.Count ? _model.Views[viewIndex] : null;
        Program.Log(view is null ? "view: default framing" : $"view {viewIndex}: '{view.Name}', lighting {view.Lighting}, {view.NodeOverrides.Count} node overrides");

        Title = $"Scene3DViewer — {_model.Title}";
        _view3D.View = view;
        _view3D.Scene = scene;
    }

    private void OnFrameRendered(object? sender, EventArgs e)
    {
        Scene3DStatistics s = _view3D.Statistics;
        if (!_firstFrameLogged && _view3D.Status == Scene3DStatus.Ready)
        {
            _firstFrameLogged = true;
            Program.Log("OpenGL: " + _view3D.GlDescription);
            Program.Log(string.Create(CultureInfo.InvariantCulture,
                $"first frame: {s.DrawCalls} draws, {s.Triangles} triangles, {s.CulledDraws} culled, {s.Samples}x samples via {_view3D.ResolvePath}, {s.FrameMilliseconds:F1} ms, GPU {s.TotalBytes / 1048576.0:F1} MB, {s.LiveGpuObjects} GPU objects"));
        }

        if (_options.BenchmarkFrames > 0 && _view3D.Status == Scene3DStatus.Ready)
        {
            _benchmark ??= new Benchmark(_view3D, _options.BenchmarkFrames);
            _benchmark.OnFrame();
            if (_benchmark.IsDone)
            {
                Program.Log(Benchmark.Header);
                Program.Log(_benchmark.Report(Path.GetFileNameWithoutExtension(_options.File!)));
                _options.BenchmarkFrames = 0;
                Avalonia.Threading.Dispatcher.UIThread.Post(Close);
            }

            return;
        }

        if (_options.Capture is { } path && _view3D.Status == Scene3DStatus.Ready && !_captureRequested)
        {
            // The frame after this one is read back: by then the scene is certainly on screen.
            _captureRequested = true;
            _view3D.CaptureNextFrame = (pixels, width, height) =>
            {
                SavePng(path, pixels, width, height);
                Program.Log($"captured {width}x{height} to {path}");
                Avalonia.Threading.Dispatcher.UIThread.Post(Close);
            };
            _view3D.RequestNextFrameRendering();
        }
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
