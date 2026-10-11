using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Caly.Avalonia.Scene3D.Internal;
using Caly.Avalonia.Scene3D.Internal.Gl;
using Scene3D;

namespace Caly.Avalonia.Scene3D;

/// <summary>
/// Draws a <see cref="Scene"/> with OpenGL, for a <see cref="SceneView"/> or a default view of the whole
/// scene.
/// <para>
/// The control never disposes the scene. Once the scene's geometry is on the GPU the control asks the
/// scene to release its copy (see <see cref="ReleaseGeometryAfterUpload"/>); if the control then loses its
/// GPU resources, <see cref="Status"/> becomes <see cref="Scene3DStatus.NeedsScene"/>.
/// </para>
/// <para>
/// It draws only when something changed. It needs Avalonia's GPU renderer: under the software renderer
/// it stays empty and reports <see cref="Scene3DStatus.Unavailable"/>.
/// </para>
/// </summary>
public class Scene3DControl : OpenGlControlBase
{
    /// <summary>Defines the <see cref="Scene"/> property.</summary>
    public static readonly StyledProperty<Scene?> SceneProperty = AvaloniaProperty.Register<Scene3DControl, Scene?>(nameof(Scene));

    /// <summary>Defines the <see cref="View"/> property.</summary>
    public static readonly StyledProperty<SceneView?> ViewProperty = AvaloniaProperty.Register<Scene3DControl, SceneView?>(nameof(View));

    /// <summary>Defines the <see cref="VisibleRegion"/> property.</summary>
    public static readonly StyledProperty<Rect> VisibleRegionProperty = AvaloniaProperty.Register<Scene3DControl, Rect>(nameof(VisibleRegion), new Rect(0, 0, 1, 1));

    /// <summary>Defines the <see cref="DefaultBackground"/> property.</summary>
    public static readonly StyledProperty<Color> DefaultBackgroundProperty = AvaloniaProperty.Register<Scene3DControl, Color>(nameof(DefaultBackground), Colors.White);

    /// <summary>Defines the <see cref="Status"/> property.</summary>
    public static readonly DirectProperty<Scene3DControl, Scene3DStatus> StatusProperty =
        AvaloniaProperty.RegisterDirect<Scene3DControl, Scene3DStatus>(nameof(Status), o => o.Status);

    /// <summary>Defines the <see cref="StatusMessage"/> property.</summary>
    public static readonly DirectProperty<Scene3DControl, string?> StatusMessageProperty =
        AvaloniaProperty.RegisterDirect<Scene3DControl, string?>(nameof(StatusMessage), o => o.StatusMessage);

    /// <summary>Most the control's multisampled framebuffer may take before it drops samples.</summary>
    private const long FramebufferBudgetBytes = 96L << 20;

    private SceneRenderer? _renderer;
    private Scene3DStatus _status;
    private string? _statusMessage;
    private Scene3DStatistics _statistics;
    private long _framesRendered;

    // The scene the renderer holds, and whether the current one still has to be uploaded.
    private Scene? _uploadedScene;
    private bool _scenePending;

    // The camera: where the current view puts it, moved by the user since.
    private CameraPose _pose;
    private SceneViewProjection _projection = ViewCamera.DefaultProjection;
    private bool _hasPose;

    static Scene3DControl()
    {
        SceneProperty.Changed.AddClassHandler<Scene3DControl>((control, _) => control.OnSceneChanged());
        ViewProperty.Changed.AddClassHandler<Scene3DControl>((control, _) => control.OnViewChanged());
        VisibleRegionProperty.Changed.AddClassHandler<Scene3DControl>((control, _) => control.RequestNextFrameRendering());
        DefaultBackgroundProperty.Changed.AddClassHandler<Scene3DControl>((control, _) => control.RequestNextFrameRendering());
    }

    /// <summary>The scene to draw. The control does not dispose it.</summary>
    public Scene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>The view to show: camera, projection, background, lighting, node overrides. Null for a default view of the whole scene.</summary>
    public SceneView? View
    {
        get => GetValue(ViewProperty);
        set => SetValue(ViewProperty, value);
    }

    /// <summary>
    /// The part of the whole view this control's bounds show, in fractions of the view: (0, 0, 1, 1) is all
    /// of it. A host that zooms far into a view places the control over the visible part only and names
    /// that part here, so the control never draws more pixels than are on screen.
    /// </summary>
    public Rect VisibleRegion
    {
        get => GetValue(VisibleRegionProperty);
        set => SetValue(VisibleRegionProperty, value);
    }

    /// <summary>Background used when the view names none. May be transparent.</summary>
    public Color DefaultBackground
    {
        get => GetValue(DefaultBackgroundProperty);
        set => SetValue(DefaultBackgroundProperty, value);
    }

    /// <summary>
    /// Whether the scene is asked to release its vertex and index data once they are on the GPU. On by
    /// default: a scene's geometry is otherwise held twice.
    /// </summary>
    public bool ReleaseGeometryAfterUpload { get; set; } = true;

    /// <summary>What the control is doing.</summary>
    public Scene3DStatus Status
    {
        get => _status;
        private set => SetAndRaise(StatusProperty, ref _status, value);
    }

    /// <summary>Detail for <see cref="Scene3DStatus.Failed"/> and <see cref="Scene3DStatus.Unavailable"/>.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetAndRaise(StatusMessageProperty, ref _statusMessage, value);
    }

    /// <summary>Figures of the last frame and of the GPU memory held.</summary>
    public Scene3DStatistics Statistics => _statistics;

    /// <summary>Raised after each frame the control draws.</summary>
    public event EventHandler? FrameRendered;

    /// <summary>When set, the next frame is read back (RGBA rows, top first) and handed to this.</summary>
    internal Action<byte[], int, int>? CaptureNextFrame { get; set; }

    /// <summary>The camera in use; for tests and the viewer.</summary>
    internal CameraPose Pose => _pose;

    /// <summary>The OpenGL version and renderer strings, once initialised; for diagnostics.</summary>
    internal string? GlDescription { get; private set; }

    /// <summary>How the multisampled image reaches Avalonia's framebuffer on this machine; for diagnostics.</summary>
    internal string ResolvePath => _renderer?.Targets.Path.ToString() ?? "none";

    /// <summary>Puts the camera back where the current view has it.</summary>
    public void ResetView()
    {
        _hasPose = false;
        RequestNextFrameRendering();
    }

    private void OnSceneChanged()
    {
        _scenePending = Scene is not null;
        _hasPose = false;
        if (Scene is null)
        {
            Status = Scene3DStatus.Empty;
            StatusMessage = null;
        }
        else if (Status is not (Scene3DStatus.Unavailable or Scene3DStatus.Failed))
        {
            Status = Scene3DStatus.Uploading;
        }

        RequestNextFrameRendering();
    }

    private void OnViewChanged()
    {
        _hasPose = false;
        RequestNextFrameRendering();
    }

    /// <inheritdoc/>
    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            var api = new GlApi(gl, GlVersion);
            GlDescription = $"{api.GetString(GlApi.Version)}; {api.GetString(GlApi.Renderer)}; max samples {api.GetInteger(GlApi.MaxSamples)}; max texture {api.GetInteger(GlApi.MaxTextureSize)}";
            _renderer = new SceneRenderer(api);
            if (Status == Scene3DStatus.Unavailable)
            {
                Status = Scene is null ? Scene3DStatus.Empty : Scene3DStatus.Uploading;
                StatusMessage = null;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            _renderer = null;
            Status = Scene3DStatus.Failed;
            StatusMessage = e.Message;
        }
    }

    /// <inheritdoc/>
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer?.Dispose();
        ForgetGpuState();
    }

    /// <inheritdoc/>
    protected override void OnOpenGlLost()
    {
        // The context is gone and its objects with it; there is nothing to delete.
        ForgetGpuState();
    }

    private void ForgetGpuState()
    {
        _renderer = null;
        _uploadedScene = null;
        if (Scene is { } scene)
        {
            _scenePending = true;
            Status = scene.HasGeometry ? Scene3DStatus.Uploading : Scene3DStatus.NeedsScene;
        }
    }

    /// <inheritdoc/>
    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        SceneRenderer? renderer = _renderer;
        if (renderer is null)
        {
            return;
        }

        long started = Stopwatch.GetTimestamp();
        try
        {
            UploadPendingScene(renderer);

            double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int pixelWidth = Math.Max(1, (int)(Bounds.Width * scaling)), pixelHeight = Math.Max(1, (int)(Bounds.Height * scaling));

            Rect region = VisibleRegion;
            var viewRegion = new ViewRegion((float)region.X, (float)region.Y, (float)region.Right, (float)region.Bottom);
            Vector2 fullViewSize = viewRegion.HasArea
                ? new Vector2((float)(Bounds.Width / region.Width), (float)(Bounds.Height / region.Height))
                : default;

            SceneView? view = View;
            if (!_hasPose && _uploadedScene is { } scene)
            {
                _pose = ViewCamera.ResolvePose(scene, view, out _projection);
                _hasPose = true;
            }

            Color background = DefaultBackground;
            Vector4 backgroundColor = view?.Background is { } authored
                ? new Vector4(authored, 1)
                : new Vector4(background.R / 255f, background.G / 255f, background.B / 255f, background.A / 255f);

            var setup = new FrameSetup
            {
                Pose = _pose,
                Projection = _projection,
                FullViewSize = fullViewSize,
                Region = viewRegion,
                View = view,
                Background = backgroundColor,
                FramebufferBudget = FramebufferBudgetBytes
            };

            renderer.Render(fb, pixelWidth, pixelHeight, setup);

            if (CaptureNextFrame is { } capture)
            {
                CaptureNextFrame = null;
                capture(renderer.ReadBack(pixelWidth, pixelHeight), pixelWidth, pixelHeight);
            }

            _framesRendered++;
            _statistics = new Scene3DStatistics(
                renderer.Resources.VertexBytes,
                renderer.Resources.IndexBytes,
                0,
                renderer.Targets.Bytes,
                renderer.LastDrawCalls,
                renderer.LastTriangles,
                renderer.LastCulled,
                renderer.Targets.Samples,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                _framesRendered,
                renderer.Gl.LiveObjects,
                0,
                0);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Nothing may escape into the compositor's callback.
            Status = Scene3DStatus.Failed;
            StatusMessage = e.Message;
            return;
        }

        FrameRendered?.Invoke(this, EventArgs.Empty);
    }

    private void UploadPendingScene(SceneRenderer renderer)
    {
        if (!_scenePending)
        {
            return;
        }

        _scenePending = false;
        Scene? scene = Scene;
        if (scene is null)
        {
            renderer.ClearScene();
            _uploadedScene = null;
            return;
        }

        if (!scene.HasGeometry)
        {
            renderer.ClearScene();
            _uploadedScene = null;
            Status = Scene3DStatus.NeedsScene;
            return;
        }

        try
        {
            renderer.SetScene(scene);
        }
        catch (InvalidOperationException e)
        {
            _uploadedScene = null;
            Status = Scene3DStatus.Failed;
            StatusMessage = e.Message;
            return;
        }

        _uploadedScene = scene;
        if (ReleaseGeometryAfterUpload)
        {
            scene.ReleaseGeometry();
        }

        Status = Scene3DStatus.Ready;
        StatusMessage = null;
    }
}
