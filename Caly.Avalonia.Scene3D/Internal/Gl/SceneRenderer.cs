using System.Diagnostics;
using System.Numerics;
using Scene3D;

namespace Caly.Avalonia.Scene3D.Internal.Gl;

/// <summary>What one frame is drawn with.</summary>
internal struct FrameSetup
{
    public CameraPose Pose;
    public SceneViewProjection Projection;

    /// <summary>Size of the whole view, in any unit; only its proportions matter.</summary>
    public Vector2 FullViewSize;

    /// <summary>The part of the whole view the framebuffer shows.</summary>
    public ViewRegion Region;

    /// <summary>Lighting and node overrides; null for the scene as it is.</summary>
    public SceneView? View;

    /// <summary>Background colour, not premultiplied; alpha 0 leaves what is behind the control visible.</summary>
    public Vector4 Background;

    /// <summary>Seconds into the scene's animations, or null for the scene as it is placed.</summary>
    public double? AnimationTime;

    /// <summary>Most bytes the multisampled framebuffer may take.</summary>
    public long FramebufferBudget;
}

/// <summary>
/// Draws a scene with OpenGL. Every method must be called with the control's GL context current.
/// </summary>
internal sealed class SceneRenderer
{
    private readonly GlApi _gl;
    private readonly SceneGpuResources _resources;
    private readonly RenderTargets _targets;
    private readonly DrawList _drawList = new();

    private readonly int _program;
    private readonly int _uModelView, _uProjection, _uNormalMatrix, _uTexTransform, _uPointSize;
    private readonly int _uFlags, _uDiffuse, _uSpecular, _uEmissive, _uOpacity, _uShininess, _uAlphaCutoff, _uTexture;
    private readonly int _uAmbientLight, _uLightCount, _uLightPosition, _uLightColor, _uLightAttenuation, _uLightSpot;

    private readonly ShaderLight[] _lights = new ShaderLight[LightRigs.MaxLights];
    private readonly float[] _lightPositions = new float[LightRigs.MaxLights * 4];
    private readonly float[] _lightColors = new float[LightRigs.MaxLights * 3];
    private readonly float[] _lightAttenuations = new float[LightRigs.MaxLights * 3];
    private readonly float[] _lightSpots = new float[LightRigs.MaxLights * 4];
    private readonly float[] _normalMatrix = new float[9];

    /// <summary>
    /// Texture rows are stored top first, and the formats measure V from the bottom of the image: V is
    /// turned over after the layer's own transform.
    /// </summary>
    private static readonly Matrix4x4 FlipV = Matrix4x4.CreateScale(1, -1, 1) * Matrix4x4.CreateTranslation(0, 1, 0);

    /// <summary>Longest a frame spends uploading textures that have arrived.</summary>
    private static readonly TimeSpan UploadTimePerFrame = TimeSpan.FromMilliseconds(4);

    private readonly TextureStreamer _streamer;
    private int[] _textureIds = [];
    private bool[] _materialHasAlphaTexture = [];
    private int[] _textureOfMaterial = [], _layerOfMaterial = [];
    private byte[] _repeatOfTexture = [];

    private Scene? _scene;
    private SceneView? _preparedView;
    private double? _preparedAnimationTime;
    private bool _drawListStale = true;

    /// <param name="texturesReady">Called on a thread-pool thread when a decoded texture waits to be uploaded: time to ask for a frame.</param>
    public SceneRenderer(GlApi gl, Action texturesReady)
    {
        _gl = gl;
        _streamer = new TextureStreamer(texturesReady);
        _resources = new SceneGpuResources(gl);
        _targets = new RenderTargets(gl);

        string header = Shaders.Header(gl.IsEmbedded);
        _program = gl.CreateProgram(header + Shaders.Vertex, header + Shaders.Fragment, Shaders.Attributes);

        _uModelView = gl.GetUniformLocation(_program, "uModelView");
        _uProjection = gl.GetUniformLocation(_program, "uProjection");
        _uNormalMatrix = gl.GetUniformLocation(_program, "uNormalMatrix");
        _uTexTransform = gl.GetUniformLocation(_program, "uTexTransform");
        _uPointSize = gl.GetUniformLocation(_program, "uPointSize");
        _uFlags = gl.GetUniformLocation(_program, "uFlags");
        _uDiffuse = gl.GetUniformLocation(_program, "uDiffuse");
        _uSpecular = gl.GetUniformLocation(_program, "uSpecular");
        _uEmissive = gl.GetUniformLocation(_program, "uEmissive");
        _uOpacity = gl.GetUniformLocation(_program, "uOpacity");
        _uShininess = gl.GetUniformLocation(_program, "uShininess");
        _uAlphaCutoff = gl.GetUniformLocation(_program, "uAlphaCutoff");
        _uTexture = gl.GetUniformLocation(_program, "uTexture");
        _uAmbientLight = gl.GetUniformLocation(_program, "uAmbientLight");
        _uLightCount = gl.GetUniformLocation(_program, "uLightCount");
        _uLightPosition = gl.GetUniformLocation(_program, "uLightPosition");
        _uLightColor = gl.GetUniformLocation(_program, "uLightColor");
        _uLightAttenuation = gl.GetUniformLocation(_program, "uLightAttenuation");
        _uLightSpot = gl.GetUniformLocation(_program, "uLightSpot");
    }

    public GlApi Gl => _gl;

    public RenderTargets Targets => _targets;

    public SceneGpuResources Resources => _resources;

    /// <summary>The box around what the current view shows; valid after a frame was drawn.</summary>
    public SceneBounds VisibleBounds => _drawList.VisibleBounds;

    public int LastDrawCalls { get; private set; }

    public int LastTriangles { get; private set; }

    public int LastCulled { get; private set; }

    /// <summary>Bytes of texture memory on the GPU, mipmaps included.</summary>
    public long TextureBytes { get; private set; }

    public int TexturesLoaded { get; private set; }

    /// <summary>Textures still being decoded or waiting to be uploaded.</summary>
    public int TexturesPending => _streamer.Pending;

    /// <summary>Textures that could not be decoded or uploaded.</summary>
    public int TexturesFailed => _streamer.Failed + _failedUploads;

    /// <summary>Whether decoded textures were left waiting when the last frame ran out of upload time.</summary>
    public bool MoreUploadsReady { get; private set; }

    private int _failedUploads;

    /// <summary>
    /// Uploads a scene's geometry and makes it the scene that is drawn. The scene must hold its geometry;
    /// afterwards the renderer needs only its nodes, meshes, parts and materials.
    /// </summary>
    public void SetScene(Scene scene)
    {
        _scene = null;
        ReleaseTextures();
        _resources.Upload(scene);
        _scene = scene;
        _drawListStale = true;

        // Which texture each material shows, and how each texture repeats (as its first user asks).
        TextureStreamer.TexturesOfMaterials(scene, out _textureOfMaterial, out _layerOfMaterial);
        _textureIds = new int[scene.Textures.Count];
        _materialHasAlphaTexture = new bool[scene.Materials.Length];
        _repeatOfTexture = new byte[scene.Textures.Count];
        var seen = new bool[scene.Textures.Count];
        for (int m = 0; m < _textureOfMaterial.Length; m++)
        {
            int texture = _textureOfMaterial[m];
            if (texture >= 0 && !seen[texture])
            {
                seen[texture] = true;
                _repeatOfTexture[texture] = scene.TextureLayers[_layerOfMaterial[m]].Repeat;
            }
        }

        _streamer.Start(scene);
    }

    /// <summary>Stops drawing the scene and frees its GPU memory.</summary>
    public void ClearScene()
    {
        _scene = null;
        ReleaseTextures();
        _resources.Release();
        _drawListStale = true;
    }

    private void ReleaseTextures()
    {
        _streamer.Stop();
        foreach (int id in _textureIds)
        {
            _gl.DeleteTexture(id);
        }

        _textureIds = [];
        _materialHasAlphaTexture = [];
        _textureOfMaterial = [];
        _layerOfMaterial = [];
        TextureBytes = 0;
        TexturesLoaded = 0;
        _failedUploads = 0;
        MoreUploadsReady = false;
    }

    /// <summary>Uploads textures that have been decoded since the last frame, for a few milliseconds at most.</summary>
    private void UploadReadyTextures()
    {
        MoreUploadsReady = false;
        long started = Stopwatch.GetTimestamp();
        while (_streamer.TryTake(out int index, out DecodedTexture pixels))
        {
            using (pixels)
            {
                if ((uint)index >= (uint)_textureIds.Length || _textureIds[index] != 0)
                {
                    continue;
                }

                _gl.DrainErrors();
                int id = _gl.GenTexture();
                _gl.ActiveTexture(GlApi.Texture0);
                _gl.BindTexture(GlApi.Texture2D, id);
                _gl.TexImage2D(pixels.Width, pixels.Height, pixels.Pixels);
                _gl.GenerateMipmap();
                _gl.TexParameter(GlApi.TextureMinFilter, GlApi.LinearMipmapLinear);
                _gl.TexParameter(GlApi.TextureMagFilter, GlApi.Linear);
                _gl.TexParameter(GlApi.TextureWrapS, (_repeatOfTexture[index] & 1) != 0 ? GlApi.Repeat : GlApi.ClampToEdge);
                _gl.TexParameter(GlApi.TextureWrapT, (_repeatOfTexture[index] & 2) != 0 ? GlApi.Repeat : GlApi.ClampToEdge);
                _gl.BindTexture(GlApi.Texture2D, 0);

                if (_gl.DrainErrors() != GlApi.NoError)
                {
                    _gl.DeleteTexture(id);
                    _failedUploads++;
                    continue;
                }

                _textureIds[index] = id;
                TextureBytes += TextureStreamer.GpuBytes(pixels.Width, pixels.Height);
                TexturesLoaded++;

                // A texture with transparency moves the materials that show it to the transparent pass.
                if (pixels.HasAlpha)
                {
                    for (int m = 0; m < _textureOfMaterial.Length; m++)
                    {
                        if (_textureOfMaterial[m] == index)
                        {
                            _materialHasAlphaTexture[m] = true;
                            _drawListStale = true;
                        }
                    }
                }
            }

            if (Stopwatch.GetElapsedTime(started) > UploadTimePerFrame)
            {
                MoreUploadsReady = true;
                break;
            }
        }
    }

    /// <summary>
    /// Draws one frame into <paramref name="target"/> and leaves that framebuffer bound. With
    /// <paramref name="drawScene"/> false only the background is drawn.
    /// </summary>
    public void Render(int target, int pixelWidth, int pixelHeight, in FrameSetup setup, bool drawScene = true)
    {
        LastDrawCalls = LastTriangles = LastCulled = 0;
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return;
        }

        UploadReadyTextures();
        Scene? scene = drawScene ? _scene : null;
        Matrix4x4 view = drawScene ? ViewCamera.ViewMatrix(setup.Pose) : Matrix4x4.Identity;
        Matrix4x4 projection = default;
        bool drawable = false;
        if (scene is not null)
        {
            if (_drawListStale || !ReferenceEquals(_preparedView, setup.View) || _preparedAnimationTime != setup.AnimationTime)
            {
                _drawList.Prepare(scene, setup.View, _materialHasAlphaTexture, setup.AnimationTime);
                _preparedView = setup.View;
                _preparedAnimationTime = setup.AnimationTime;
                _drawListStale = false;
            }

            (float near, float far) = ViewCamera.ClipPlanes(setup.Pose, setup.Projection, _drawList.VisibleBounds);
            drawable = _drawList.Count > 0 && ViewCamera.TryProjectionMatrix(setup.Projection, setup.FullViewSize, setup.Region, near, far, out projection);
        }

        // A second pass only when the first multisampled frame could not be delivered (see RenderTargets).
        for (int attempt = 0; attempt < 2; attempt++)
        {
            _targets.Begin(target, pixelWidth, pixelHeight, setup.FramebufferBudget);
            _gl.Viewport(0, 0, pixelWidth, pixelHeight);
            _gl.Disable(GlApi.ScissorTest);
            _gl.DepthMask(true);
            float alpha = Math.Clamp(setup.Background.W, 0f, 1f);
            _gl.ClearColor(setup.Background.X * alpha, setup.Background.Y * alpha, setup.Background.Z * alpha, alpha);
            _gl.Clear(GlApi.ColorBufferBit | GlApi.DepthBufferBit);

            if (drawable)
            {
                Draw(scene!, setup, view, projection, pixelHeight);
            }

            if (_targets.End(target))
            {
                break;
            }
        }
    }

    private void Draw(Scene scene, in FrameSetup setup, in Matrix4x4 view, in Matrix4x4 projection, int pixelHeight)
    {
        GlApi gl = _gl;
        _drawList.Update(view, view * projection);
        LastCulled = _drawList.Culled;

        gl.Enable(GlApi.DepthTest);
        gl.DepthFunc(GlApi.LessOrEqual);
        gl.Disable(GlApi.CullFace);
        gl.Disable(GlApi.Blend);

        gl.UseProgram(_program);
        gl.UniformMatrix(_uProjection, projection);
        gl.UniformMatrix(_uTexTransform, Matrix4x4.Identity);
        gl.Uniform(_uTexture, 0);

        // The points of a point set: four pixels across on a view 1080 high, never under three. OpenGL ES
        // always takes the size from the shader; desktop OpenGL has to be told to.
        gl.Uniform(_uPointSize, MathF.Max(3f, pixelHeight / 270f));
        if (!gl.IsEmbedded)
        {
            gl.Enable(GlApi.ProgramPointSize);
        }
        SetLights(scene, setup.View?.Lighting ?? SceneLighting.Artwork, view);

        var state = new DrawState { Mesh = -1, Material = int.MinValue, Opacity = float.NaN, Orthographic = setup.Projection.Kind == SceneProjection.Orthographic };
        foreach (ref readonly DrawItem item in _drawList.Opaque)
        {
            DrawOne(scene, item, view, ref state);
        }

        if (!_drawList.Transparent.IsEmpty)
        {
            // Premultiplied colours over what is there; depth is tested but not written, so that one
            // transparent surface does not hide another behind it.
            gl.Enable(GlApi.Blend);
            gl.BlendFuncSeparate(GlApi.One, GlApi.OneMinusSrcAlpha, GlApi.One, GlApi.OneMinusSrcAlpha);
            gl.DepthMask(false);
            foreach (ref readonly DrawItem item in _drawList.Transparent)
            {
                DrawOne(scene, item, view, ref state);
            }

            gl.DepthMask(true);
            gl.Disable(GlApi.Blend);
        }

        gl.BindVertexArray(0);
        gl.UseProgram(0);
    }

    private struct DrawState
    {
        public int Mesh;
        public int Material;
        public float Opacity;
        public SceneMeshFlags MeshFlags;
        public SceneTopology Topology;
        public bool Orthographic;
    }

    private void DrawOne(Scene scene, in DrawItem item, in Matrix4x4 view, ref DrawState state)
    {
        GlApi gl = _gl;
        bool meshChanged = item.Mesh != state.Mesh;
        if (meshChanged)
        {
            int vertexArray = _resources.VertexArray(item.Mesh);
            if (vertexArray == 0)
            {
                return;
            }

            gl.BindVertexArray(vertexArray);
            state.Mesh = item.Mesh;
        }

        SceneMeshFlags meshFlags = scene.Meshes[item.Mesh].Flags;
        SceneTopology topology = scene.Meshes[item.Mesh].Topology;
        if (item.Material != state.Material || item.Opacity != state.Opacity || meshFlags != state.MeshFlags || topology != state.Topology)
        {
            // Line segments and points have no surface for light to fall on: they are drawn in their colour.
            SetMaterial(scene, item.Material, item.Opacity, meshFlags, state.Orthographic, lit: topology == SceneTopology.Triangles);
            state.Material = item.Material;
            state.Opacity = item.Opacity;
            state.MeshFlags = meshFlags;
            state.Topology = topology;
        }

        Matrix4x4 modelView = item.World * view;
        gl.UniformMatrix(_uModelView, modelView);

        // Normals go by the inverse transpose, which differs from the matrix itself under uneven scaling.
        Span<float> n = _normalMatrix;
        if (Matrix4x4.Invert(modelView, out Matrix4x4 inverse))
        {
            n[0] = inverse.M11; n[1] = inverse.M21; n[2] = inverse.M31;
            n[3] = inverse.M12; n[4] = inverse.M22; n[5] = inverse.M32;
            n[6] = inverse.M13; n[7] = inverse.M23; n[8] = inverse.M33;
        }
        else
        {
            n[0] = modelView.M11; n[1] = modelView.M12; n[2] = modelView.M13;
            n[3] = modelView.M21; n[4] = modelView.M22; n[5] = modelView.M23;
            n[6] = modelView.M31; n[7] = modelView.M32; n[8] = modelView.M33;
        }

        gl.UniformMatrix3(_uNormalMatrix, n);
        int mode = topology switch { SceneTopology.Lines => GlApi.Lines, SceneTopology.Points => GlApi.Points, _ => GlApi.Triangles };
        gl.DrawElements(mode, item.IndexCount, GlApi.UnsignedInt, (nint)item.IndexStart * 4);
        LastDrawCalls++;
        LastTriangles += topology == SceneTopology.Triangles ? item.IndexCount / 3 : 0;
    }

    private void SetMaterial(Scene scene, int materialIndex, float nodeOpacity, SceneMeshFlags meshFlags, bool orthographic, bool lit = true)
    {
        GlApi gl = _gl;

        // The format's default material, for a scene that has none at all.
        SceneMaterial material = (uint)materialIndex < (uint)scene.Materials.Length
            ? scene.Materials[materialIndex]
            : new SceneMaterial { Ambient = new Vector3(0.75f), Opacity = 1, Flags = SceneMaterialFlags.Lit };

        // What light picks out is the diffuse colour. A material with none (the format's default has only
        // an ambient colour) would be black under any light; its ambient colour stands in.
        Vector3 diffuse = material.Diffuse == Vector3.Zero ? material.Ambient : material.Diffuse;

        int flags = orthographic ? Shaders.FlagOrthographic : 0;
        flags |= lit && (material.Flags & SceneMaterialFlags.Lit) != 0 ? Shaders.FlagLit : 0;
        flags |= (meshFlags & SceneMeshFlags.HasNormals) != 0 ? Shaders.FlagHasNormals : 0;
        flags |= (meshFlags & SceneMeshFlags.HasColors) != 0 && (material.Flags & SceneMaterialFlags.UseVertexColor) != 0 ? Shaders.FlagVertexColor : 0;
        flags |= (material.Flags & SceneMaterialFlags.AlphaTest) != 0 ? Shaders.FlagAlphaTest : 0;

        // The base-colour texture, once it has arrived, for meshes that carry texture coordinates.
        int texture = (uint)materialIndex < (uint)_textureOfMaterial.Length ? _textureOfMaterial[materialIndex] : -1;
        if (texture >= 0 && _textureIds[texture] != 0 && (meshFlags & SceneMeshFlags.HasTexCoords) != 0)
        {
            SceneTextureLayer layer = scene.TextureLayers[_layerOfMaterial[materialIndex]];
            gl.ActiveTexture(GlApi.Texture0);
            gl.BindTexture(GlApi.Texture2D, _textureIds[texture]);
            flags |= Shaders.FlagTexture | (layer.BlendFunction == SceneBlendFunction.Replace ? Shaders.FlagTextureReplaces : 0);
            gl.UniformMatrix(_uTexTransform, IsFinite(layer.Transform) ? layer.Transform * FlipV : FlipV);
        }

        gl.Uniform(_uFlags, flags);
        gl.Uniform(_uDiffuse, Sanitize(diffuse));
        gl.Uniform(_uSpecular, Sanitize(material.Specular));
        gl.Uniform(_uEmissive, Sanitize(material.Emissive));
        gl.Uniform(_uOpacity, Unit(material.Opacity, 1) * nodeOpacity);
        gl.Uniform(_uShininess, 1 + 127 * Unit(material.Reflectivity, 0));
        gl.Uniform(_uAlphaCutoff, Unit(material.AlphaReference, 0.5f));
    }

    private void SetLights(Scene scene, SceneLighting scheme, in Matrix4x4 view)
    {
        int count = LightRigs.Build(scheme, scene, view, _lights, out Vector3 ambient);
        for (int i = 0; i < count; i++)
        {
            ShaderLight light = _lights[i];
            light.Position.CopyTo(_lightPositions, i * 4);
            light.Color.CopyTo(_lightColors, i * 3);
            light.Attenuation.CopyTo(_lightAttenuations, i * 3);
            light.SpotDirection.CopyTo(_lightSpots, i * 4);
            _lightSpots[i * 4 + 3] = light.SpotCosine;
        }

        _gl.Uniform(_uAmbientLight, ambient);
        _gl.Uniform(_uLightCount, count);
        if (count > 0)
        {
            _gl.Uniform4(_uLightPosition, _lightPositions.AsSpan(0, count * 4));
            _gl.Uniform3(_uLightColor, _lightColors.AsSpan(0, count * 3));
            _gl.Uniform3(_uLightAttenuation, _lightAttenuations.AsSpan(0, count * 3));
            _gl.Uniform4(_uLightSpot, _lightSpots.AsSpan(0, count * 4));
        }
    }

    /// <summary>Reads the bound framebuffer back as RGBA rows, top row first.</summary>
    public byte[] ReadBack(int pixelWidth, int pixelHeight)
    {
        var bottomUp = new byte[pixelWidth * pixelHeight * 4];
        _gl.ReadPixels(0, 0, pixelWidth, pixelHeight, bottomUp);

        var pixels = new byte[bottomUp.Length];
        int stride = pixelWidth * 4;
        for (int y = 0; y < pixelHeight; y++)
        {
            bottomUp.AsSpan((pixelHeight - 1 - y) * stride, stride).CopyTo(pixels.AsSpan(y * stride));
        }

        return pixels;
    }

    /// <summary>Frees every GPU object. The renderer cannot be used afterwards.</summary>
    public void Dispose()
    {
        _scene = null;
        ReleaseTextures();
        _streamer.Dispose();
        _resources.Release();
        _targets.Release();
        _gl.DeleteProgram(_program);
    }

    /// <summary>
    /// For a context that was lost: its objects went with it, so nothing is deleted, but the decoding of
    /// textures nobody will take is stopped. The renderer cannot be used afterwards.
    /// </summary>
    public void Abandon()
    {
        _scene = null;
        _streamer.Dispose();
    }

    private static bool IsFinite(in Matrix4x4 m)
    {
        return float.IsFinite(m.M11 + m.M12 + m.M13 + m.M14 + m.M21 + m.M22 + m.M23 + m.M24 + m.M31 + m.M32 + m.M33 + m.M34 + m.M41 + m.M42 + m.M43 + m.M44);
    }

    private static Vector3 Sanitize(Vector3 color)
    {
        return new Vector3(float.IsFinite(color.X) ? color.X : 0, float.IsFinite(color.Y) ? color.Y : 0, float.IsFinite(color.Z) ? color.Z : 0);
    }

    private static float Unit(float value, float fallback) => float.IsNaN(value) ? fallback : Math.Clamp(value, 0f, 1f);
}
