using System.Numerics;
using Caly.Rendering.Scene3D;

namespace Caly.Avalonia.Scene3D.Tests;

/// <summary>Small scenes made by hand.</summary>
internal static class TestScenes
{
    public static SceneNode Node(int name, int parent = -1, Matrix4x4? local = null, int mesh = -1, int camera = -1, bool visible = true)
    {
        return new SceneNode
        {
            Parent = parent,
            Local = local ?? Matrix4x4.Identity,
            Kind = mesh >= 0 ? SceneNodeKind.Model : camera >= 0 ? SceneNodeKind.Camera : SceneNodeKind.Group,
            Mesh = mesh,
            Light = -1,
            Camera = camera,
            Name = name,
            Visible = visible
        };
    }

    /// <summary>Adds an axis-aligned box as one mesh with one part and returns its index.</summary>
    public static int AddBox(SceneBuilder builder, Vector3 min, Vector3 max, string name = "Box", int shadingId = 0)
    {
        builder.BeginMesh(builder.AddName(name), SceneMeshFlags.None);
        Span<float> positions = builder.AllocateVertices(8).Positions;
        for (int corner = 0; corner < 8; corner++)
        {
            positions[corner * 3] = (corner & 1) == 0 ? min.X : max.X;
            positions[corner * 3 + 1] = (corner & 2) == 0 ? min.Y : max.Y;
            positions[corner * 3 + 2] = (corner & 4) == 0 ? min.Z : max.Z;
        }

        ReadOnlySpan<uint> indices =
        [
            0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4,
            2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5
        ];
        indices.CopyTo(builder.AllocateIndices(indices.Length));
        builder.AddPart(indices.Length, shadingId);
        return builder.EndMesh();
    }

    /// <summary>
    /// One box from (-1, -2, -3) to (1, 2, 3) under an identity node, the default material, and
    /// optionally camera nodes at the given poses (named "Camera0", "Camera1"…).
    /// </summary>
    public static Scene Box(params Matrix4x4[] cameras)
    {
        using var builder = new SceneBuilder();
        builder.AddMaterial(new SceneMaterial { Ambient = new Vector3(0.75f), Opacity = 1, Flags = SceneMaterialFlags.Lit, Name = builder.AddName("") });
        int mesh = AddBox(builder, new Vector3(-1, -2, -3), new Vector3(1, 2, 3));
        builder.AddNode(Node(builder.AddName("BoxNode"), mesh: mesh));

        for (int i = 0; i < cameras.Length; i++)
        {
            int camera = builder.AddCamera(new SceneCamera { Projection = SceneProjection.Perspective, FieldOfView = 40, Near = 1, Far = 1000, Name = builder.AddName("Camera" + i) });
            builder.AddNode(Node(builder.AddName("Camera" + i), local: cameras[i], camera: camera));
        }

        return builder.Build();
    }

    /// <summary>A box that slides 10 along X in two seconds, looping or holding its end; plays at once if asked.</summary>
    public static Scene AnimatedBox(bool autoPlay, bool loop = true)
    {
        using var builder = new SceneBuilder();
        builder.AddMaterial(new SceneMaterial { Ambient = new Vector3(0.75f), Opacity = 1, Flags = SceneMaterialFlags.Lit, Name = builder.AddName("") });
        int mesh = AddBox(builder, new Vector3(-1), new Vector3(1));
        builder.AddNode(Node(builder.AddName("Mover"), mesh: mesh));
        builder.AddAnimation(new SceneAnimation("slide", builder.AddName("Mover"),
            [new SceneKeyFrame(0, Vector3.Zero, Quaternion.Identity, Vector3.One), new SceneKeyFrame(2, new Vector3(10, 0, 0), Quaternion.Identity, Vector3.One)], loop, 1, 0));
        builder.AutoPlayAnimations = autoPlay;
        return builder.Build();
    }

    /// <summary>A triangle that follows one bone, which a motion of 2 seconds slides 10 along X; plays at once if asked.</summary>
    public static Scene SkinnedTriangle(bool autoPlay)
    {
        using var builder = new SceneBuilder();
        builder.AddMaterial(new SceneMaterial { Ambient = new Vector3(0.75f), Opacity = 1, Flags = SceneMaterialFlags.Lit, Name = builder.AddName("") });
        int skeleton = builder.AddSkeleton(new SceneSkeleton([new SceneBone(builder.AddName("bone"), -1, 1, Vector3.Zero, Quaternion.Identity)]));
        builder.BeginMesh(builder.AddName("tri"), SceneMeshFlags.None);
        new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }.CopyTo(builder.AllocateVertices(3).Positions);
        new uint[] { 0, 1, 2 }.CopyTo(builder.AllocateIndices(3));
        builder.AddPart(3, 0);
        var motion = new SceneBoneMotion("slide", [[new SceneKeyFrame(0, Vector3.Zero, Quaternion.Identity, Vector3.One), new SceneKeyFrame(2, new Vector3(10, 0, 0), Quaternion.Identity, Vector3.One)]], true, 1, 0, false);
        builder.SetMeshSkin(builder.AddSkin(new SceneSkin(skeleton, motion, new uint[3], [255, 255, 255])));
        int mesh = builder.EndMesh();
        builder.AddNode(Node(builder.AddName("Mover"), mesh: mesh));
        builder.AutoPlayAnimations = autoPlay;
        return builder.Build();
    }

    /// <summary>
    /// A box at the origin, and a camera node "Eye" under a group "Rig" that slides 10 along X in 2 seconds
    /// (looping); the camera looks along +Y from (0, -20, 0) in the group's space, the group stands at x = 1.
    /// </summary>
    public static Scene RiggedCamera(bool autoPlay = false)
    {
        using var builder = new SceneBuilder();
        builder.AddMaterial(new SceneMaterial { Ambient = new Vector3(0.75f), Opacity = 1, Flags = SceneMaterialFlags.Lit, Name = builder.AddName("") });
        int mesh = AddBox(builder, new Vector3(-1), new Vector3(1));
        builder.AddNode(Node(builder.AddName("BoxNode"), mesh: mesh));
        int rig = builder.AddNode(Node(builder.AddName("Rig"), -1, Matrix4x4.CreateTranslation(1, 0, 0)));
        int camera = builder.AddCamera(new SceneCamera { Projection = SceneProjection.Perspective, FieldOfView = 40, Near = 1, Far = 1000, Name = builder.AddName("Eye") });
        builder.AddNode(Node(builder.AddName("Eye"), rig, LookFrom(new Vector3(0, -20, 0), Vector3.Zero, Vector3.UnitZ), camera: camera));
        builder.AddAnimation(new SceneAnimation("slide", builder.AddName("Rig"),
            [new SceneKeyFrame(0, Vector3.Zero, Quaternion.Identity, Vector3.One), new SceneKeyFrame(2, new Vector3(10, 0, 0), Quaternion.Identity, Vector3.One)], true, 1, 0));
        builder.AutoPlayAnimations = autoPlay;
        return builder.Build();
    }

    public static Scene Empty()
    {
        using var builder = new SceneBuilder();
        return builder.Build();
    }

    /// <summary>The pose of a camera at <paramref name="eye"/> looking at <paramref name="target"/>: a camera-to-world matrix.</summary>
    public static Matrix4x4 LookFrom(Vector3 eye, Vector3 target, Vector3 up)
    {
        Matrix4x4.Invert(Matrix4x4.CreateLookAt(eye, target, up), out Matrix4x4 cameraToWorld);
        return cameraToWorld;
    }
}
