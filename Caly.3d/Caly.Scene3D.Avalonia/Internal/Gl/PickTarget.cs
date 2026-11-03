namespace Caly.Scene3D.Avalonia.Internal.Gl;

/// <summary>
/// The picking pass's framebuffer: one pixel of colour and depth, not multisampled (blended samples would
/// mix two nodes' colours at an edge). Made on the first pick and kept until the renderer is released.
/// </summary>
internal sealed class PickTarget(GlApi gl)
{
    private int _framebuffer, _color, _depth;

    /// <summary>Binds the target, making it first if need be.</summary>
    /// <exception cref="InvalidOperationException">The driver refused it.</exception>
    public void Bind()
    {
        if (_framebuffer != 0)
        {
            gl.BindFramebuffer(GlApi.Framebuffer, _framebuffer);
            return;
        }

        _framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(GlApi.Framebuffer, _framebuffer);
        _color = gl.GenRenderBuffer();
        gl.BindRenderBuffer(_color);
        gl.RenderBufferStorage(0, GlApi.Rgba8, 1, 1);
        gl.FramebufferRenderBuffer(GlApi.ColorAttachment0, _color);
        _depth = gl.GenRenderBuffer();
        gl.BindRenderBuffer(_depth);
        gl.RenderBufferStorage(0, GlApi.DepthComponent24, 1, 1);
        gl.FramebufferRenderBuffer(GlApi.DepthAttachment, _depth);
        gl.BindRenderBuffer(0);

        if (!gl.IsFramebufferComplete())
        {
            Release();
            throw new InvalidOperationException("The picking framebuffer is not complete.");
        }
    }

    /// <summary>The node drawn into the pixel, or -1 for the background.</summary>
    public int ReadNode()
    {
        Span<byte> rgba = stackalloc byte[4];
        gl.ReadPixels(0, 0, 1, 1, rgba);
        return Picking.Decode(rgba);
    }

    public void Release()
    {
        gl.DeleteFramebuffer(_framebuffer);
        gl.DeleteRenderBuffer(_color);
        gl.DeleteRenderBuffer(_depth);
        _framebuffer = _color = _depth = 0;
    }
}
