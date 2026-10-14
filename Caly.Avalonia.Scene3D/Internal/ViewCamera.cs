using System.Numerics;
using Caly.Rendering.Scene3D;

namespace Caly.Avalonia.Scene3D.Internal;

/// <summary>
/// Where the camera is and which way it faces. Camera space is the scene's: +X right, +Y up, looking
/// along −Z.
/// </summary>
internal struct CameraPose
{
    public Vector3 Position;
    public Quaternion Orientation;

    /// <summary>Distance along the view direction to the point that orbiting turns about. Always positive.</summary>
    public float OrbitDistance;

    public readonly Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Orientation);

    public readonly Vector3 Up => Vector3.Transform(Vector3.UnitY, Orientation);

    public readonly Vector3 Right => Vector3.Transform(Vector3.UnitX, Orientation);

    public readonly Vector3 OrbitPoint => Position + Forward * OrbitDistance;

    public readonly bool IsFinite =>
        float.IsFinite(Position.X) && float.IsFinite(Position.Y) && float.IsFinite(Position.Z)
        && float.IsFinite(Orientation.X) && float.IsFinite(Orientation.Y) && float.IsFinite(Orientation.Z) && float.IsFinite(Orientation.W)
        && float.IsFinite(OrbitDistance);
}

/// <summary>
/// The part of the whole view a surface shows, as fractions of the view: (0, 0) is its top-left corner,
/// (1, 1) its bottom-right.
/// </summary>
internal readonly record struct ViewRegion(float Left, float Top, float Right, float Bottom)
{
    public static ViewRegion Full => new(0, 0, 1, 1);

    public bool HasArea => Right - Left > 0 && Bottom - Top > 0 && float.IsFinite(Left + Top + Right + Bottom);
}

/// <summary>
/// The camera mathematics: which camera a view means, and the matrices that put the scene on screen.
/// Pure functions; nothing here touches the GPU.
/// </summary>
internal static class ViewCamera
{
    /// <summary>What a scene is shown with when neither a view nor a camera node says otherwise.</summary>
    public static readonly SceneViewProjection DefaultProjection = new(SceneProjection.Perspective, 30, SceneViewBinding.Min, 1, null, null);

    /// <summary>Direction from the scene towards the default camera: front right, a little above; +Z is up.</summary>
    private static readonly Vector3 DefaultEyeDirection = Vector3.Normalize(new Vector3(1f, -1.25f, 0.8f));

    /// <summary>Most the far plane may be further than the near plane, for the depth buffer's sake.</summary>
    private const float MaxDepthRatio = 10_000f;

    public static bool IsValid(in SceneViewProjection projection)
    {
        return projection.Kind == SceneProjection.Orthographic
            ? projection.OrthographicScale > 0 && float.IsFinite(projection.OrthographicScale)
            : projection.FieldOfView > 0 && projection.FieldOfView < 180;
    }

    /// <summary>
    /// The camera a view means for a scene, in this order: the camera node the view names; the view's own
    /// pose; the scene's first camera node; a default view of the whole scene. A source that is missing
    /// or unusable (not finite, collapsed) is passed over.
    /// </summary>
    public static CameraPose ResolvePose(Scene scene, SceneView? view, out SceneViewProjection projection) => ResolvePose(scene, view, out projection, out _);

    /// <summary>As above, and says which camera node the pose is that of (-1 for a pose from a matrix or the default).</summary>
    public static CameraPose ResolvePose(Scene scene, SceneView? view, out SceneViewProjection projection, out int cameraNode)
    {
        SceneBounds bounds = scene.ComputeWorldBounds();
        CameraPose pose = default;
        cameraNode = -1;
        bool found = false;

        if (view is { CameraNodePath.Count: > 0 })
        {
            var path = new string[view.CameraNodePath.Count];
            for (int i = 0; i < path.Length; i++)
            {
                path[i] = view.CameraNodePath[i];
            }

            int node = scene.FindNode(path, SceneNodeKind.Camera);
            if (node >= 0 && TryPoseFromMatrix(scene.GetWorldMatrix(node), out pose))
            {
                cameraNode = node;
                found = true;
            }
        }

        if (!found && view?.CameraToWorld is { } cameraToWorld)
        {
            found = TryPoseFromMatrix(cameraToWorld, out pose);
        }

        if (!found)
        {
            ReadOnlySpan<SceneNode> nodes = scene.Nodes;
            for (int i = 0; i < nodes.Length && !found; i++)
            {
                if (nodes[i].Kind == SceneNodeKind.Camera && nodes[i].Camera >= 0 && TryPoseFromMatrix(scene.GetWorldMatrix(i), out pose))
                {
                    cameraNode = i;
                    found = true;
                }
            }
        }

        // The projection: the view's, else the camera node's, else the default.
        projection = DefaultProjection;
        if (view?.Projection is { } wanted && IsValid(wanted))
        {
            projection = wanted;
        }
        else if (cameraNode >= 0)
        {
            SceneCamera camera = scene.Cameras[scene.Nodes[cameraNode].Camera];
            SceneViewProjection own = camera.Projection == SceneProjection.Orthographic
                ? new SceneViewProjection(SceneProjection.Orthographic, 0, SceneViewBinding.Height, 1 / camera.OrthographicHeight, null, null)
                : new SceneViewProjection(SceneProjection.Perspective, camera.FieldOfView, SceneViewBinding.Height, 1, null, null);
            if (IsValid(own))
            {
                projection = own;
            }
        }

        Vector3 centre = bounds.IsEmpty ? Vector3.Zero : (bounds.Min + bounds.Max) / 2;
        float radius = bounds.IsEmpty ? 1 : MathF.Max((bounds.Max - bounds.Min).Length() / 2, 1e-6f);
        if (!float.IsFinite(radius))
        {
            centre = Vector3.Zero;
            radius = 1;
        }

        if (!found)
        {
            // Far enough for the scene's bounding sphere to fit inside the cone of the field of view.
            float halfAngle = (projection.Kind == SceneProjection.Perspective ? projection.FieldOfView : DefaultProjection.FieldOfView) * MathF.PI / 360;
            float distance = radius / MathF.Sin(halfAngle);
            Matrix4x4.Invert(Matrix4x4.CreateLookAt(centre + DefaultEyeDirection * distance, centre, Vector3.UnitZ), out Matrix4x4 defaultCamera);
            TryPoseFromMatrix(defaultCamera, out pose);
            pose.OrbitDistance = distance;

            if (projection.Kind == SceneProjection.Orthographic && view?.Projection is null)
            {
                projection = new SceneViewProjection(SceneProjection.Orthographic, 0, SceneViewBinding.Min, 1 / (2 * radius), null, null);
            }

            return pose;
        }

        // Orbit about the point the view names, else about where the view axis passes the scene's centre.
        if (view?.OrbitDistance is { } orbit && orbit > 0 && float.IsFinite(orbit))
        {
            pose.OrbitDistance = orbit;
        }
        else
        {
            float along = Vector3.Dot(centre - pose.Position, pose.Forward);
            pose.OrbitDistance = along > radius * 1e-3f && float.IsFinite(along) ? along : radius;
        }

        return pose;
    }

    /// <summary>The world matrix of a node while its own animations, and those of the nodes above it, are at <paramref name="seconds"/>.</summary>
    public static Matrix4x4 AnimatedWorldMatrix(Scene scene, int node, double seconds)
    {
        ReadOnlySpan<SceneNode> nodes = scene.Nodes;
        Matrix4x4 world = Matrix4x4.Identity;
        bool first = true;

        // Parents precede children, so the walk ends; the counter guards against a corrupt tree.
        for (int guard = 0; node >= 0 && guard <= nodes.Length; guard++)
        {
            Matrix4x4 local = nodes[node].Local;
            foreach (SceneAnimation animation in scene.Animations)
            {
                if (animation.NodeName == nodes[node].Name)
                {
                    // The motion happens in the own space of the node, before its placement.
                    local = animation.Sample(seconds) * local;
                }
            }

            world = first ? local : world * local;
            first = false;
            node = nodes[node].Parent;
        }

        return world;
    }

    /// <summary>Whether an animation moves the node or any node above it.</summary>
    public static bool IsAnimatedNode(Scene scene, int node)
    {
        if (scene.Animations.Count == 0)
        {
            return false;
        }

        ReadOnlySpan<SceneNode> nodes = scene.Nodes;
        for (int guard = 0; node >= 0 && guard <= nodes.Length; guard++)
        {
            foreach (SceneAnimation animation in scene.Animations)
            {
                if (animation.NodeName == nodes[node].Name)
                {
                    return true;
                }
            }

            node = nodes[node].Parent;
        }

        return false;
    }

    /// <summary>
    /// The pose a camera has when the node it hangs on has moved by <paramref name="delta"/> (world to
    /// world, rigid): carried along with it, orbit distance kept. A delta that gives no pose changes nothing.
    /// </summary>
    public static CameraPose Carry(in CameraPose pose, in Matrix4x4 delta)
    {
        Matrix4x4 cameraToWorld = Matrix4x4.CreateFromQuaternion(pose.Orientation) * Matrix4x4.CreateTranslation(pose.Position);
        if (!TryPoseFromMatrix(cameraToWorld * delta, out CameraPose carried))
        {
            return pose;
        }

        carried.OrbitDistance = pose.OrbitDistance;
        return carried;
    }

    /// <summary>
    /// A pose from a camera-to-world matrix. Only the view direction (−Z row) and up (Y row) are kept, made
    /// perpendicular, so scale and skew in the matrix do no harm; a matrix that is not finite or whose axes
    /// do not span space is refused.
    /// </summary>
    public static bool TryPoseFromMatrix(in Matrix4x4 cameraToWorld, out CameraPose pose)
    {
        pose = default;
        var x = new Vector3(cameraToWorld.M11, cameraToWorld.M12, cameraToWorld.M13);
        var y = new Vector3(cameraToWorld.M21, cameraToWorld.M22, cameraToWorld.M23);
        var z = new Vector3(cameraToWorld.M31, cameraToWorld.M32, cameraToWorld.M33);
        Vector3 position = cameraToWorld.Translation;

        float lx = x.Length(), ly = y.Length(), lz = z.Length();
        if (!(lx > 1e-12f) || !(ly > 1e-12f) || !(lz > 1e-12f) || !float.IsFinite(lx + ly + lz) || !float.IsFinite(position.X + position.Y + position.Z))
        {
            return false;
        }

        // The volume the unit axes span: 1 for a rotation, 0 when they lie in a plane.
        x /= lx;
        y /= ly;
        z /= lz;
        if (!(MathF.Abs(Vector3.Dot(Vector3.Cross(x, y), z)) > 1e-3f))
        {
            return false;
        }

        Vector3 forward = -z;
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, y));
        Vector3 up = Vector3.Cross(right, forward);
        var rotation = new Matrix4x4(
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            -forward.X, -forward.Y, -forward.Z, 0,
            0, 0, 0, 1);

        pose = new CameraPose { Position = position, Orientation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotation)), OrbitDistance = 1 };
        return pose.IsFinite;
    }

    /// <summary>World space to camera space.</summary>
    public static Matrix4x4 ViewMatrix(in CameraPose pose)
    {
        return Matrix4x4.CreateTranslation(-pose.Position) * Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(pose.Orientation));
    }

    /// <summary>
    /// Near and far clipping distances along the view direction: the projection's own where it gives
    /// usable ones, else fitted around the scene. An orthographic near plane may be behind the camera.
    /// </summary>
    public static (float Near, float Far) ClipPlanes(in CameraPose pose, in SceneViewProjection projection, SceneBounds worldBounds)
    {
        bool perspective = projection.Kind == SceneProjection.Perspective;
        float fallbackFar = MathF.Max(pose.OrbitDistance, 1e-3f) * 4;

        // Extent of the scene's box along the view direction.
        float nearFit, farFit;
        if (worldBounds.IsEmpty)
        {
            nearFit = perspective ? fallbackFar / MaxDepthRatio : -fallbackFar;
            farFit = fallbackFar;
        }
        else
        {
            Vector3 forward = pose.Forward;
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? worldBounds.Min.X : worldBounds.Max.X,
                    (corner & 2) == 0 ? worldBounds.Min.Y : worldBounds.Max.Y,
                    (corner & 4) == 0 ? worldBounds.Min.Z : worldBounds.Max.Z);
                float distance = Vector3.Dot(point - pose.Position, forward);
                minimum = MathF.Min(minimum, distance);
                maximum = MathF.Max(maximum, distance);
            }

            float margin = MathF.Max((maximum - minimum) * 0.01f, 1e-6f);
            nearFit = minimum - margin;
            farFit = maximum + margin;

            if (!float.IsFinite(nearFit) || !float.IsFinite(farFit))
            {
                nearFit = perspective ? fallbackFar / MaxDepthRatio : -fallbackFar;
                farFit = fallbackFar;
            }
            else if (perspective)
            {
                // Everything is behind the camera: any valid pair will do. Otherwise keep the near plane
                // in front of the camera, and close enough to the far plane for the depth buffer.
                if (farFit <= 0)
                {
                    farFit = fallbackFar;
                }

                nearFit = MathF.Max(nearFit, farFit / MaxDepthRatio);
            }
        }

        float near = nearFit, far = farFit;
        if (projection.Near is { } wantedNear && float.IsFinite(wantedNear) && (wantedNear > 0 || (!perspective && wantedNear >= 0)))
        {
            near = wantedNear;
        }

        if (projection.Far is { } wantedFar && float.IsFinite(wantedFar) && wantedFar > near)
        {
            far = wantedFar;
        }

        // Explicit planes that leave nothing between them are worth less than the fitted ones.
        return far > near ? (near, far) : (nearFit, farFit);
    }

    /// <summary>
    /// The projection matrix (OpenGL clip space: depth −1 at the near plane, +1 at the far plane) for the
    /// part <paramref name="region"/> of a view whose whole extent is <paramref name="fullViewSize"/>, in
    /// any unit. False when there is nothing to project onto.
    /// </summary>
    public static bool TryProjectionMatrix(in SceneViewProjection projection, Vector2 fullViewSize, ViewRegion region, float near, float far, out Matrix4x4 matrix)
    {
        matrix = Matrix4x4.Identity;
        float width = fullViewSize.X, height = fullViewSize.Y;
        if (!(width > 0) || !(height > 0) || !float.IsFinite(width + height) || !region.HasArea || !IsValid(projection) || !(far > near) || !float.IsFinite(far - near))
        {
            return false;
        }

        float bound = projection.Binding switch
        {
            SceneViewBinding.Width => width,
            SceneViewBinding.Height => height,
            SceneViewBinding.Max => MathF.Max(width, height),
            _ => MathF.Min(width, height)
        };

        // Half the extent of the whole view on the projection plane: at the near plane for a perspective
        // view, in scene units for an orthographic one.
        bool perspective = projection.Kind == SceneProjection.Perspective;
        float halfBound;
        if (perspective)
        {
            if (!(near > 0))
            {
                return false;
            }

            halfBound = near * MathF.Tan(projection.FieldOfView * MathF.PI / 360);
        }
        else
        {
            halfBound = 0.5f / projection.OrthographicScale;
        }

        float halfWidth = halfBound * width / bound, halfHeight = halfBound * height / bound;

        // The region's edges on that plane; the view's Y runs down, the plane's up.
        float left = -halfWidth + 2 * halfWidth * region.Left;
        float right = -halfWidth + 2 * halfWidth * region.Right;
        float top = halfHeight - 2 * halfHeight * region.Top;
        float bottom = halfHeight - 2 * halfHeight * region.Bottom;

        // Row-vector form (clip = point × matrix) of the classic OpenGL frustum and orthographic matrices.
        matrix = default;
        if (perspective)
        {
            matrix.M11 = 2 * near / (right - left);
            matrix.M22 = 2 * near / (top - bottom);
            matrix.M31 = (right + left) / (right - left);
            matrix.M32 = (top + bottom) / (top - bottom);
            matrix.M33 = -(far + near) / (far - near);
            matrix.M34 = -1;
            matrix.M43 = -2 * far * near / (far - near);
        }
        else
        {
            matrix.M11 = 2 / (right - left);
            matrix.M22 = 2 / (top - bottom);
            matrix.M33 = -2 / (far - near);
            matrix.M41 = -(right + left) / (right - left);
            matrix.M42 = -(top + bottom) / (top - bottom);
            matrix.M43 = -(far + near) / (far - near);
            matrix.M44 = 1;
        }

        return float.IsFinite(matrix.M11 + matrix.M22 + matrix.M31 + matrix.M32 + matrix.M33 + matrix.M41 + matrix.M42 + matrix.M43);
    }
}
