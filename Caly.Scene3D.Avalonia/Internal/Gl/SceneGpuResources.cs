using Caly.Rendering.Scene3D;

namespace Caly.Scene3D.Avalonia.Internal.Gl;

/// <summary>
/// A scene's geometry on the GPU: one buffer per vertex stream and one index buffer, uploaded as the
/// scene holds them, and one vertex array per mesh pointing at the mesh's range of each stream.
/// </summary>
internal sealed class SceneGpuResources
{
    private readonly GlApi _gl;
    private int _positions, _normals, _texCoords, _colors, _indices, _boneIndices, _boneWeights;
    private int[] _vertexArrays = [];
    private bool[] _skinned = [];
    private int[] _skinStart = [];

    // Every edge of the triangles, for the wireframe render modes: one index buffer, a vertex array per mesh.
    private int _wireIndices;
    private int[] _wireArrays = [];

    public SceneGpuResources(GlApi gl)
    {
        _gl = gl;
    }

    /// <summary>Bytes of vertex data held on the GPU.</summary>
    public long VertexBytes { get; private set; }

    /// <summary>Bytes of index data held on the GPU.</summary>
    public long IndexBytes { get; private set; }

    public int MeshCount => _vertexArrays.Length;

    /// <summary>The vertex array of a mesh, or 0 for a mesh without vertices.</summary>
    public int VertexArray(int mesh) => (uint)mesh < (uint)_vertexArrays.Length ? _vertexArrays[mesh] : 0;

    /// <summary>Whether a mesh has the bones and weights of its vertices on the GPU, for the shader to skin it.</summary>
    public bool IsSkinned(int mesh) => (uint)mesh < (uint)_skinned.Length && _skinned[mesh];

    /// <summary>
    /// Uploads the scene's geometry. The scene must still hold it (<see cref="Scene.HasGeometry"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The GPU refused the memory; what was created is released.</exception>
    public void Upload(Scene scene)
    {
        Release();
        _gl.DrainErrors();

        try
        {
            _positions = UploadStream(GlApi.ArrayBuffer, scene.Positions);
            _normals = UploadStream(GlApi.ArrayBuffer, scene.Normals);
            _texCoords = UploadStream(GlApi.ArrayBuffer, scene.TexCoords);
            _colors = UploadStream(GlApi.ArrayBuffer, scene.Colors);
            VertexBytes = ((long)scene.Positions.Length + scene.Normals.Length + scene.TexCoords.Length + scene.Colors.Length) * 4;

            ReadOnlySpan<SceneMesh> meshes = scene.Meshes;
            int[] skinStart = UploadSkins(scene, meshes);
            _skinStart = skinStart;
            _vertexArrays = new int[meshes.Length];
            for (int i = 0; i < meshes.Length; i++)
            {
                SceneMesh mesh = meshes[i];
                if (mesh.VertexCount <= 0)
                {
                    continue;
                }

                int vertexArray = _gl.GenVertexArray();
                _vertexArrays[i] = vertexArray;
                _gl.BindVertexArray(vertexArray);
                BindMeshAttributes(mesh, skinStart[i]);

                // The index buffer binding belongs to the vertex array, so each one needs it; the data is
                // uploaded once, below, through the last binding.
                if (_indices == 0)
                {
                    _indices = _gl.GenBuffer();
                }

                _gl.BindBuffer(GlApi.ElementArrayBuffer, _indices);
            }

            if (_indices != 0)
            {
                _gl.BufferData(GlApi.ElementArrayBuffer, scene.Indices);
                IndexBytes = scene.Indices.Length * 4L;
            }

            _gl.BindVertexArray(0);
            _gl.BindBuffer(GlApi.ArrayBuffer, 0);

            int error = _gl.DrainErrors();
            if (error != GlApi.NoError)
            {
                throw new InvalidOperationException(error == GlApi.OutOfMemory
                    ? "The GPU has no memory for the scene's geometry."
                    : $"Uploading the scene's geometry failed (OpenGL error 0x{error:X}).");
            }
        }
        catch
        {
            Release();
            throw;
        }
    }

    /// <summary>
    /// Puts the bones and weights of the vertices of every mesh whose skeleton moves into two buffers, one
    /// after another; returns where each mesh's begin (in vertices), or -1.
    /// </summary>
    private int[] UploadSkins(Scene scene, ReadOnlySpan<SceneMesh> meshes)
    {
        var starts = new int[meshes.Length];
        _skinned = new bool[meshes.Length];
        long total = 0;
        for (int i = 0; i < meshes.Length; i++)
        {
            starts[i] = -1;
            if ((uint)meshes[i].Skin < (uint)scene.Skins.Count &&
                scene.Skins[meshes[i].Skin].Motion is not null &&
                scene.Skins[meshes[i].Skin].Indices.Length == meshes[i].VertexCount)
            {
                total += meshes[i].VertexCount;
            }
        }

        if (total == 0)
        {
            return starts;
        }

        var indices = new uint[total];
        var weights = new uint[total];
        int next = 0;
        for (int i = 0; i < meshes.Length; i++)
        {
            if ((uint)meshes[i].Skin >= (uint)scene.Skins.Count ||
                scene.Skins[meshes[i].Skin].Motion is null ||
                scene.Skins[meshes[i].Skin].Indices.Length != meshes[i].VertexCount)
            {
                continue;
            }

            SceneSkin skin = scene.Skins[meshes[i].Skin];
            skin.Indices.CopyTo(indices.AsSpan(next));
            skin.Weights.CopyTo(weights.AsSpan(next));
            starts[i] = next;
            _skinned[i] = true;
            next += meshes[i].VertexCount;
        }

        _boneIndices = UploadStream(GlApi.ArrayBuffer, indices.AsSpan());
        _boneWeights = UploadStream(GlApi.ArrayBuffer, weights.AsSpan());
        VertexBytes += total * 8;
        return starts;
    }

    private int UploadStream<T>(int target, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        int buffer = _gl.GenBuffer();
        _gl.BindBuffer(target, buffer);
        _gl.BufferData(target, data);
        return buffer;
    }

    /// <summary>Points the attributes of the bound vertex array at a mesh's ranges of the vertex streams.</summary>
    private void BindMeshAttributes(in SceneMesh mesh, int skinStart)
    {
        Attribute(Shaders.AttributePosition, _positions, 3, GlApi.Float, false, 12, mesh.VertexStart);
        Attribute(Shaders.AttributeNormal, _normals, 3, GlApi.Float, false, 12, mesh.NormalStart);
        Attribute(Shaders.AttributeTexCoord, _texCoords, 2, GlApi.Float, false, 8, mesh.TexCoordStart);
        Attribute(Shaders.AttributeColor, _colors, 4, GlApi.UnsignedByte, true, 4, mesh.ColorStart);

        // Bones are numbers in bytes, weights fractions of 255; a mesh that is not skinned has none of either.
        Attribute(Shaders.AttributeBoneIndices, _boneIndices, 4, GlApi.UnsignedByte, false, 4, skinStart, 0);
        Attribute(Shaders.AttributeBoneWeights, _boneWeights, 4, GlApi.UnsignedByte, true, 4, skinStart, 0);
    }

    /// <summary>Bytes of edge indices held on the GPU.</summary>
    public long WireBytes { get; private set; }

    /// <summary>The vertex array that draws a mesh's edges (<see cref="WireEdges"/>), or 0 for none.</summary>
    public int WireArray(int mesh) => (uint)mesh < (uint)_wireArrays.Length ? _wireArrays[mesh] : 0;

    /// <summary>
    /// Uploads every edge of the triangles: a vertex array per mesh over the mesh's vertex streams, with the
    /// edges as its indices. The vertex streams must be on the GPU (<see cref="Upload"/>); the scene need not
    /// hold its geometry any more.
    /// </summary>
    /// <exception cref="InvalidOperationException">The GPU refused the memory; no edges are left.</exception>
    public void UploadWires(Scene scene, WireEdges wires)
    {
        ReleaseWires();
        ReadOnlySpan<SceneMesh> meshes = scene.Meshes;
        if (wires.Indices.IsEmpty || _vertexArrays.Length != meshes.Length)
        {
            return;
        }

        _gl.DrainErrors();
        _wireIndices = _gl.GenBuffer();
        _wireArrays = new int[meshes.Length];
        for (int i = 0; i < meshes.Length; i++)
        {
            if (_vertexArrays[i] == 0 || meshes[i].Topology != SceneTopology.Triangles)
            {
                continue;
            }

            _wireArrays[i] = _gl.GenVertexArray();
            _gl.BindVertexArray(_wireArrays[i]);
            BindMeshAttributes(meshes[i], (uint)i < (uint)_skinStart.Length ? _skinStart[i] : -1);
            _gl.BindBuffer(GlApi.ElementArrayBuffer, _wireIndices);
        }

        _gl.BufferData(GlApi.ElementArrayBuffer, wires.Indices);
        WireBytes = wires.Indices.Length * 4L;
        _gl.BindVertexArray(0);
        _gl.BindBuffer(GlApi.ArrayBuffer, 0);

        int error = _gl.DrainErrors();
        if (error != GlApi.NoError)
        {
            ReleaseWires();
            throw new InvalidOperationException(error == GlApi.OutOfMemory
                ? "The GPU has no memory for the model's edges."
                : $"Uploading the model's edges failed (OpenGL error 0x{error:X}).");
        }
    }

    public void ReleaseWires()
    {
        _gl.BindVertexArray(0);
        foreach (int vertexArray in _wireArrays)
        {
            if (vertexArray != 0)
            {
                _gl.DeleteVertexArray(vertexArray);
            }
        }

        _wireArrays = [];
        if (_wireIndices != 0)
        {
            _gl.DeleteBuffer(_wireIndices);
        }

        _wireIndices = 0;
        WireBytes = 0;
    }

    /// <summary>Points an attribute of the bound vertex array at a mesh's range of a stream, or switches it off.</summary>
    private void Attribute(int index, int buffer, int size, int type, bool normalized, int stride, int firstVertex, float absent = 1)
    {
        if (buffer == 0 || firstVertex < 0)
        {
            // A constant in place of the stream: white, so that an absent colour changes nothing.
            _gl.DisableVertexAttribArray(index);
            _gl.VertexAttrib4(index, absent, absent, absent, absent);
            return;
        }

        _gl.BindBuffer(GlApi.ArrayBuffer, buffer);
        _gl.VertexAttribPointer(index, size, type, normalized, stride, (nint)firstVertex * stride);
        _gl.EnableVertexAttribArray(index);
    }

    public void Release()
    {
        ReleaseWires();
        _gl.BindVertexArray(0);
        foreach (int vertexArray in _vertexArrays)
        {
            _gl.DeleteVertexArray(vertexArray);
        }

        _vertexArrays = [];
        _gl.DeleteBuffer(_positions);
        _gl.DeleteBuffer(_normals);
        _gl.DeleteBuffer(_texCoords);
        _gl.DeleteBuffer(_colors);
        _gl.DeleteBuffer(_indices);
        _gl.DeleteBuffer(_boneIndices);
        _gl.DeleteBuffer(_boneWeights);
        _skinned = [];
        _skinStart = [];
        _positions = _normals = _texCoords = _colors = _indices = _boneIndices = _boneWeights = 0;
        VertexBytes = IndexBytes = 0;
    }
}
