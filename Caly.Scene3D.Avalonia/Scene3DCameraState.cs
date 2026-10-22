using Caly.Rendering.Scene3D;
using Caly.Scene3D.Avalonia.Internal;

namespace Caly.Scene3D.Avalonia;

/// <summary>
/// Where the user left a <see cref="Scene3DControl"/>: its camera, as moved since the view put it, and
/// its animations' playhead. A host keeps it while the control is away (a control that leaves the
/// visual tree is usually dropped) and hands it to the control built in its place, through
/// <see cref="Scene3DControl.RestoreCameraState"/>. Opaque to the host.
/// </summary>
public sealed class Scene3DCameraState
{
    internal Scene3DCameraState(SceneView? view, CameraPose pose, SceneViewProjection projection,
        double animationTime, bool isAnimationPlaying)
    {
        View = view;
        Pose = pose;
        Projection = projection;
        AnimationTime = animationTime;
        IsAnimationPlaying = isAnimationPlaying;
    }

    /// <summary>The view the camera was moved from; the state is given back only to the same view.</summary>
    internal SceneView? View { get; }

    internal CameraPose Pose { get; }

    internal SceneViewProjection Projection { get; }

    internal double AnimationTime { get; }

    internal bool IsAnimationPlaying { get; }
}
