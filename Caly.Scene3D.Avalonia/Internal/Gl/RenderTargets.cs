namespace Caly.Scene3D.Avalonia.Internal.Gl;

/// <summary>
/// The control's own multisampled framebuffer, and its resolve into the framebuffer Avalonia provides
/// (which is not multisampled: drawing straight into it leaves every edge stair-stepped).
/// <para>
/// A multisample resolve needs matching formats on some back-ends. If the direct resolve is refused, the
/// samples are resolved into a texture of this class's own first and that is copied across; if even that
/// is refused, the scene is drawn into Avalonia's framebuffer directly, without anti-aliasing.
/// </para>
/// </summary>
internal sealed class RenderTargets
{
    /// <summary>How the multisampled image reaches Avalonia's framebuffer.</summary>
    public enum ResolvePath
    {
        Untested,
        Direct,
        ThroughTexture,
        NoMultisampling
    }

    private readonly GlApi _gl;
    private int _framebuffer, _color, _depth;
    private int _resolveFramebuffer, _resolveTexture;
    private int _width, _height, _samples;

    public RenderTargets(GlApi gl)
    {
        _gl = gl;
        MaxSamples = Math.Max(1, gl.GetInteger(GlApi.MaxSamples));
    }

    public int MaxSamples { get; }

    public ResolvePath Path { get; private set; } = ResolvePath.Untested;

    /// <summary>Samples per pixel of the current framebuffer; 1 when drawing without multisampling.</summary>
    public int Samples => Path == ResolvePath.NoMultisampling ? 1 : Math.Max(1, _samples);

    /// <summary>Bytes of framebuffer memory this class holds.</summary>
    public long Bytes { get; private set; }

    /// <summary>
    /// Binds the framebuffer to draw the scene into, (re)creating it for the size. The sample count is
    /// the most that keeps the framebuffer within <paramref name="budgetBytes"/>: 4, 2 or none.
    /// </summary>
    public void Begin(int target, int width, int height, long budgetBytes)
    {
        int wanted = 1;
        if (Path != ResolvePath.NoMultisampling)
        {
            foreach (int samples in (ReadOnlySpan<int>)[4, 2])
            {
                // Colour and depth, 4 bytes each, per sample.
                if (samples <= MaxSamples && (long)width * height * samples * 8 <= budgetBytes)
                {
                    wanted = samples;
                    break;
                }
            }
        }

        if (wanted == 1)
        {
            Release();
            _samples = 1;
            _gl.BindFramebuffer(GlApi.Framebuffer, target);
            return;
        }

        if (_framebuffer == 0 || width != _width || height != _height || wanted != _samples)
        {
            Release();
            _width = width;
            _height = height;
            _samples = wanted;

            _framebuffer = _gl.GenFramebuffer();
            _gl.BindFramebuffer(GlApi.Framebuffer, _framebuffer);
            _color = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(_color);
            _gl.RenderbufferStorage(wanted, GlApi.Rgba8, width, height);
            _gl.FramebufferRenderbuffer(GlApi.ColorAttachment0, _color);
            _depth = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(_depth);
            _gl.RenderbufferStorage(wanted, GlApi.DepthComponent24, width, height);
            _gl.FramebufferRenderbuffer(GlApi.DepthAttachment, _depth);
            _gl.BindRenderbuffer(0);
            Bytes = (long)width * height * wanted * 8;

            if (!_gl.IsFramebufferComplete() || _gl.DrainErrors() != GlApi.NoError)
            {
                // No multisampled framebuffer on this context: draw directly from now on.
                Release();
                Path = ResolvePath.NoMultisampling;
                _samples = 1;
                _gl.BindFramebuffer(GlApi.Framebuffer, target);
                return;
            }
        }

        _gl.BindFramebuffer(GlApi.Framebuffer, _framebuffer);
    }

    /// <summary>
    /// Brings what was drawn to <paramref name="target"/> and leaves it bound. Returns false the one time
    /// a multisampled frame could not be delivered at all: the caller draws that frame again (it will go
    /// directly to the target).
    /// </summary>
    public bool End(int target)
    {
        if (_samples <= 1 || _framebuffer == 0)
        {
            _gl.BindFramebuffer(GlApi.Framebuffer, target);
            return true;
        }

        _gl.DrainErrors();
        if (Path is ResolvePath.Untested or ResolvePath.Direct)
        {
            _gl.BindFramebuffer(GlApi.ReadFramebuffer, _framebuffer);
            _gl.BindFramebuffer(GlApi.DrawFramebuffer, target);
            _gl.BlitColor(_width, _height);
            if (_gl.DrainErrors() == GlApi.NoError)
            {
                Path = ResolvePath.Direct;
                _gl.BindFramebuffer(GlApi.Framebuffer, target);
                return true;
            }

            Path = ResolvePath.ThroughTexture;
        }

        if (Path == ResolvePath.ThroughTexture)
        {
            if (_resolveFramebuffer == 0)
            {
                _resolveTexture = _gl.GenTexture();
                _gl.BindTexture(GlApi.Texture2D, _resolveTexture);
                _gl.TexImage2D(_width, _height, default);
                _gl.TexParameter(GlApi.TextureMinFilter, GlApi.Nearest);
                _gl.TexParameter(GlApi.TextureMagFilter, GlApi.Nearest);
                _gl.BindTexture(GlApi.Texture2D, 0);
                _resolveFramebuffer = _gl.GenFramebuffer();
                _gl.BindFramebuffer(GlApi.Framebuffer, _resolveFramebuffer);
                _gl.FramebufferTexture2D(GlApi.ColorAttachment0, _resolveTexture);
                Bytes += (long)_width * _height * 4;
            }

            _gl.BindFramebuffer(GlApi.ReadFramebuffer, _framebuffer);
            _gl.BindFramebuffer(GlApi.DrawFramebuffer, _resolveFramebuffer);
            _gl.BlitColor(_width, _height);
            _gl.BindFramebuffer(GlApi.ReadFramebuffer, _resolveFramebuffer);
            _gl.BindFramebuffer(GlApi.DrawFramebuffer, target);
            _gl.BlitColor(_width, _height);
            if (_gl.DrainErrors() == GlApi.NoError)
            {
                _gl.BindFramebuffer(GlApi.Framebuffer, target);
                return true;
            }
        }

        Release();
        Path = ResolvePath.NoMultisampling;
        _samples = 1;
        _gl.BindFramebuffer(GlApi.Framebuffer, target);
        return false;
    }

    public void Release()
    {
        _gl.DeleteFramebuffer(_framebuffer);
        _gl.DeleteRenderbuffer(_color);
        _gl.DeleteRenderbuffer(_depth);
        _gl.DeleteFramebuffer(_resolveFramebuffer);
        _gl.DeleteTexture(_resolveTexture);
        _framebuffer = _color = _depth = _resolveFramebuffer = _resolveTexture = 0;
        _width = _height = 0;
        Bytes = 0;
    }
}
