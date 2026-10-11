using Scene3D;

namespace Caly.Avalonia.Scene3D.Internal.Gl;

/// <summary>
/// A scene's geometry on the GPU: one buffer per vertex stream and one index buffer, uploaded as the
/// scene holds them, and one vertex array per mesh pointing at the mesh's range of each stream.
/// </summary>
internal sealed class SceneGpuResources
{
    private readonly GlApi _gl;
    private int _positions, _normals, _texCoords, _colors, _indices;
    private int[] _vertexArrays = [];

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

                Attribute(Shaders.AttributePosition, _positions, 3, GlApi.Float, false, 12, mesh.VertexStart);
                Attribute(Shaders.AttributeNormal, _normals, 3, GlApi.Float, false, 12, mesh.NormalStart);
                Attribute(Shaders.AttributeTexCoord, _texCoords, 2, GlApi.Float, false, 8, mesh.TexCoordStart);
                Attribute(Shaders.AttributeColor, _colors, 4, GlApi.UnsignedByte, true, 4, mesh.ColorStart);

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

    /// <summary>Points an attribute of the bound vertex array at a mesh's range of a stream, or switches it off.</summary>
    private void Attribute(int index, int buffer, int size, int type, bool normalized, int stride, int firstVertex)
    {
        if (buffer == 0 || firstVertex < 0)
        {
            // A constant in place of the stream: white, so that an absent colour changes nothing.
            _gl.DisableVertexAttribArray(index);
            _gl.VertexAttrib4(index, 1, 1, 1, 1);
            return;
        }

        _gl.BindBuffer(GlApi.ArrayBuffer, buffer);
        _gl.VertexAttribPointer(index, size, type, normalized, stride, (nint)firstVertex * stride);
        _gl.EnableVertexAttribArray(index);
    }

    public void Release()
    {
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
        _positions = _normals = _texCoords = _colors = _indices = 0;
        VertexBytes = IndexBytes = 0;
    }
}
