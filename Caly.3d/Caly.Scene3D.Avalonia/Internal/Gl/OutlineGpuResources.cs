namespace Caly.Scene3D.Avalonia.Internal.Gl;

/// <summary>
/// The <see cref="OutlineEdges"/> of the scene on the GPU: one buffer, and a vertex array for each mesh that
/// has edges.
/// </summary>
internal sealed class OutlineGpuResources(GlApi gl)
{
    private int _buffer;
    private int[] _vertexArrays = [];
    private int[] _count = [];

    /// <summary>Whether edges are on the GPU.</summary>
    public bool IsLoaded => _buffer != 0;

    /// <summary>Bytes of edge data held on the GPU.</summary>
    public long Bytes { get; private set; }

    /// <summary>The vertex array of a mesh's edges, or 0 for a mesh without.</summary>
    public int VertexArray(int mesh) => (uint)mesh < (uint)_vertexArrays.Length ? _vertexArrays[mesh] : 0;

    /// <summary>Number of edge vertices of a mesh, drawn from 0 in its vertex array.</summary>
    public int Count(int mesh) => (uint)mesh < (uint)_count.Length ? _count[mesh] : 0;

    /// <exception cref="InvalidOperationException">The GPU refused the memory; nothing is left loaded.</exception>
    public void Upload(OutlineEdges edges, int meshCount)
    {
        Release();
        if (edges.VertexCount == 0)
        {
            return;
        }

        gl.DrainErrors();
        _buffer = gl.GenBuffer();
        gl.BindBuffer(GlApi.ArrayBuffer, _buffer);
        gl.BufferData(GlApi.ArrayBuffer, edges.Vertices);
        Bytes = edges.Vertices.Length * 4L;

        const int stride = OutlineEdges.WordsPerVertex * 4;
        _vertexArrays = new int[meshCount];
        _count = new int[meshCount];
        for (int mesh = 0; mesh < meshCount; mesh++)
        {
            int count = edges.Count(mesh);
            if (count == 0)
            {
                continue;
            }

            int vertexArray = gl.GenVertexArray();
            _vertexArrays[mesh] = vertexArray;
            _count[mesh] = count;
            gl.BindVertexArray(vertexArray);

            nint start = (nint)edges.First(mesh) * stride;
            Attribute(Shaders.OutlineAttributePosition, 3, GlApi.Float, false, start);
            Attribute(Shaders.OutlineAttributeMiddle, 3, GlApi.Float, false, start + 12);
            Attribute(Shaders.OutlineAttributeNormalA, 4, GlApi.Int2101010Rev, true, start + 24);
            Attribute(Shaders.OutlineAttributeNormalB, 4, GlApi.Int2101010Rev, true, start + 28);
        }

        gl.BindVertexArray(0);
        gl.BindBuffer(GlApi.ArrayBuffer, 0);

        int error = gl.DrainErrors();
        if (error != GlApi.NoError)
        {
            Release();
            throw new InvalidOperationException(error == GlApi.OutOfMemory
                ? "The GPU has no memory for the model's outlines."
                : $"Uploading the model's outlines failed (OpenGL error 0x{error:X}).");
        }

        void Attribute(int index, int size, int type, bool normalized, nint offset)
        {
            gl.EnableVertexAttribArray(index);
            gl.VertexAttribPointer(index, size, type, normalized, stride, offset);
        }
    }

    public void Release()
    {
        foreach (int vertexArray in _vertexArrays)
        {
            if (vertexArray != 0)
            {
                gl.DeleteVertexArray(vertexArray);
            }
        }

        if (_buffer != 0)
        {
            gl.DeleteBuffer(_buffer);
        }

        _buffer = 0;
        _vertexArrays = [];
        _count = [];
        Bytes = 0;
    }
}
