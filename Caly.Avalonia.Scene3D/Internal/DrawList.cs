using System.Buffers;
using System.Numerics;
using Scene3D;

namespace Caly.Avalonia.Scene3D.Internal;

/// <summary>One draw: a run of a mesh's indices, for one node instance, with one material.</summary>
internal struct DrawItem
{
    public Matrix4x4 World;
    public int Mesh;

    /// <summary>First index and index count in the scene's index buffer.</summary>
    public int IndexStart;
    public int IndexCount;

    /// <summary>Index into the scene's materials, or -1 when the scene has none.</summary>
    public int Material;
    public int Node;

    /// <summary>Opacity the view gives the node, to be multiplied into the material's.</summary>
    public float Opacity;

    /// <summary>Distance of the instance's centre in front of the camera; set for transparent items.</summary>
    public float Depth;
}

/// <summary>
/// What to draw for a scene and a view, and in which order.
/// <para>
/// <see cref="Prepare"/> does the work that depends only on the scene and the view's node overrides:
/// world matrices, visibility, materials, the fixed order of opaque draws. <see cref="Update"/> runs every
/// frame: it drops what is outside the view volume and orders transparent draws back to front, without
/// allocating.
/// </para>
/// </summary>
internal sealed class DrawList
{
    private struct Record
    {
        public DrawItem Item;
        public Vector3 BoundsMin, BoundsMax;
        public bool Transparent;
    }

    private sealed class RecordOrder : IComparer<Record>
    {
        public static readonly RecordOrder Instance = new();

        // Opaque first; among opaque, by material, then mesh, to keep GPU state changes few.
        public int Compare(Record a, Record b)
        {
            if (a.Transparent != b.Transparent)
            {
                return a.Transparent ? 1 : -1;
            }

            int byMaterial = a.Item.Material.CompareTo(b.Item.Material);
            if (byMaterial != 0)
            {
                return byMaterial;
            }

            int byMesh = a.Item.Mesh.CompareTo(b.Item.Mesh);
            return byMesh != 0 ? byMesh : a.Item.Node.CompareTo(b.Item.Node);
        }
    }

    private static readonly Comparison<DrawItem> FarthestFirst = static (a, b) => b.Depth.CompareTo(a.Depth);

    private Record[] _records = [];
    private int _recordCount;
    private DrawItem[] _opaque = [];
    private DrawItem[] _transparent = [];
    private int _opaqueCount, _transparentCount;

    public ReadOnlySpan<DrawItem> Opaque => _opaque.AsSpan(0, _opaqueCount);

    public ReadOnlySpan<DrawItem> Transparent => _transparent.AsSpan(0, _transparentCount);

    /// <summary>Draws left out of the last <see cref="Update"/> because they are outside the view volume.</summary>
    public int Culled { get; private set; }

    /// <summary>Draws the scene and view have in all, before culling.</summary>
    public int Count => _recordCount;

    /// <summary>The box around everything the view shows, with its overrides applied.</summary>
    public SceneBounds VisibleBounds { get; private set; } = SceneBounds.Empty;

    /// <summary>
    /// Works out the draws of a scene under a view's node overrides.
    /// </summary>
    /// <param name="materialHasAlphaTexture">Per material, whether its texture has transparency; may be shorter than the material list.</param>
    public void Prepare(Scene scene, SceneView? view, ReadOnlySpan<bool> materialHasAlphaTexture = default)
    {
        ReadOnlySpan<SceneNode> nodes = scene.Nodes;
        ReadOnlySpan<SceneMesh> meshes = scene.Meshes;
        ReadOnlySpan<SceneMeshPart> parts = scene.Parts;
        ReadOnlySpan<SceneMaterial> materials = scene.Materials;
        ReadOnlySpan<int> bindings = scene.Bindings;

        _recordCount = 0;
        _opaqueCount = _transparentCount = 0;
        Culled = 0;
        SceneBounds visibleBounds = SceneBounds.Empty;

        Dictionary<string, SceneNodeOverride>? overrides = null;
        if (view is { NodeOverrides.Count: > 0 })
        {
            overrides = new Dictionary<string, SceneNodeOverride>(view.NodeOverrides.Count, StringComparer.Ordinal);
            foreach (SceneNodeOverride item in view.NodeOverrides)
            {
                overrides[item.Name] = item;
            }
        }

        Matrix4x4[] world = ArrayPool<Matrix4x4>.Shared.Rent(Math.Max(nodes.Length, 1));
        float[] opacity = ArrayPool<float>.Shared.Rent(Math.Max(nodes.Length, 1));
        try
        {
            // Parents come before children, so one pass carries transforms and opacity down the tree.
            for (int i = 0; i < nodes.Length; i++)
            {
                SceneNode node = nodes[i];
                bool hasParent = (uint)node.Parent < (uint)i;

                SceneNodeOverride change = default;
                bool changed = overrides is not null && overrides.TryGetValue(scene.GetName(node.Name), out change);

                Matrix4x4 local = changed && change.Transform is { } transform && IsFinite(transform) ? transform : node.Local;
                world[i] = hasParent ? local * world[node.Parent] : local;

                float own = changed && change.Opacity is { } o && float.IsFinite(o) ? Math.Clamp(o, 0f, 1f) : 1f;
                opacity[i] = (hasParent ? opacity[node.Parent] : 1f) * own;

                // Shown or hidden is said of the node alone, as ISO 32000 has it ("the visibility of this
                // node"): views hide the node of an assembly and leave its parts to be seen.
                bool visible = changed && change.Visible is { } v ? v : node.Visible;
                if (!visible || !(opacity[i] > 0) || (uint)node.Mesh >= (uint)meshes.Length)
                {
                    continue;
                }

                SceneMesh mesh = meshes[node.Mesh];
                if (mesh.VertexCount <= 0 || mesh.Bounds.IsEmpty || !TryWorldBounds(mesh.Bounds, world[i], out Vector3 min, out Vector3 max))
                {
                    continue;
                }

                for (int p = 0; p < mesh.PartCount; p++)
                {
                    SceneMeshPart part = parts[mesh.FirstPart + p];
                    if (part.IndexCount <= 0)
                    {
                        continue;
                    }

                    // A part the node binds no material to, or binds a missing one to, uses material 0.
                    int material = materials.IsEmpty ? -1 : 0;
                    if ((uint)part.ShadingId < (uint)node.BindingCount)
                    {
                        int bound = bindings[node.FirstBinding + part.ShadingId];
                        if ((uint)bound < (uint)materials.Length)
                        {
                            material = bound;
                        }
                    }

                    float materialOpacity = material >= 0 ? materials[material].Opacity : 1f;
                    bool transparent = opacity[i] * materialOpacity < 0.999f
                        || ((uint)material < (uint)materialHasAlphaTexture.Length && materialHasAlphaTexture[material]);

                    if (_recordCount == _records.Length)
                    {
                        Array.Resize(ref _records, Math.Max(64, _records.Length * 2));
                    }

                    _records[_recordCount++] = new Record
                    {
                        Item = new DrawItem { World = world[i], Mesh = node.Mesh, IndexStart = part.IndexStart, IndexCount = part.IndexCount, Material = material, Node = i, Opacity = opacity[i] },
                        BoundsMin = min,
                        BoundsMax = max,
                        Transparent = transparent
                    };
                }

                visibleBounds.Include(min);
                visibleBounds.Include(max);
            }
        }
        finally
        {
            ArrayPool<Matrix4x4>.Shared.Return(world);
            ArrayPool<float>.Shared.Return(opacity);
        }

        Array.Sort(_records, 0, _recordCount, RecordOrder.Instance);
        VisibleBounds = visibleBounds;

        if (_opaque.Length < _recordCount)
        {
            _opaque = new DrawItem[_records.Length];
            _transparent = new DrawItem[_records.Length];
        }
    }

    /// <summary>
    /// Selects and orders the draws for a camera. <paramref name="viewMatrix"/> is world to camera space,
    /// <paramref name="viewProjection"/> world to clip space.
    /// </summary>
    public void Update(in Matrix4x4 viewMatrix, in Matrix4x4 viewProjection)
    {
        int opaque = 0, transparent = 0, culled = 0;
        Record[] records = _records;
        for (int i = 0; i < _recordCount; i++)
        {
            ref Record record = ref records[i];
            if (IsOutside(record.BoundsMin, record.BoundsMax, viewProjection))
            {
                culled++;
                continue;
            }

            if (!record.Transparent)
            {
                _opaque[opaque++] = record.Item;
                continue;
            }

            ref DrawItem item = ref _transparent[transparent++];
            item = record.Item;
            item.Depth = -Vector3.Transform((record.BoundsMin + record.BoundsMax) / 2, viewMatrix).Z;
        }

        if (transparent > 1)
        {
            _transparent.AsSpan(0, transparent).Sort(FarthestFirst);
        }

        _opaqueCount = opaque;
        _transparentCount = transparent;
        Culled = culled;
    }

    /// <summary>The world-space box around a mesh's box under a transform; false if it is not finite.</summary>
    private static bool TryWorldBounds(in SceneBounds local, in Matrix4x4 world, out Vector3 min, out Vector3 max)
    {
        if (System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated)
        {
            var rowX = System.Runtime.Intrinsics.Vector128.Create(world.M11, world.M12, world.M13, 0f);
            var rowY = System.Runtime.Intrinsics.Vector128.Create(world.M21, world.M22, world.M23, 0f);
            var rowZ = System.Runtime.Intrinsics.Vector128.Create(world.M31, world.M32, world.M33, 0f);
            var rowT = System.Runtime.Intrinsics.Vector128.Create(world.M41, world.M42, world.M43, 0f);

            Vector3 c = (local.Min + local.Max) * 0.5f;
            Vector3 e = (local.Max - local.Min) * 0.5f;

            var newCenter = rowT + rowX * c.X + rowY * c.Y + rowZ * c.Z;
            var newExtent = System.Runtime.Intrinsics.Vector128.Abs(rowX) * e.X + System.Runtime.Intrinsics.Vector128.Abs(rowY) * e.Y + System.Runtime.Intrinsics.Vector128.Abs(rowZ) * e.Z;

            var minVec = newCenter - newExtent;
            var maxVec = newCenter + newExtent;

            min = new Vector3(minVec[0], minVec[1], minVec[2]);
            max = new Vector3(maxVec[0], maxVec[1], maxVec[2]);

            return float.IsFinite(min.X + min.Y + min.Z + max.X + max.Y + max.Z);
        }

        min = new Vector3(float.PositiveInfinity);
        max = new Vector3(float.NegativeInfinity);
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = Vector3.Transform(
                new Vector3((corner & 1) == 0 ? local.Min.X : local.Max.X, (corner & 2) == 0 ? local.Min.Y : local.Max.Y, (corner & 4) == 0 ? local.Min.Z : local.Max.Z),
                world);
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        return float.IsFinite(min.X + min.Y + min.Z + max.X + max.Y + max.Z);
    }

    /// <summary>Whether a box lies wholly on the outer side of one plane of the view volume.</summary>
    private static bool IsOutside(Vector3 min, Vector3 max, in Matrix4x4 viewProjection)
    {
        if (System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated)
        {
            var rowX = System.Runtime.Intrinsics.Vector128.Create(viewProjection.M11, viewProjection.M12, viewProjection.M13, viewProjection.M14);
            var rowY = System.Runtime.Intrinsics.Vector128.Create(viewProjection.M21, viewProjection.M22, viewProjection.M23, viewProjection.M24);
            var rowZ = System.Runtime.Intrinsics.Vector128.Create(viewProjection.M31, viewProjection.M32, viewProjection.M33, viewProjection.M34);
            var rowW = System.Runtime.Intrinsics.Vector128.Create(viewProjection.M41, viewProjection.M42, viewProjection.M43, viewProjection.M44);

            int outside = 0b111111;
            for (int corner = 0; corner < 8 && outside != 0; corner++)
            {
                float px = (corner & 1) == 0 ? min.X : max.X;
                float py = (corner & 2) == 0 ? min.Y : max.Y;
                float pz = (corner & 4) == 0 ? min.Z : max.Z;

                var clip = rowW + rowX * px + rowY * py + rowZ * pz;
                float cx = clip[0], cy = clip[1], cz = clip[2], cw = clip[3];

                int beyond = (cx < -cw ? 1 : 0) | (cx > cw ? 2 : 0)
                    | (cy < -cw ? 4 : 0) | (cy > cw ? 8 : 0)
                    | (cz < -cw ? 16 : 0) | (cz > cw ? 32 : 0);
                outside &= beyond;
            }

            return outside != 0;
        }

        // One bit per plane, kept while every corner so far is beyond that plane.
        int outsideScalar = 0b111111;
        for (int corner = 0; corner < 8 && outsideScalar != 0; corner++)
        {
            Vector4 clip = Vector4.Transform(
                new Vector4((corner & 1) == 0 ? min.X : max.X, (corner & 2) == 0 ? min.Y : max.Y, (corner & 4) == 0 ? min.Z : max.Z, 1),
                viewProjection);

            int beyond = (clip.X < -clip.W ? 1 : 0) | (clip.X > clip.W ? 2 : 0)
                | (clip.Y < -clip.W ? 4 : 0) | (clip.Y > clip.W ? 8 : 0)
                | (clip.Z < -clip.W ? 16 : 0) | (clip.Z > clip.W ? 32 : 0);
            outsideScalar &= beyond;
        }

        return outsideScalar != 0;
    }

    private static bool IsFinite(in Matrix4x4 m)
    {
        return float.IsFinite(m.M11 + m.M12 + m.M13 + m.M14 + m.M21 + m.M22 + m.M23 + m.M24 + m.M31 + m.M32 + m.M33 + m.M34 + m.M41 + m.M42 + m.M43 + m.M44);
    }
}
