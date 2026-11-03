namespace Caly.Scene3D.Avalonia;

/// <summary>What a <see cref="Scene3DControl"/> is doing.</summary>
public enum Scene3DStatus
{
    /// <summary>No scene has been set.</summary>
    Empty = 0,

    /// <summary>A scene is set and waits for the GPU: for OpenGL to initialise or for its upload.</summary>
    Uploading,

    /// <summary>The scene is on the GPU and drawn.</summary>
    Ready,

    /// <summary>
    /// The control lost its GPU resources (it left the visual tree, or the GPU context was lost) and the
    /// scene no longer holds its geometry: set a scene that does.
    /// </summary>
    NeedsScene,

    /// <summary>OpenGL is not available here (software rendering, or a platform without GPU interop).</summary>
    Unavailable,

    /// <summary>Something failed; see <see cref="Scene3DControl.StatusMessage"/>.</summary>
    Failed
}

/// <summary>Figures of a <see cref="Scene3DControl"/>'s last frame and of the GPU memory it holds.</summary>
/// <param name="VertexBytes">Vertex data on the GPU.</param>
/// <param name="IndexBytes">Index data on the GPU.</param>
/// <param name="TextureBytes">Texture data on the GPU, mipmaps included.</param>
/// <param name="FramebufferBytes">The control's own multisampled framebuffer.</param>
/// <param name="DrawCalls">Draws of the last frame.</param>
/// <param name="Triangles">Triangles of the last frame.</param>
/// <param name="CulledDraws">Draws left out of the last frame for being outside the view.</param>
/// <param name="Samples">Samples per pixel of the last frame; 1 means no anti-aliasing.</param>
/// <param name="FrameMilliseconds">Time the last frame took to issue, on the CPU.</param>
/// <param name="FramesRendered">Frames drawn since the control was created.</param>
/// <param name="LiveGpuObjects">GPU objects created and not yet released.</param>
/// <param name="TexturesLoaded">Textures on the GPU.</param>
/// <param name="TexturesFailed">Textures that could not be decoded or uploaded.</param>
/// <param name="TexturesPending">Textures still being decoded or waiting to be uploaded; the model is complete at 0.</param>
public readonly record struct Scene3DStatistics(
    long VertexBytes,
    long IndexBytes,
    long TextureBytes,
    long FramebufferBytes,
    int DrawCalls,
    int Triangles,
    int CulledDraws,
    int Samples,
    double FrameMilliseconds,
    long FramesRendered,
    int LiveGpuObjects,
    int TexturesLoaded,
    int TexturesFailed,
    int TexturesPending)
{
    /// <summary>All GPU memory the control accounts for.</summary>
    public long TotalBytes => VertexBytes + IndexBytes + TextureBytes + FramebufferBytes;
}
