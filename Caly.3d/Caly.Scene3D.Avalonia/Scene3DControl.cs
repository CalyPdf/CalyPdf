using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Caly.Scene3D.Avalonia.Internal;
using Caly.Scene3D.Avalonia.Internal.Gl;
using Caly.Rendering.Scene3D;

namespace Caly.Scene3D.Avalonia;

/// <summary>
/// Draws a <see cref="Scene"/> with OpenGL, for a <see cref="SceneView"/> or a default view of the whole
/// scene, and lets the pointer turn, move and zoom the camera:
/// <list type="bullet">
/// <item>left-drag rotates about the view's orbit point;</item>
/// <item>right-drag up or down, or the wheel while the control has focus, zooms;</item>
/// <item>Ctrl + left-drag or middle-drag pans;</item>
/// <item>double-click or Home returns to the view;</item>
/// <item>a right-click without movement is left to the host, as a request for its context menu.</item>
/// </list>
/// <para>
/// A host with gestures of its own adjusts these with <see cref="ReservedModifiers"/>,
/// <see cref="PanModifiers"/> and <see cref="ResetKey"/>.
/// </para>
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
public partial class Scene3DControl : OpenGlControlBase, ICustomHitTest
{
    /// <summary>Defines the <see cref="Scene"/> property.</summary>
    public static readonly StyledProperty<Scene?> SceneProperty = AvaloniaProperty.Register<Scene3DControl, Scene?>(nameof(Scene));

    /// <summary>Defines the <see cref="View"/> property.</summary>
    public static readonly StyledProperty<SceneView?> ViewProperty = AvaloniaProperty.Register<Scene3DControl, SceneView?>(nameof(View));

    /// <summary>Defines the <see cref="VisibleRegion"/> property.</summary>
    public static readonly StyledProperty<Rect> VisibleRegionProperty = AvaloniaProperty.Register<Scene3DControl, Rect>(nameof(VisibleRegion), new Rect(0, 0, 1, 1));

    /// <summary>Defines the <see cref="DefaultBackground"/> property.</summary>
    public static readonly StyledProperty<Color> DefaultBackgroundProperty = AvaloniaProperty.Register<Scene3DControl, Color>(nameof(DefaultBackground), Colors.White);

    /// <summary>Defines the <see cref="Shading"/> property.</summary>
    public static readonly StyledProperty<Scene3DShading?> ShadingProperty = AvaloniaProperty.Register<Scene3DControl, Scene3DShading?>(nameof(Shading));

    /// <summary>Defines the <see cref="ReservedModifiers"/> property.</summary>
    public static readonly StyledProperty<KeyModifiers> ReservedModifiersProperty =
        AvaloniaProperty.Register<Scene3DControl, KeyModifiers>(nameof(ReservedModifiers));

    /// <summary>Defines the <see cref="PanModifiers"/> property.</summary>
    public static readonly StyledProperty<KeyModifiers> PanModifiersProperty =
        AvaloniaProperty.Register<Scene3DControl, KeyModifiers>(nameof(PanModifiers), KeyModifiers.Control);

    /// <summary>Defines the <see cref="ResetKey"/> property.</summary>
    public static readonly StyledProperty<Key?> ResetKeyProperty =
        AvaloniaProperty.Register<Scene3DControl, Key?>(nameof(ResetKey), Key.Home);

    /// <summary>Defines the <see cref="SelectedNode"/> property.</summary>
    public static readonly StyledProperty<int> SelectedNodeProperty =
        AvaloniaProperty.Register<Scene3DControl, int>(nameof(SelectedNode), -1);

    /// <summary>Defines the <see cref="HighlightColor"/> property.</summary>
    public static readonly StyledProperty<Color> HighlightColorProperty =
        AvaloniaProperty.Register<Scene3DControl, Color>(nameof(HighlightColor), Color.FromRgb(0x33, 0x99, 0xFF));

    /// <summary>Defines the <see cref="Status"/> property.</summary>
    public static readonly DirectProperty<Scene3DControl, Scene3DStatus> StatusProperty =
        AvaloniaProperty.RegisterDirect<Scene3DControl, Scene3DStatus>(nameof(Status), o => o.Status);

    /// <summary>Defines the <see cref="StatusMessage"/> property.</summary>
    public static readonly DirectProperty<Scene3DControl, string?> StatusMessageProperty =
        AvaloniaProperty.RegisterDirect<Scene3DControl, string?>(nameof(StatusMessage), o => o.StatusMessage);

    /// <summary>Defines the <see cref="CameraOrientation"/> property.</summary>
    public static readonly DirectProperty<Scene3DControl, Quaternion> CameraOrientationProperty =
        AvaloniaProperty.RegisterDirect<Scene3DControl, Quaternion>(nameof(CameraOrientation), o => o.CameraOrientation);

    /// <summary>Defines the <see cref="HasAnimation"/> property.</summary>
    public static readonly DirectProperty<Scene3DControl, bool> HasAnimationProperty =
        AvaloniaProperty.RegisterDirect<Scene3DControl, bool>(nameof(HasAnimation), o => o.HasAnimation);

    /// <summary>Defines the <see cref="IsAnimationPlaying"/> property.</summary>
    public static readonly DirectProperty<Scene3DControl, bool> IsAnimationPlayingProperty =
        AvaloniaProperty.RegisterDirect<Scene3DControl, bool>(nameof(IsAnimationPlaying), o => o.IsAnimationPlaying);

    /// <summary>Most the control's multisampled framebuffer may take before it drops samples.</summary>
    private const long FramebufferBudgetBytes = 96L << 20;

    /// <summary>How long OpenGL may take to initialise before the control calls it unavailable.</summary>
    private static readonly TimeSpan AvailabilityTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>A right-drag of this many pixels is one zoom step (as one notch of the wheel).</summary>
    private const float PixelsPerZoomStep = 20;

    /// <summary>
    /// A right button released having strayed less than this many pixels from where it went down is a
    /// click, which asks for the host's context menu, and not a zoom.
    /// </summary>
    internal const double ContextClickTolerance = 4;

    private enum Drag : byte
    {
        None,
        Rotate,
        Pan,
        Zoom
    }

    private SceneRenderer? _renderer;
    private readonly AnimationClock _animationClock = new();
    private long _framesRendered;
    private IDisposable? _availabilityTimer;

    // The scene the renderer holds, and whether the current one still has to be uploaded.
    private Scene? _uploadedScene;
    private bool _scenePending;

    // The camera: where the current view puts it, moved by the user since.
    private CameraPose _pose;

    // The camera node the pose came from, when an animation moves it (or a node above it): the pose is
    // kept as the view put it, in the frame of that node as it stands in the scene, and drawn carried
    // along with the node as the animation moves it.
    private int _cameraNode = -1;
    private bool _cameraAnimated;
    private Matrix4x4 _cameraNodeInverse = Matrix4x4.Identity;
    private SceneViewProjection _projection = ViewCamera.DefaultProjection;
    private bool _hasPose;

    private Drag _drag;
    private Point _lastPointer;
    private Point _pressPointer;
    private bool _movedSincePress;

    static Scene3DControl()
    {
        FocusableProperty.OverrideDefaultValue<Scene3DControl>(true);
        SceneProperty.Changed.AddClassHandler<Scene3DControl>((control, _) => control.OnSceneChanged());
        ViewProperty.Changed.AddClassHandler<Scene3DControl>((control, _) =>
        {
            control.ClearSelection();
            control.ResetView();
            control.StartEdges();
        });
        SelectedNodeProperty.Changed.AddClassHandler<Scene3DControl>((control, _) => control.RequestNextFrameRendering());
        HighlightColorProperty.Changed.AddClassHandler<Scene3DControl>((control, _) => control.RequestNextFrameRendering());
        PrepareEdgesProperty.Changed.AddClassHandler<Scene3DControl>((control, _) => control.StartEdges());
        RenderModeOverrideProperty.Changed.AddClassHandler<Scene3DControl>((control, _) =>
        {
            control.StartEdges();
            control.RequestNextFrameRendering();
        });
        VisibleRegionProperty.Changed.AddClassHandler<Scene3DControl>((control, _) => control.RequestNextFrameRendering());
        DefaultBackgroundProperty.Changed.AddClassHandler<Scene3DControl>((control, _) => control.RequestNextFrameRendering());
        ShadingProperty.Changed.AddClassHandler<Scene3DControl>((control, _) => control.RequestNextFrameRendering());
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
    /// The viewer's changes to how the scene is lit and shaded (brighter lights, stronger highlights…);
    /// null or <see cref="Scene3DShading.AsAuthored"/> for the scene as its file has it. Applied from the
    /// next frame; it is kept across a new scene or view.
    /// </summary>
    public Scene3DShading? Shading
    {
        get => GetValue(ShadingProperty);
        set => SetValue(ShadingProperty, value);
    }

    /// <summary>
    /// Whether the scene is asked to release its vertex and index data once they are on the GPU. On by
    /// default: a scene's geometry is otherwise held twice.
    /// </summary>
    public bool ReleaseGeometryAfterUpload { get; set; } = true;

    /// <summary>
    /// Modifier keys that belong to the host. A press or a wheel turn made with any of them down is left
    /// unhandled, for the host's own gestures (a page pan, a page zoom). None by default.
    /// </summary>
    public KeyModifiers ReservedModifiers
    {
        get => GetValue(ReservedModifiersProperty);
        set => SetValue(ReservedModifiersProperty, value);
    }

    /// <summary>
    /// The modifier keys that turn a left-drag into a pan; <see cref="KeyModifiers.None"/> for no such
    /// drag (the middle button always pans). Ctrl by default.
    /// </summary>
    public KeyModifiers PanModifiers
    {
        get => GetValue(PanModifiersProperty);
        set => SetValue(PanModifiersProperty, value);
    }

    /// <summary>The key that returns the camera to the view, or null for none. Home by default.</summary>
    public Key? ResetKey
    {
        get => GetValue(ResetKeyProperty);
        set => SetValue(ResetKeyProperty, value);
    }

    /// <summary>
    /// The node selected by a click (<see cref="SelectAt"/>): drawn tinted and with its bounding box in
    /// <see cref="HighlightColor"/>; -1 for none. Cleared by Escape and by a new scene or view.
    /// </summary>
    public int SelectedNode
    {
        get => GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    /// <summary>The colour the selected node is tinted and boxed in.</summary>
    public Color HighlightColor
    {
        get => GetValue(HighlightColorProperty);
        set => SetValue(HighlightColorProperty, value);
    }

    /// <summary>How many releases were taken as a click on the model, whether or not a scene was there to pick.</summary>
    internal int ClicksTaken { get; private set; }

    /// <summary>The point of the control a pick was asked for and the next frame will look at; null for none.</summary>
    internal Point? PendingPick { get; private set; }

    // Counts clearings of the selection: a pick answered after one (a new scene or view) is dropped.
    private int _selectionGeneration;

    /// <summary>
    /// Selects the node drawn at <paramref name="point"/> (device-independent pixels, in the control), or
    /// none where only the background is; the answer comes with the next frame. Does nothing while no
    /// scene is on the GPU.
    /// </summary>
    public void SelectAt(Point point)
    {
        if (!_hasPose || _uploadedScene is null)
        {
            return;
        }

        PendingPick = point;
        RequestNextFrameRendering();
    }

    private void ClearSelection()
    {
        PendingPick = null;
        _selectionGeneration++;
        SelectedNode = -1;
    }

    /// <summary>Runs the pick asked for since the last frame, after that frame was drawn with the same setup.</summary>
    private void TakePendingPick(SceneRenderer renderer, in FrameSetup setup, double scaling, int framebuffer)
    {
        if (PendingPick is not { } point)
        {
            return;
        }

        PendingPick = null;
        int node = renderer.Pick(setup, Picking.PickRegion(setup.Region, point, Bounds.Size, scaling));
        renderer.Gl.BindFramebuffer(GlApi.Framebuffer, framebuffer);

        int generation = _selectionGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation == _selectionGeneration)
            {
                SelectedNode = node;
            }
        });
    }

    /// <summary>Whether the scene has animations to play.</summary>
    public bool HasAnimation
    {
        get;
        private set => SetAndRaise(HasAnimationProperty, ref field, value);
    }

    /// <summary>Whether the scene's animations are running.</summary>
    public bool IsAnimationPlaying
    {
        get;
        private set => SetAndRaise(IsAnimationPlayingProperty, ref field, value);
    }

    /// <summary>Seconds the scene's animations have been playing, as of now; held while paused.</summary>
    public double AnimationTime => _animationClock.Elapsed(Stopwatch.GetTimestamp());

    /// <summary>Seconds the longest animation of the scene takes to run once; 0 without animations.</summary>
    public double AnimationDuration => Scene?.AnimationDuration ?? 0;

    /// <summary>Runs the scene's animations from where they stand. Does nothing for a scene without any.</summary>
    public void PlayAnimation()
    {
        if (HasAnimation)
        {
            _animationClock.Play(Stopwatch.GetTimestamp());
            IsAnimationPlaying = true;
            RequestNextFrameRendering();
        }
    }

    /// <summary>Holds the scene's animations where they are.</summary>
    public void PauseAnimation()
    {
        _animationClock.Pause(Stopwatch.GetTimestamp());
        IsAnimationPlaying = false;
        RequestNextFrameRendering();
    }

    /// <summary>Puts the animations' playhead at <paramref name="seconds"/>, playing or paused as it was.</summary>
    public void SeekAnimation(double seconds)
    {
        if (!HasAnimation)
        {
            return;
        }

        _animationClock.Seek(seconds, Stopwatch.GetTimestamp());
        ShowCameraOrientation();
        RequestNextFrameRendering();
    }

    /// <summary>Plays the scene's animations from their start.</summary>
    public void RestartAnimation()
    {
        if (!HasAnimation)
        {
            return;
        }

        _animationClock.Restart(Stopwatch.GetTimestamp());
        IsAnimationPlaying = true;
        ShowCameraOrientation();
        RequestNextFrameRendering();
    }

    /// <summary>What the control is doing.</summary>
    public Scene3DStatus Status
    {
        get;
        private set => SetAndRaise(StatusProperty, ref field, value);
    }

    /// <summary>Detail for <see cref="Scene3DStatus.Failed"/> and <see cref="Scene3DStatus.Unavailable"/>.</summary>
    public string? StatusMessage
    {
        get;
        private set => SetAndRaise(StatusMessageProperty, ref field, value);
    }

    /// <summary>The orientation of the camera in world space.</summary>
    public Quaternion CameraOrientation
    {
        get;
        private set => SetAndRaise(CameraOrientationProperty, ref field, value);
    } = Quaternion.Identity;

    /// <summary>Figures of the last frame and of the GPU memory held.</summary>
    public Scene3DStatistics Statistics { get; private set; }

    /// <summary>Raised after each frame the control draws. A handler that throws fails the control, as a failed frame does.</summary>
    public event EventHandler? FrameRendered;

    /// <summary>When set, the next frame is read back (RGBA rows, top first) and handed to this.</summary>
    internal Action<byte[], int, int>? CaptureNextFrame { get; set; }

    /// <summary>The camera in use; for tests and the viewer.</summary>
    internal CameraPose Pose => _pose;

    /// <summary>The pose the camera is drawn from now: <see cref="Pose"/>, carried along by the animation of its camera node, if it has one.</summary>
    internal CameraPose DisplayedPose => DisplayedPoseAt(AnimationTime);

    private CameraPose DisplayedPoseAt(double seconds)
    {
        if (!_cameraAnimated || !_hasPose || Scene is not { } scene)
        {
            return _pose;
        }

        return ViewCamera.Carry(_pose, _cameraNodeInverse * ViewCamera.AnimatedWorldMatrix(scene, _cameraNode, seconds));
    }

    /// <summary>
    /// Gives <see cref="CameraOrientation"/> the camera as it is drawn now, carried by its animated node if it
    /// has one: the pose alone is where the animation started, and the axes would jump back to it.
    /// </summary>
    private void ShowCameraOrientation() => CameraOrientation = DisplayedPose.Orientation;

    /// <summary>The projection in use; for tests and the viewer.</summary>
    internal SceneViewProjection Projection => _projection;

    /// <summary>
    /// For measurements: wait for the GPU at the end of each frame, so that the frame time in
    /// <see cref="Statistics"/> covers the GPU's work and not only issuing it.
    /// </summary>
    internal bool FinishEachFrame { get; set; }

    /// <summary>Managed bytes allocated while drawing the last frame; for measurements.</summary>
    internal long LastFrameAllocatedBytes { get; private set; }

    /// <summary>GPU objects still alive after the last release of the control's GPU resources; -1 before any.</summary>
    internal int LiveGpuObjectsAfterRelease { get; private set; } = -1;

    /// <summary>The OpenGL version and renderer strings, once initialised; for diagnostics.</summary>
    internal string? GlDescription { get; private set; }

    /// <summary>How the multisampled image reaches Avalonia's framebuffer on this machine; for diagnostics.</summary>
    internal string ResolvePath => _renderer?.Targets.Path.ToString() ?? "none";

    /// <summary>Size of the whole view in the control's units: its bounds, or more when it shows only a region.</summary>
    private Vector2 FullViewSize
    {
        get
        {
            Rect region = VisibleRegion;
            return region.Width > 0 && region.Height > 0
                ? new Vector2((float)(Bounds.Width / region.Width), (float)(Bounds.Height / region.Height))
                : default;
        }
    }

    /// <summary>Puts the camera back where the current view has it.</summary>
    public void ResetView()
    {
        if (Scene is { } scene)
        {
            _pose = ViewCamera.ResolvePose(scene, View, out _projection, out _cameraNode);
            _cameraNodeInverse = Matrix4x4.Identity;
            _cameraAnimated = _cameraNode >= 0 && ViewCamera.IsAnimatedNode(scene, _cameraNode)
                && Matrix4x4.Invert(scene.GetWorldMatrix(_cameraNode), out _cameraNodeInverse);
            _hasPose = true;
            ShowCameraOrientation();
        }
        else
        {
            _hasPose = false;
            _cameraAnimated = false;
            CameraOrientation = Quaternion.Identity;
        }

        RequestNextFrameRendering();
    }

    /// <summary>
    /// The camera and the animations' playhead as they stand, for <see cref="RestoreCameraState"/>; null
    /// while there is no camera (no scene).
    /// </summary>
    public Scene3DCameraState? CaptureCameraState()
    {
        return _hasPose ? new Scene3DCameraState(View, _pose, _projection, AnimationTime, IsAnimationPlaying) : null;
    }

    /// <summary>
    /// Puts back a camera and a playhead captured from a control showing the same scene. Call it once the
    /// control has its <see cref="Scene"/> and <see cref="View"/>: setting either afterwards resets the
    /// camera, as it always does. Ignored when the state was captured for another view.
    /// </summary>
    /// <returns>Whether the state was applied.</returns>
    public bool RestoreCameraState(Scene3DCameraState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!_hasPose || !ReferenceEquals(state.View, View) || !state.Pose.IsFinite || !ViewCamera.IsValid(state.Projection))
        {
            return false;
        }

        // The camera node, and whether an animation carries it, are the view's: ResetView set them.
        _pose = state.Pose;
        _projection = state.Projection;

        if (HasAnimation)
        {
            if (state.IsAnimationPlaying)
            {
                PlayAnimation();
            }
            else
            {
                PauseAnimation();
            }

            SeekAnimation(state.AnimationTime);
        }

        ShowCameraOrientation();
        RequestNextFrameRendering();
        return true;
    }

    /// <summary>Turns the camera about the orbit point as a pointer drag of this many pixels would.</summary>
    internal void RotateBy(float dx, float dy)
    {
        if (!_hasPose)
        {
            return;
        }

        _pose = OrbitController.Rotate(_pose, dx, dy, (float)Bounds.Height);
        ShowCameraOrientation();
        RequestNextFrameRendering();
    }

    /// <summary>Slides the camera as a pointer drag of this many pixels would.</summary>
    internal void PanBy(float dx, float dy)
    {
        if (!_hasPose)
        {
            return;
        }

        _pose = OrbitController.Pan(_pose, _projection, dx, dy, FullViewSize);
        ShowCameraOrientation();
        RequestNextFrameRendering();
    }

    /// <summary>Zooms by a number of steps; positive is closer.</summary>
    internal void ZoomBy(float steps)
    {
        if (!_hasPose)
        {
            return;
        }

        OrbitController.Zoom(ref _pose, ref _projection, steps);
        ShowCameraOrientation();
        RequestNextFrameRendering();
    }

    private void OnSceneChanged()
    {
        ClearSelection();
        CancelEdges();
        Scene? scene = Scene;

        // The next frame puts the scene on the GPU, or without one takes the old one off.
        _scenePending = true;
        if (scene is null)
        {
            Status = Scene3DStatus.Empty;
            StatusMessage = null;
        }
        else if (Status is not (Scene3DStatus.Unavailable or Scene3DStatus.Failed))
        {
            // A scene that has given up its geometry cannot be uploaded, and no GPU is needed to see
            // that: say so now, so that a host can decode again while OpenGL is still starting.
            Status = scene.HasGeometry ? Scene3DStatus.Uploading : Scene3DStatus.NeedsScene;
        }

        // A scene begins its animations again, playing if its source asks.
        _animationClock.Reset();
        HasAnimation = scene is { IsAnimated: true };
        IsAnimationPlaying = false;
        if (HasAnimation && scene!.AutoPlayAnimations)
        {
            PlayAnimation();
        }

        ResetView();
        RestoreKeptCamera();
        StartEdges();
    }

    // ---- pointer and keyboard ----

    /// <summary>
    /// The whole of the control takes pointer input. Avalonia hit-tests what a control draws, and this
    /// one draws nothing itself: its picture is the OpenGL surface.
    /// </summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint point = e.GetCurrentPoint(this);

        if ((e.KeyModifiers & ReservedModifiers) != 0)
        {
            // The host's gesture: the press travels on, unhandled. Avalonia has already focused this control
            // (its focus manager focuses on any left press, before this runs); the keyboard goes back to the
            // host, or the wheel would zoom the model after a page pan.
            _drag = Drag.None;
            if (IsFocused)
            {
                GiveFocusToHost();
            }

            return;
        }

        // A left or middle press works the model and takes the wheel with it; a right press may yet be a
        // click for the host's context menu, and takes the focus only once it turns into a zoom.
        if (point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed)
        {
            Focus();
        }

        if (point.Properties.IsLeftButtonPressed && e.ClickCount == 2)
        {
            ResetView();
            e.Handled = true;
            return;
        }

        bool pans = PanModifiers != KeyModifiers.None && (e.KeyModifiers & PanModifiers) == PanModifiers;
        _drag = point.Properties.IsLeftButtonPressed ? (pans ? Drag.Pan : Drag.Rotate)
            : point.Properties.IsMiddleButtonPressed ? Drag.Pan
            : point.Properties.IsRightButtonPressed ? Drag.Zoom
            : Drag.None;

        if (_drag != Drag.None)
        {
            _lastPointer = _pressPointer = point.Position;
            _movedSincePress = false;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    /// <summary>Hands the keyboard focus to the nearest focusable element around the control (the host's page), if there is one.</summary>
    private void GiveFocusToHost()
    {
        for (Visual? ancestor = this.GetVisualParent(); ancestor is not null; ancestor = ancestor.GetVisualParent())
        {
            if (ancestor is InputElement { Focusable: true, IsEffectivelyEnabled: true, IsEffectivelyVisible: true } element && element.Focus())
            {
                return;
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag == Drag.None)
        {
            return;
        }

        Point position = e.GetPosition(this);
        float dx = (float)(position.X - _lastPointer.X), dy = (float)(position.Y - _lastPointer.Y);
        _lastPointer = position;
        _movedSincePress |= Math.Abs(position.X - _pressPointer.X) >= ContextClickTolerance || Math.Abs(position.Y - _pressPointer.Y) >= ContextClickTolerance;

        switch (_drag)
        {
            case Drag.Rotate:
                RotateBy(dx, dy);
                break;
            case Drag.Pan:
                PanBy(dx, dy);
                break;
            case Drag.Zoom:
                if (_movedSincePress && !IsFocused)
                {
                    Focus();
                }

                ZoomBy(-dy / PixelsPerZoomStep);
                break;
        }

        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        Drag drag = _drag;
        if (drag != Drag.None)
        {
            _drag = Drag.None;
            e.Pointer.Capture(null);

            // A right button that never left its place was a click: left unhandled, so that the base
            // class asks for the context menu and a host's menu opens. Everything else was a drag, and
            // is marked handled before the base class sees it, which would otherwise ask for a menu
            // after every right-drag.
            e.Handled = drag != Drag.Zoom || _movedSincePress || e.InitialPressMouseButton != MouseButton.Right;

            // A left button that never left its place was a click on the model: it selects what is there.
            if (drag == Drag.Rotate && !_movedSincePress && e.InitialPressMouseButton == MouseButton.Left)
            {
                ClicksTaken++;
                SelectAt(e.GetPosition(this));
            }
        }

        base.OnPointerReleased(e);
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _drag = Drag.None;
    }

    /// <inheritdoc/>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        // Without focus the wheel is left to whatever scrolls around the control.
        if (!IsFocused || !_hasPose || e.Delta.Y == 0 || (e.KeyModifiers & ReservedModifiers) != 0)
        {
            return;
        }

        ZoomBy((float)e.Delta.Y);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Escape clears a selection first; with nothing selected it is left to the host.
        if (e.Key == Key.Escape && !e.Handled && SelectedNode >= 0)
        {
            SelectedNode = -1;
            e.Handled = true;
            return;
        }

        if (ResetKey is not { } resetKey || e.Key != resetKey || e.Handled)
        {
            return;
        }

        ResetView();
        e.Handled = true;
    }

    // ---- lifetime ----

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _availabilityTimer?.Dispose();
        _availabilityTimer = DispatcherTimer.RunOnce(CheckAvailability, AvailabilityTimeout);
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _availabilityTimer?.Dispose();
        _availabilityTimer = null;
        _drag = Drag.None;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Under the software renderer (and wherever the compositor cannot share GPU surfaces) OpenGL never
    /// initialises, and nothing says so: the control would just stay blank. Called a moment after
    /// attaching, this turns that silence into a status.
    /// </summary>
    internal void CheckAvailability()
    {
        if (_renderer is null && Status != Scene3DStatus.Failed)
        {
            Status = Scene3DStatus.Unavailable;
            StatusMessage = "3D needs the GPU renderer.";
        }
    }

    /// <inheritdoc/>
    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            var api = new GlApi(gl, GlVersion);
            GlDescription = $"{api.GetString(GlApi.Version)}; {api.GetString(GlApi.Renderer)}; max samples {api.GetInteger(GlApi.MaxSamples)}; max texture {api.GetInteger(GlApi.MaxTextureSize)}";
            _renderer = new SceneRenderer(api, () => Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Background));
            if (Status == Scene3DStatus.Unavailable)
            {
                Status = Scene is null ? Scene3DStatus.Empty : Scene3DStatus.Uploading;
                StatusMessage = null;
            }
        }
        catch (Exception e)
        {
            // Nothing may escape into the compositor's callback (as in OnOpenGlRender): a driver or a
            // shader compiler can fail in more ways than the two this used to expect.
            _renderer = null;
            Status = Scene3DStatus.Failed;
            StatusMessage = e.Message;
        }
    }

    /// <inheritdoc/>
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_renderer is { } renderer)
        {
            renderer.Dispose();
            LiveGpuObjectsAfterRelease = renderer.Gl.LiveObjects;
        }

        ForgetGpuState();
    }

    /// <inheritdoc/>
    protected override void OnOpenGlLost()
    {
        // The context is gone and its objects with it; there is nothing to delete.
        _renderer?.Abandon();
        ForgetGpuState();
    }

    private void ForgetGpuState()
    {
        _renderer = null;
        _uploadedScene = null;
        ForgetEdges();
        if (Scene is { } scene && Status != Scene3DStatus.Failed)
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
            UploadPendingEdges(renderer);

            double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int pixelWidth = Math.Max(1, (int)(Bounds.Width * scaling)), pixelHeight = Math.Max(1, (int)(Bounds.Height * scaling));

            Rect region = VisibleRegion;
            SceneView? view = View;
            double? animationTime = HasAnimation ? AnimationTime : null;
            CameraPose pose = DisplayedPoseAt(animationTime ?? 0);
            if (_cameraAnimated)
            {
                CameraOrientation = pose.Orientation;
            }

            Color background = DefaultBackground;
            var setup = new FrameSetup
            {
                Pose = pose,
                Projection = _projection,
                FullViewSize = FullViewSize,
                Region = new ViewRegion((float)region.X, (float)region.Y, (float)region.Right, (float)region.Bottom),
                View = view,
                Shading = Shading,
                RenderMode = RenderMode,
                Background = view?.Background is { } authored
                    ? new Vector4(authored, 1)
                    : new Vector4(background.R / 255f, background.G / 255f, background.B / 255f, background.A / 255f),
                FramebufferBudget = FramebufferBudgetBytes,
                AnimationTime = animationTime,
                SelectedNode = SelectedNode >= 0 ? SelectedNode : null,
                Highlight = new Vector3(HighlightColor.R, HighlightColor.G, HighlightColor.B) / 255f
            };

            // Without a camera (no scene) or a scene on the GPU, only the background is drawn.
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            renderer.Render(fb, pixelWidth, pixelHeight, setup, drawScene: _hasPose && _uploadedScene is not null);
            LastFrameAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            if (_hasPose && _uploadedScene is not null)
            {
                TakePendingPick(renderer, setup, scaling, fb);
            }
            if (FinishEachFrame)
            {
                renderer.Gl.Finish();
            }

            if (CaptureNextFrame is { } capture)
            {
                CaptureNextFrame = null;
                capture(renderer.ReadBack(pixelWidth, pixelHeight), pixelWidth, pixelHeight);
            }

            _framesRendered++;
            Statistics = new Scene3DStatistics(
                renderer.Resources.VertexBytes + renderer.EdgeBytes,
                renderer.Resources.IndexBytes,
                renderer.TextureBytes,
                renderer.Targets.Bytes,
                renderer.LastDrawCalls,
                renderer.LastTriangles,
                renderer.LastCulled,
                renderer.Targets.Samples,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                _framesRendered,
                renderer.Gl.LiveObjects,
                renderer.TexturesLoaded,
                renderer.TexturesFailed,
                renderer.TexturesPending);

            // A playing animation is a frame after frame.
            if (renderer.MoreUploadsReady || IsAnimationPlaying)
            {
                RequestNextFrameRendering();
            }

            FrameRendered?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            // Nothing may escape into the compositor's callback, where it would end the application: not a
            // driver's failure, not running out of memory for a large model, not a host's FrameRendered handler.
            Status = Scene3DStatus.Failed;
            StatusMessage = e.Message;
        }
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

        // Back from losing the GPU with a scene that still holds its geometry: its edges are found again.
        StartEdges();

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
        if (ReleaseGeometryAfterUpload && MayReleaseGeometry(scene))
        {
            scene.ReleaseGeometry();
        }

        Status = Scene3DStatus.Ready;
        StatusMessage = null;
    }
}
