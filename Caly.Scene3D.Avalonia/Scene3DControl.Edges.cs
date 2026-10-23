using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;
using Caly.Scene3D.Avalonia.Internal;
using Caly.Scene3D.Avalonia.Internal.Gl;
using Caly.Rendering.Scene3D;

namespace Caly.Scene3D.Avalonia;

// The render modes, and the edges some of them draw: silhouette edges (SolidOutline, the illustrations)
// and every edge (the wireframes). Edges are found from the scene's geometry on a worker thread (a large
// model takes a fifth of a second), so the scene keeps its geometry until they are found, even if it is
// uploaded first; then they go to the GPU.
public partial class Scene3DControl
{
    /// <summary>Defines the <see cref="PrepareEdges"/> property.</summary>
    public static readonly StyledProperty<bool> PrepareEdgesProperty =
        AvaloniaProperty.Register<Scene3DControl, bool>(nameof(PrepareEdges));

    /// <summary>Defines the <see cref="RenderModeOverride"/> property.</summary>
    public static readonly StyledProperty<SceneRenderMode?> RenderModeOverrideProperty =
        AvaloniaProperty.Register<Scene3DControl, SceneRenderMode?>(nameof(RenderModeOverride));

    private Scene? _edgeScene;                  // the scene the edges below are for (or being found for)
    private CancellationTokenSource? _edgeCancel;
    private bool _edgesBuilding;
    private EdgeSet? _edgesToUpload;
    private bool _releaseGeometryAfterEdges;

    // The camera when the control asked for the scene again to find its edges: given back to the scene that comes.
    private KeptCamera? _cameraForNextScene;

    private sealed record EdgeSet(OutlineEdges Outlines, WireEdges Wires);

    private sealed record KeptCamera(SceneView? View, CameraPose Pose, SceneViewProjection Projection, double AnimationTime, bool Playing);

    /// <summary>
    /// Whether to find the model's edges as soon as a scene is set, although the current render mode does
    /// not draw them: set it when a render mode the host may show does (a PDF's views, a choice of render
    /// modes). A render mode that draws edges has them found anyway; if the scene has given its geometry away
    /// by then, the control reports <see cref="Scene3DStatus.NeedsScene"/>, and gives the camera and the
    /// animations' playhead back to the scene set in answer.
    /// </summary>
    public bool PrepareEdges
    {
        get => GetValue(PrepareEdgesProperty);
        set => SetValue(PrepareEdgesProperty, value);
    }

    /// <summary>The render mode to draw in instead of the view's; null for the view's own (Solid without a view).</summary>
    public SceneRenderMode? RenderModeOverride
    {
        get => GetValue(RenderModeOverrideProperty);
        set => SetValue(RenderModeOverrideProperty, value);
    }

    /// <summary>The render mode drawn: <see cref="RenderModeOverride"/>, or the view's.</summary>
    public SceneRenderMode RenderMode => RenderModeOverride ?? View?.RenderMode ?? SceneRenderMode.Solid;

    /// <summary>
    /// Whether edges are still being found or have yet to reach the GPU: until then a render mode that draws
    /// them is drawn without (a frame is requested when they arrive).
    /// </summary>
    public bool IsFindingEdges => _edgesBuilding || _edgesToUpload is not null;

    /// <summary>Whether a render mode draws edges found from the geometry: silhouettes or every edge.</summary>
    public static bool DrawsEdges(SceneRenderMode mode) => RenderModePlan.For(mode).NeedsEdges;

    /// <summary>
    /// Starts finding the scene's edges if they are wanted and not found or being found yet. With the
    /// scene's geometry gone, a render mode that draws them asks for the scene again.
    /// </summary>
    private void StartEdges()
    {
        Scene? scene = Scene;
        if (scene is null || ReferenceEquals(_edgeScene, scene) || !(PrepareEdges || DrawsEdges(RenderMode)))
        {
            return;
        }

        if (!scene.HasGeometry)
        {
            if (DrawsEdges(RenderMode) && ReferenceEquals(_uploadedScene, scene) && Status == Scene3DStatus.Ready)
            {
                _cameraForNextScene = _hasPose
                    ? new KeptCamera(View, _pose, _projection, HasAnimation ? AnimationTime : 0, IsAnimationPlaying)
                    : null;
                Status = Scene3DStatus.NeedsScene;
            }

            return;
        }

        CancelEdges();
        var cancel = new CancellationTokenSource();
        _edgeCancel = cancel;
        _edgeScene = scene;
        _edgesBuilding = true;
        Task.Run(() => new EdgeSet(OutlineEdges.Build(scene, cancel.Token), WireEdges.Build(scene, cancel.Token)), cancel.Token)
            .ContinueWith(task => Dispatcher.UIThread.Post(() => OnEdgesFound(scene, cancel, task)), TaskScheduler.Default);
    }

    private void OnEdgesFound(Scene scene, CancellationTokenSource cancel, Task<EdgeSet> task)
    {
        if (!ReferenceEquals(cancel, _edgeCancel))
        {
            cancel.Dispose();
            return;
        }

        _edgesBuilding = false;
        _edgeCancel = null;
        cancel.Dispose();

        // A model whose edges cannot be found is drawn without them.
        _edgesToUpload = task.IsCompletedSuccessfully ? task.Result : null;
        if (_releaseGeometryAfterEdges)
        {
            _releaseGeometryAfterEdges = false;
            if (ReferenceEquals(Scene, scene))
            {
                scene.ReleaseGeometry();
            }
        }

        RequestNextFrameRendering();
    }

    /// <summary>Stops finding edges and forgets those found.</summary>
    private void CancelEdges()
    {
        _edgeCancel?.Cancel();
        _edgeCancel = null;
        _edgesBuilding = false;
        _edgeScene = null;
        _edgesToUpload = null;
        _releaseGeometryAfterEdges = false;
    }

    /// <summary>
    /// Whether the scene just uploaded may give its geometry away now; if its edges are still being found,
    /// it does so once they are.
    /// </summary>
    private bool MayReleaseGeometry(Scene scene)
    {
        if (_edgesBuilding && ReferenceEquals(_edgeScene, scene))
        {
            _releaseGeometryAfterEdges = true;
            return false;
        }

        return true;
    }

    /// <summary>Puts edges found since the last frame on the GPU.</summary>
    private void UploadPendingEdges(SceneRenderer renderer)
    {
        if (_edgesToUpload is not { } edges || _uploadedScene is null || !ReferenceEquals(_edgeScene, _uploadedScene))
        {
            return;
        }

        _edgesToUpload = null;
        try
        {
            renderer.SetEdges(edges.Outlines, edges.Wires);
        }
        catch (InvalidOperationException)
        {
            // Without the memory for its edges, the model is drawn without them.
        }
    }

    /// <summary>The GPU objects are gone: edges found again when wanted.</summary>
    private void ForgetEdges()
    {
        if (!_edgesBuilding && _edgesToUpload is null)
        {
            _edgeScene = null;
        }
    }

    /// <summary>
    /// After a new scene has reset the camera: the camera the control kept when it asked for the scene again
    /// to find its edges, if the view is still the one it was kept for.
    /// </summary>
    private void RestoreKeptCamera()
    {
        if (_cameraForNextScene is not { } kept)
        {
            return;
        }

        _cameraForNextScene = null;
        if (!_hasPose || !ReferenceEquals(kept.View, View))
        {
            return;
        }

        _pose = kept.Pose;
        _projection = kept.Projection;
        if (HasAnimation)
        {
            if (kept.Playing)
            {
                PlayAnimation();
            }
            else
            {
                PauseAnimation();
            }

            SeekAnimation(kept.AnimationTime);
        }

        ShowCameraOrientation();
        RequestNextFrameRendering();
    }
}
