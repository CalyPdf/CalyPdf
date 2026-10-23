using System.Runtime.InteropServices;
using System.Text;
using Avalonia.OpenGL;

namespace Caly.Scene3D.Avalonia.Internal.Gl;

/// <summary>
/// The OpenGL (ES 3.0 level) functions the renderer uses, looked up once from the context Avalonia made
/// current and called through function pointers: no reflection, nothing that Native AOT would trim.
/// <para>
/// Objects are created and deleted only through here, so <see cref="LiveObjects"/> can tell whether
/// everything created was released.
/// </para>
/// <para>
/// For later: Silk.NET (Silk.NET.OpenGLES / Silk.NET.OpenGL) could replace this class, loaded from the
/// same context with <c>GL.GetApi(name =&gt; gl.GetProcAddress(name))</c>; the renderer, shaders and
/// GPU resources would stay. It pays off once many more GL features are needed (debug output, queries,
/// compute) or desktop GL is set up for Linux and macOS. Before switching, check that it publishes under
/// Native AOT without trim warnings and is strong-named (Caly signs every assembly with Caly.snk); pick
/// the GLES binding for ANGLE and desktop GL elsewhere; keep <see cref="LiveObjects"/> (the self-test
/// relies on it) by wrapping the create and delete calls.
/// </para>
/// </summary>
internal sealed unsafe class GlApi
{
    public const int Points = 0x0000;
    public const int Lines = 0x0001;
    public const int Triangles = 0x0004;
    public const int ProgramPointSize = 0x8642;
    public const int DepthBufferBit = 0x0100, ColorBufferBit = 0x4000;
    public const int Less = 0x0201, LessOrEqual = 0x0203;
    public const int One = 1, SrcAlpha = 0x0302, OneMinusSrcAlpha = 0x0303;
    public const int Front = 0x0404, Back = 0x0405;
    public const int CullFace = 0x0B44, DepthTest = 0x0B71, Blend = 0x0BE2, ScissorTest = 0x0C11;
    public const int UnpackAlignment = 0x0CF5, PackAlignment = 0x0D05;
    public const int Texture2D = 0x0DE1, Texture0 = 0x84C0;
    public const int UnsignedByte = 0x1401, UnsignedInt = 0x1405, Float = 0x1406, Int2101010Rev = 0x8D9F;
    public const int PolygonOffsetFill = 0x8037;
    public const int Rgba = 0x1908, Rgba8 = 0x8058, Rgba32F = 0x8814, DepthComponent24 = 0x81A6;
    public const int Nearest = 0x2600, Linear = 0x2601, LinearMipmapLinear = 0x2703;
    public const int TextureMagFilter = 0x2800, TextureMinFilter = 0x2801, TextureWrapS = 0x2802, TextureWrapT = 0x2803;
    public const int Repeat = 0x2901, ClampToEdge = 0x812F;
    public const int Vendor = 0x1F00, Renderer = 0x1F01, Version = 0x1F02;
    public const int ArrayBuffer = 0x8892, ElementArrayBuffer = 0x8893, StaticDraw = 0x88E4;
    public const int FragmentShader = 0x8B30, VertexShader = 0x8B31, CompileStatus = 0x8B81, LinkStatus = 0x8B82, InfoLogLength = 0x8B84;
    public const int ReadFramebuffer = 0x8CA8, DrawFramebuffer = 0x8CA9, Framebuffer = 0x8D40, Renderbuffer = 0x8D41;
    public const int ColorAttachment0 = 0x8CE0, DepthAttachment = 0x8D00, FramebufferComplete = 0x8CD5;
    public const int MaxSamples = 0x8D57, MaxTextureSize = 0x0D33, MaxRenderbufferSize = 0x84E8;
    public const int NoError = 0, OutOfMemory = 0x0505;

    private readonly delegate* unmanaged<int> _getError;
    private readonly delegate* unmanaged<void> _finish;
    private readonly delegate* unmanaged<int, byte*> _getString;
    private readonly delegate* unmanaged<int, int*, void> _getIntegerv;
    private readonly delegate* unmanaged<int, int, int, int, void> _viewport;
    private readonly delegate* unmanaged<float, float, float, float, void> _clearColor;
    private readonly delegate* unmanaged<int, void> _clear;
    private readonly delegate* unmanaged<int, void> _enable;
    private readonly delegate* unmanaged<int, void> _disable;
    private readonly delegate* unmanaged<int, void> _depthFunc;
    private readonly delegate* unmanaged<byte, void> _depthMask;
    private readonly delegate* unmanaged<int, void> _cullFace;
    private readonly delegate* unmanaged<int, int, int, int, void> _blendFuncSeparate;
    private readonly delegate* unmanaged<int, int, void> _pixelStorei;
    private readonly delegate* unmanaged<int, int, int, int, int, int, void*, void> _readPixels;

    private readonly delegate* unmanaged<int, int*, void> _genBuffers;
    private readonly delegate* unmanaged<int, int*, void> _deleteBuffers;
    private readonly delegate* unmanaged<int, int, void> _bindBuffer;
    private readonly delegate* unmanaged<int, nint, void*, int, void> _bufferData;

    private readonly delegate* unmanaged<int, int*, void> _genVertexArrays;
    private readonly delegate* unmanaged<int, int*, void> _deleteVertexArrays;
    private readonly delegate* unmanaged<int, void> _bindVertexArray;
    private readonly delegate* unmanaged<int, int, int, byte, int, nint, void> _vertexAttribPointer;
    private readonly delegate* unmanaged<int, void> _enableVertexAttribArray;
    private readonly delegate* unmanaged<int, void> _disableVertexAttribArray;
    private readonly delegate* unmanaged<int, float, float, float, float, void> _vertexAttrib4f;

    private readonly delegate* unmanaged<int, int> _createShader;
    private readonly delegate* unmanaged<int, int, byte**, int*, void> _shaderSource;
    private readonly delegate* unmanaged<int, void> _compileShader;
    private readonly delegate* unmanaged<int, int, int*, void> _getShaderiv;
    private readonly delegate* unmanaged<int, int, int*, byte*, void> _getShaderInfoLog;
    private readonly delegate* unmanaged<int, void> _deleteShader;
    private readonly delegate* unmanaged<int> _createProgram;
    private readonly delegate* unmanaged<int, int, void> _attachShader;
    private readonly delegate* unmanaged<int, int, byte*, void> _bindAttribLocation;
    private readonly delegate* unmanaged<int, void> _linkProgram;
    private readonly delegate* unmanaged<int, int, int*, void> _getProgramiv;
    private readonly delegate* unmanaged<int, int, int*, byte*, void> _getProgramInfoLog;
    private readonly delegate* unmanaged<int, void> _deleteProgram;
    private readonly delegate* unmanaged<int, void> _useProgram;
    private readonly delegate* unmanaged<int, byte*, int> _getUniformLocation;
    private readonly delegate* unmanaged<int, int, void> _uniform1i;
    private readonly delegate* unmanaged<int, float, void> _uniform1f;
    private readonly delegate* unmanaged<int, int, float*, void> _uniform3fv;
    private readonly delegate* unmanaged<int, int, float*, void> _uniform4fv;
    private readonly delegate* unmanaged<int, int, byte, float*, void> _uniformMatrix3fv;
    private readonly delegate* unmanaged<int, int, byte, float*, void> _uniformMatrix4fv;
    private readonly delegate* unmanaged<int, int, int, nint, void> _drawElements;
    private readonly delegate* unmanaged<int, int, int, void> _drawArrays;
    private readonly delegate* unmanaged<float, float, void> _polygonOffset;
    private readonly delegate* unmanaged<byte, byte, byte, byte, void> _colorMask;

    private readonly delegate* unmanaged<int, int*, void> _genTextures;
    private readonly delegate* unmanaged<int, int*, void> _deleteTextures;
    private readonly delegate* unmanaged<int, int, void> _bindTexture;
    private readonly delegate* unmanaged<int, void> _activeTexture;
    private readonly delegate* unmanaged<int, int, int, int, int, int, int, int, void*, void> _texImage2D;
    private readonly delegate* unmanaged<int, int, int, void> _texParameteri;
    private readonly delegate* unmanaged<int, void> _generateMipmap;

    private readonly delegate* unmanaged<int, int*, void> _genFramebuffers;
    private readonly delegate* unmanaged<int, int*, void> _deleteFramebuffers;
    private readonly delegate* unmanaged<int, int, void> _bindFramebuffer;
    private readonly delegate* unmanaged<int, int, int, int, void> _framebufferRenderbuffer;
    private readonly delegate* unmanaged<int, int, int, int, int, void> _framebufferTexture2D;
    private readonly delegate* unmanaged<int, int> _checkFramebufferStatus;
    private readonly delegate* unmanaged<int, int, int, int, int, int, int, int, int, int, void> _blitFramebuffer;
    private readonly delegate* unmanaged<int, int*, void> _genRenderbuffers;
    private readonly delegate* unmanaged<int, int*, void> _deleteRenderbuffers;
    private readonly delegate* unmanaged<int, int, void> _bindRenderbuffer;
    private readonly delegate* unmanaged<int, int, int, int, int, void> _renderbufferStorageMultisample;

    public GlApi(GlInterface gl, GlVersion version)
    {
        IsEmbedded = version.Type == GlProfileType.OpenGLES;

        _getError = (delegate* unmanaged<int>)Load(gl, "glGetError");
        _finish = (delegate* unmanaged<void>)Load(gl, "glFinish");
        _getString = (delegate* unmanaged<int, byte*>)Load(gl, "glGetString");
        _getIntegerv = (delegate* unmanaged<int, int*, void>)Load(gl, "glGetIntegerv");
        _viewport = (delegate* unmanaged<int, int, int, int, void>)Load(gl, "glViewport");
        _clearColor = (delegate* unmanaged<float, float, float, float, void>)Load(gl, "glClearColor");
        _clear = (delegate* unmanaged<int, void>)Load(gl, "glClear");
        _enable = (delegate* unmanaged<int, void>)Load(gl, "glEnable");
        _disable = (delegate* unmanaged<int, void>)Load(gl, "glDisable");
        _depthFunc = (delegate* unmanaged<int, void>)Load(gl, "glDepthFunc");
        _depthMask = (delegate* unmanaged<byte, void>)Load(gl, "glDepthMask");
        _cullFace = (delegate* unmanaged<int, void>)Load(gl, "glCullFace");
        _blendFuncSeparate = (delegate* unmanaged<int, int, int, int, void>)Load(gl, "glBlendFuncSeparate");
        _pixelStorei = (delegate* unmanaged<int, int, void>)Load(gl, "glPixelStorei");
        _readPixels = (delegate* unmanaged<int, int, int, int, int, int, void*, void>)Load(gl, "glReadPixels");

        _genBuffers = (delegate* unmanaged<int, int*, void>)Load(gl, "glGenBuffers");
        _deleteBuffers = (delegate* unmanaged<int, int*, void>)Load(gl, "glDeleteBuffers");
        _bindBuffer = (delegate* unmanaged<int, int, void>)Load(gl, "glBindBuffer");
        _bufferData = (delegate* unmanaged<int, nint, void*, int, void>)Load(gl, "glBufferData");

        _genVertexArrays = (delegate* unmanaged<int, int*, void>)Load(gl, "glGenVertexArrays");
        _deleteVertexArrays = (delegate* unmanaged<int, int*, void>)Load(gl, "glDeleteVertexArrays");
        _bindVertexArray = (delegate* unmanaged<int, void>)Load(gl, "glBindVertexArray");
        _vertexAttribPointer = (delegate* unmanaged<int, int, int, byte, int, nint, void>)Load(gl, "glVertexAttribPointer");
        _enableVertexAttribArray = (delegate* unmanaged<int, void>)Load(gl, "glEnableVertexAttribArray");
        _disableVertexAttribArray = (delegate* unmanaged<int, void>)Load(gl, "glDisableVertexAttribArray");
        _vertexAttrib4f = (delegate* unmanaged<int, float, float, float, float, void>)Load(gl, "glVertexAttrib4f");

        _createShader = (delegate* unmanaged<int, int>)Load(gl, "glCreateShader");
        _shaderSource = (delegate* unmanaged<int, int, byte**, int*, void>)Load(gl, "glShaderSource");
        _compileShader = (delegate* unmanaged<int, void>)Load(gl, "glCompileShader");
        _getShaderiv = (delegate* unmanaged<int, int, int*, void>)Load(gl, "glGetShaderiv");
        _getShaderInfoLog = (delegate* unmanaged<int, int, int*, byte*, void>)Load(gl, "glGetShaderInfoLog");
        _deleteShader = (delegate* unmanaged<int, void>)Load(gl, "glDeleteShader");
        _createProgram = (delegate* unmanaged<int>)Load(gl, "glCreateProgram");
        _attachShader = (delegate* unmanaged<int, int, void>)Load(gl, "glAttachShader");
        _bindAttribLocation = (delegate* unmanaged<int, int, byte*, void>)Load(gl, "glBindAttribLocation");
        _linkProgram = (delegate* unmanaged<int, void>)Load(gl, "glLinkProgram");
        _getProgramiv = (delegate* unmanaged<int, int, int*, void>)Load(gl, "glGetProgramiv");
        _getProgramInfoLog = (delegate* unmanaged<int, int, int*, byte*, void>)Load(gl, "glGetProgramInfoLog");
        _deleteProgram = (delegate* unmanaged<int, void>)Load(gl, "glDeleteProgram");
        _useProgram = (delegate* unmanaged<int, void>)Load(gl, "glUseProgram");
        _getUniformLocation = (delegate* unmanaged<int, byte*, int>)Load(gl, "glGetUniformLocation");
        _uniform1i = (delegate* unmanaged<int, int, void>)Load(gl, "glUniform1i");
        _uniform1f = (delegate* unmanaged<int, float, void>)Load(gl, "glUniform1f");
        _uniform3fv = (delegate* unmanaged<int, int, float*, void>)Load(gl, "glUniform3fv");
        _uniform4fv = (delegate* unmanaged<int, int, float*, void>)Load(gl, "glUniform4fv");
        _uniformMatrix3fv = (delegate* unmanaged<int, int, byte, float*, void>)Load(gl, "glUniformMatrix3fv");
        _uniformMatrix4fv = (delegate* unmanaged<int, int, byte, float*, void>)Load(gl, "glUniformMatrix4fv");
        _drawElements = (delegate* unmanaged<int, int, int, nint, void>)Load(gl, "glDrawElements");
        _drawArrays = (delegate* unmanaged<int, int, int, void>)Load(gl, "glDrawArrays");
        _polygonOffset = (delegate* unmanaged<float, float, void>)Load(gl, "glPolygonOffset");
        _colorMask = (delegate* unmanaged<byte, byte, byte, byte, void>)Load(gl, "glColorMask");

        _genTextures = (delegate* unmanaged<int, int*, void>)Load(gl, "glGenTextures");
        _deleteTextures = (delegate* unmanaged<int, int*, void>)Load(gl, "glDeleteTextures");
        _bindTexture = (delegate* unmanaged<int, int, void>)Load(gl, "glBindTexture");
        _activeTexture = (delegate* unmanaged<int, void>)Load(gl, "glActiveTexture");
        _texImage2D = (delegate* unmanaged<int, int, int, int, int, int, int, int, void*, void>)Load(gl, "glTexImage2D");
        _texParameteri = (delegate* unmanaged<int, int, int, void>)Load(gl, "glTexParameteri");
        _generateMipmap = (delegate* unmanaged<int, void>)Load(gl, "glGenerateMipmap");

        _genFramebuffers = (delegate* unmanaged<int, int*, void>)Load(gl, "glGenFramebuffers");
        _deleteFramebuffers = (delegate* unmanaged<int, int*, void>)Load(gl, "glDeleteFramebuffers");
        _bindFramebuffer = (delegate* unmanaged<int, int, void>)Load(gl, "glBindFramebuffer");
        _framebufferRenderbuffer = (delegate* unmanaged<int, int, int, int, void>)Load(gl, "glFramebufferRenderbuffer");
        _framebufferTexture2D = (delegate* unmanaged<int, int, int, int, int, void>)Load(gl, "glFramebufferTexture2D");
        _checkFramebufferStatus = (delegate* unmanaged<int, int>)Load(gl, "glCheckFramebufferStatus");
        _blitFramebuffer = (delegate* unmanaged<int, int, int, int, int, int, int, int, int, int, void>)Load(gl, "glBlitFramebuffer");
        _genRenderbuffers = (delegate* unmanaged<int, int*, void>)Load(gl, "glGenRenderbuffers");
        _deleteRenderbuffers = (delegate* unmanaged<int, int*, void>)Load(gl, "glDeleteRenderbuffers");
        _bindRenderbuffer = (delegate* unmanaged<int, int, void>)Load(gl, "glBindRenderbuffer");
        _renderbufferStorageMultisample = (delegate* unmanaged<int, int, int, int, int, void>)Load(gl, "glRenderbufferStorageMultisample");
    }

    /// <summary>Whether the context is OpenGL ES (shader sources differ in their first lines).</summary>
    public bool IsEmbedded { get; }

    /// <summary>Objects created through this instance and not yet deleted.</summary>
    public int LiveObjects { get; private set; }

    private static nint Load(GlInterface gl, string name)
    {
        nint address = gl.GetProcAddress(name);
        return address != 0 ? address : throw new NotSupportedException($"The OpenGL context has no {name}.");
    }

    // ---- state ----

    public int GetError() => _getError();

    /// <summary>Waits until the GPU has executed everything issued so far. For measurements only.</summary>
    public void Finish() => _finish();

    public string GetString(int name) => Marshal.PtrToStringUTF8((nint)_getString(name)) ?? "";

    public int GetInteger(int name)
    {
        int value = 0;
        _getIntegerv(name, &value);
        return value;
    }

    public void Viewport(int x, int y, int width, int height) => _viewport(x, y, width, height);

    public void ClearColor(float r, float g, float b, float a) => _clearColor(r, g, b, a);

    public void Clear(int mask) => _clear(mask);

    public void Enable(int capability) => _enable(capability);

    public void Disable(int capability) => _disable(capability);

    public void DepthFunc(int function) => _depthFunc(function);

    public void DepthMask(bool write) => _depthMask(write ? (byte)1 : (byte)0);

    /// <summary>Which faces are dropped while <see cref="CullFace"/> is enabled: <see cref="Front"/> or <see cref="Back"/>.</summary>
    public void CullFaces(int faces) => _cullFace(faces);

    public void BlendFuncSeparate(int sourceColor, int destinationColor, int sourceAlpha, int destinationAlpha) => _blendFuncSeparate(sourceColor, destinationColor, sourceAlpha, destinationAlpha);

    public void PixelStore(int name, int value) => _pixelStorei(name, value);

    /// <summary>Reads RGBA bytes from the read framebuffer; rows come bottom to top.</summary>
    public void ReadPixels(int x, int y, int width, int height, Span<byte> rgba)
    {
        if (rgba.Length < (long)width * height * 4)
        {
            throw new ArgumentException("The destination is too small.", nameof(rgba));
        }

        fixed (byte* destination = rgba)
        {
            _pixelStorei(PackAlignment, 1);
            _readPixels(x, y, width, height, Rgba, UnsignedByte, destination);
        }
    }

    // ---- buffers and vertex arrays ----

    public int GenBuffer()
    {
        int id = 0;
        _genBuffers(1, &id);
        LiveObjects++;
        return id;
    }

    public void DeleteBuffer(int id)
    {
        if (id != 0)
        {
            _deleteBuffers(1, &id);
            LiveObjects--;
        }
    }

    public void BindBuffer(int target, int id) => _bindBuffer(target, id);

    public void BufferData<T>(int target, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        fixed (T* source = data)
        {
            _bufferData(target, (nint)data.Length * sizeof(T), source, StaticDraw);
        }
    }

    public int GenVertexArray()
    {
        int id = 0;
        _genVertexArrays(1, &id);
        LiveObjects++;
        return id;
    }

    public void DeleteVertexArray(int id)
    {
        if (id != 0)
        {
            _deleteVertexArrays(1, &id);
            LiveObjects--;
        }
    }

    public void BindVertexArray(int id) => _bindVertexArray(id);

    public void VertexAttribPointer(int index, int size, int type, bool normalized, int stride, nint offset)
    {
        _vertexAttribPointer(index, size, type, normalized ? (byte)1 : (byte)0, stride, offset);
    }

    public void EnableVertexAttribArray(int index) => _enableVertexAttribArray(index);

    public void DisableVertexAttribArray(int index) => _disableVertexAttribArray(index);

    public void VertexAttrib4(int index, float x, float y, float z, float w) => _vertexAttrib4f(index, x, y, z, w);

    public void DrawElements(int mode, int count, int type, nint offset) => _drawElements(mode, count, type, offset);

    public void DrawArrays(int mode, int first, int count) => _drawArrays(mode, first, count);

    /// <summary>Moves the depth of the polygons drawn from now on (with <see cref="PolygonOffsetFill"/> enabled).</summary>
    public void PolygonOffset(float factor, float units) => _polygonOffset(factor, units);

    /// <summary>Whether drawing writes colour at all (false: depth only).</summary>
    public void ColorMask(bool write)
    {
        byte b = write ? (byte)1 : (byte)0;
        _colorMask(b, b, b, b);
    }

    // ---- programs ----

    /// <summary>Compiles and links a program, binding the given attribute names to locations 0, 1, 2…</summary>
    /// <exception cref="InvalidOperationException">With the driver's log, when compiling or linking fails.</exception>
    public int CreateProgram(string vertexSource, string fragmentSource, ReadOnlySpan<string> attributes)
    {
        int vertex = CompileShader(VertexShader, vertexSource);
        int fragment = 0, program = 0;
        try
        {
            fragment = CompileShader(FragmentShader, fragmentSource);
            program = _createProgram();
            LiveObjects++;
            _attachShader(program, vertex);
            _attachShader(program, fragment);
            for (int i = 0; i < attributes.Length; i++)
            {
                fixed (byte* name = Encoding.UTF8.GetBytes(attributes[i] + '\0'))
                {
                    _bindAttribLocation(program, i, name);
                }
            }

            _linkProgram(program);
            int linked = 0;
            _getProgramiv(program, LinkStatus, &linked);
            if (linked == 0)
            {
                int length = 0;
                _getProgramiv(program, InfoLogLength, &length);
                var log = new byte[Math.Max(length, 1)];
                fixed (byte* buffer = log)
                {
                    _getProgramInfoLog(program, log.Length, &length, buffer);
                }

                string message = Encoding.UTF8.GetString(log, 0, Math.Clamp(length, 0, log.Length));
                DeleteProgram(program);
                program = 0;
                throw new InvalidOperationException("The shader program did not link: " + message);
            }

            return program;
        }
        finally
        {
            // Linked programs keep what they need; the shader objects can go either way.
            DeleteShader(vertex);
            DeleteShader(fragment);
        }
    }

    private int CompileShader(int type, string source)
    {
        int shader = _createShader(type);
        LiveObjects++;
        byte[] utf8 = Encoding.UTF8.GetBytes(source);
        fixed (byte* text = utf8)
        {
            byte* pointer = text;
            int length = utf8.Length;
            _shaderSource(shader, 1, &pointer, &length);
        }

        _compileShader(shader);
        int compiled = 0;
        _getShaderiv(shader, CompileStatus, &compiled);
        if (compiled != 0)
        {
            return shader;
        }

        int logLength = 0;
        _getShaderiv(shader, InfoLogLength, &logLength);
        var log = new byte[Math.Max(logLength, 1)];
        fixed (byte* buffer = log)
        {
            _getShaderInfoLog(shader, log.Length, &logLength, buffer);
        }

        DeleteShader(shader);
        throw new InvalidOperationException((type == VertexShader ? "The vertex" : "The fragment") + " shader did not compile: " + Encoding.UTF8.GetString(log, 0, Math.Clamp(logLength, 0, log.Length)));
    }

    private void DeleteShader(int id)
    {
        if (id != 0)
        {
            _deleteShader(id);
            LiveObjects--;
        }
    }

    public void DeleteProgram(int id)
    {
        if (id != 0)
        {
            _deleteProgram(id);
            LiveObjects--;
        }
    }

    public void UseProgram(int id) => _useProgram(id);

    public int GetUniformLocation(int program, string name)
    {
        fixed (byte* utf8 = Encoding.UTF8.GetBytes(name + '\0'))
        {
            return _getUniformLocation(program, utf8);
        }
    }

    public void Uniform(int location, int value) => _uniform1i(location, value);

    public void Uniform(int location, float value) => _uniform1f(location, value);

    public void Uniform(int location, System.Numerics.Vector3 value) => _uniform3fv(location, 1, (float*)&value);

    public void Uniform(int location, System.Numerics.Vector4 value) => _uniform4fv(location, 1, (float*)&value);

    public void Uniform3(int location, ReadOnlySpan<float> values)
    {
        fixed (float* source = values)
        {
            _uniform3fv(location, values.Length / 3, source);
        }
    }

    public void Uniform4(int location, ReadOnlySpan<float> values)
    {
        fixed (float* source = values)
        {
            _uniform4fv(location, values.Length / 4, source);
        }
    }

    /// <summary>
    /// A <see cref="System.Numerics.Matrix4x4"/> as it is: its row-vector layout read as OpenGL's columns
    /// is the same transformation for a shader that multiplies <c>matrix * vector</c>.
    /// </summary>
    public void UniformMatrix(int location, in System.Numerics.Matrix4x4 matrix)
    {
        fixed (System.Numerics.Matrix4x4* source = &matrix)
        {
            _uniformMatrix4fv(location, 1, 0, (float*)source);
        }
    }

    /// <summary>Nine floats of a 3 × 3 matrix, in the same layout as <see cref="UniformMatrix"/>.</summary>
    public void UniformMatrix3(int location, ReadOnlySpan<float> nine)
    {
        fixed (float* source = nine)
        {
            _uniformMatrix3fv(location, 1, 0, source);
        }
    }

    // ---- textures ----

    public int GenTexture()
    {
        int id = 0;
        _genTextures(1, &id);
        LiveObjects++;
        return id;
    }

    public void DeleteTexture(int id)
    {
        if (id != 0)
        {
            _deleteTextures(1, &id);
            LiveObjects--;
        }
    }

    public void BindTexture(int target, int id) => _bindTexture(target, id);

    public void ActiveTexture(int unit) => _activeTexture(unit);

    public void TexImage2D(int width, int height, ReadOnlySpan<byte> rgba)
    {
        fixed (byte* source = rgba)
        {
            _pixelStorei(UnpackAlignment, 1);
            _texImage2D(Texture2D, 0, Rgba8, width, height, 0, Rgba, UnsignedByte, rgba.IsEmpty ? null : source);
        }
    }

    /// <summary>Makes the bound texture <paramref name="width"/> × <paramref name="height"/> texels of four 32-bit floats each.</summary>
    public void TexImage2DFloat(int width, int height, ReadOnlySpan<float> rgba)
    {
        fixed (float* source = rgba)
        {
            _pixelStorei(UnpackAlignment, 4);
            _texImage2D(Texture2D, 0, Rgba32F, width, height, 0, Rgba, Float, rgba.IsEmpty ? null : source);
        }
    }

    public void TexParameter(int name, int value) => _texParameteri(Texture2D, name, value);

    public void GenerateMipmap() => _generateMipmap(Texture2D);

    // ---- framebuffers ----

    public int GenFramebuffer()
    {
        int id = 0;
        _genFramebuffers(1, &id);
        LiveObjects++;
        return id;
    }

    public void DeleteFramebuffer(int id)
    {
        if (id != 0)
        {
            _deleteFramebuffers(1, &id);
            LiveObjects--;
        }
    }

    public void BindFramebuffer(int target, int id) => _bindFramebuffer(target, id);

    public void FramebufferRenderbuffer(int attachment, int renderbuffer) => _framebufferRenderbuffer(Framebuffer, attachment, Renderbuffer, renderbuffer);

    public void FramebufferTexture2D(int attachment, int texture) => _framebufferTexture2D(Framebuffer, attachment, Texture2D, texture, 0);

    public bool IsFramebufferComplete() => _checkFramebufferStatus(Framebuffer) == FramebufferComplete;

    /// <summary>Copies the colour of the read framebuffer to the draw framebuffer, same size both.</summary>
    public void BlitColor(int width, int height) => _blitFramebuffer(0, 0, width, height, 0, 0, width, height, ColorBufferBit, Nearest);

    public int GenRenderbuffer()
    {
        int id = 0;
        _genRenderbuffers(1, &id);
        LiveObjects++;
        return id;
    }

    public void DeleteRenderbuffer(int id)
    {
        if (id != 0)
        {
            _deleteRenderbuffers(1, &id);
            LiveObjects--;
        }
    }

    public void BindRenderbuffer(int id) => _bindRenderbuffer(Renderbuffer, id);

    /// <summary>Storage for the bound renderbuffer; <paramref name="samples"/> 0 means not multisampled.</summary>
    public void RenderbufferStorage(int samples, int format, int width, int height) => _renderbufferStorageMultisample(Renderbuffer, samples, format, width, height);

    /// <summary>Discards any pending errors and returns the first, or <see cref="NoError"/>.</summary>
    public int DrainErrors()
    {
        int first = _getError();
        for (int guard = 0; guard < 64 && _getError() != NoError; guard++)
        {
        }

        return first;
    }
}
