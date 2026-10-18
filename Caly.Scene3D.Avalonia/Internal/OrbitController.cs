using System.Numerics;
using Caly.Rendering.Scene3D;

namespace Caly.Scene3D.Avalonia.Internal;

/// <summary>
/// What the pointer does to the camera: rotate about the orbit point, pan in the view plane, zoom towards
/// the orbit point. Pure functions from a pose to a pose; input that is not a number changes nothing.
/// </summary>
internal static class OrbitController
{
    /// <summary>The distance to the orbit point (or the orthographic scale) changes by this factor per zoom step.</summary>
    private const float ZoomFactorPerStep = 0.9f;

    private const float MinimumDistance = 1e-12f;
    private const float RelativeMinimumDistance = 1e-5f;
    private const float MaximumDistance = 1e18f;

    /// <summary>
    /// Turns the camera about the orbit point, relative to the screen: a horizontal drag turns about the
    /// camera's up axis, a vertical drag about its right axis, and the model follows the pointer. Dragging
    /// the height of the view is half a turn.
    /// </summary>
    public static CameraPose Rotate(in CameraPose pose, float dx, float dy, float viewHeight)
    {
        if (!float.IsFinite(dx) || !float.IsFinite(dy) || !(viewHeight > 0) || !float.IsFinite(viewHeight))
        {
            return pose;
        }

        float radiansPerPixel = MathF.PI / viewHeight;
        Quaternion turn = Quaternion.CreateFromAxisAngle(pose.Up, -dx * radiansPerPixel) * Quaternion.CreateFromAxisAngle(pose.Right, -dy * radiansPerPixel);

        Vector3 orbitPoint = pose.OrbitPoint;
        CameraPose turned = pose;
        turned.Orientation = Quaternion.Normalize(turn * pose.Orientation);

        // Placed from the orbit point along the new view direction, so the distance cannot drift.
        turned.Position = orbitPoint - turned.Forward * pose.OrbitDistance;
        return turned.IsFinite ? turned : pose;
    }

    /// <summary>
    /// Slides the camera in its own plane so that what is at the orbit distance moves on screen by the
    /// dragged amount.
    /// </summary>
    public static CameraPose Pan(in CameraPose pose, in SceneViewProjection projection, float dx, float dy, Vector2 fullViewSize)
    {
        if (!float.IsFinite(dx) || !float.IsFinite(dy) || !(fullViewSize.X > 0) || !(fullViewSize.Y > 0) || !ViewCamera.IsValid(projection))
        {
            return pose;
        }

        float bound = projection.Binding switch
        {
            SceneViewBinding.Width => fullViewSize.X,
            SceneViewBinding.Height => fullViewSize.Y,
            SceneViewBinding.Max => MathF.Max(fullViewSize.X, fullViewSize.Y),
            _ => MathF.Min(fullViewSize.X, fullViewSize.Y)
        };

        // Scene units one pixel covers at the orbit distance.
        float extent = projection.Kind == SceneProjection.Perspective
            ? 2 * pose.OrbitDistance * MathF.Tan(projection.FieldOfView * MathF.PI / 360)
            : 1 / projection.OrthographicScale;
        float unitsPerPixel = extent / bound;

        // The scene goes right when the camera goes left; the screen's Y runs down.
        CameraPose panned = pose;
        panned.Position += (-dx * pose.Right + dy * pose.Up) * unitsPerPixel;
        return panned.IsFinite ? panned : pose;
    }

    /// <summary>
    /// Zooms by <paramref name="steps"/> (positive: closer). A perspective camera moves along its view
    /// direction and never reaches the orbit point; an orthographic view changes its scale.
    /// </summary>
    public static void Zoom(ref CameraPose pose, ref SceneViewProjection projection, float steps)
    {
        if (!float.IsFinite(steps))
        {
            return;
        }

        float factor = MathF.Pow(ZoomFactorPerStep, steps);
        if (!(factor > 0) || !float.IsFinite(factor))
        {
            return;
        }

        if (projection.Kind == SceneProjection.Orthographic)
        {
            float scale = projection.OrthographicScale / factor;
            if (scale > 0 && float.IsFinite(scale))
            {
                projection = projection with { OrthographicScale = scale };
            }

            return;
        }

        // Not so close that the camera and the orbit point become the same numbers: the floor follows the
        // size of the coordinates involved.
        Vector3 orbitPoint = pose.OrbitPoint;
        float floor = MathF.Max(MinimumDistance, orbitPoint.Length() * RelativeMinimumDistance);
        float distance = pose.OrbitDistance * factor;
        if ((distance < floor && factor < 1) || distance > MaximumDistance || !float.IsFinite(distance))
        {
            return;
        }

        CameraPose zoomed = pose;
        zoomed.OrbitDistance = distance;
        zoomed.Position = orbitPoint - pose.Forward * distance;
        if (zoomed.IsFinite)
        {
            pose = zoomed;
        }
    }
}
