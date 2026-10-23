using System.Diagnostics;
using System.Numerics;
using Caly.Rendering.Scene3D;

namespace Caly.Scene3D.Avalonia.Internal.Gl;

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

    /// <summary>The viewer's changes to lights and materials; null for none.</summary>
    public Scene3DShading? Shading;

    /// <summary>The render mode to draw in (the view's, or the host's choice).</summary>
    public SceneRenderMode RenderMode;

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
    private readonly int _uSkinOffset, _uBones;
    private int _boneTexture;
    private float[] _boneTexels = [];

    // The viewer's changes to lights and materials, for the frame being drawn.
    private Scene3DShading _shading = Scene3DShading.AsAuthored;

    private readonly ShaderLight[] _lights = new ShaderLight[LightRigs.MaxLights];
    private readonly float[] _lightPositions = new float[LightRigs.MaxLights * 4];
    private readonly float[] _lightColors = new float[LightRigs.MaxLights * 3];
    private readonly float[] _lightAttenuations = new float[LightRigs.MaxLights * 3];
    private readonly float[] _lightSpots = new float[LightRigs.MaxLights * 4];
    private readonly float[] _normalMatrix = new float[9];

    // The silhouette edges of the render modes that draw them, and the program that draws them.
    private readonly OutlineGpuResources _outlines;
    private readonly int _outlineProgram;
    private readonly int _uOutlineModelView, _uOutlineProjection, _uOutlineNormalMatrix, _uOutlineCrease, _uOutlineOrthographic, _uOutlineColor;

    // Which nodes' edges (or boxes) a pass has drawn: a node's mesh has several draw items, one per material.
    private int[] _outlineDrawnInFrame = [];
    private int _outlineFrame;

    // Every edge of each part, for the wireframe render modes: where each part's begin in the edge indices.
    private int[] _wireFirst = [], _wireCount = [];

    // A unit cube, for the bounding-box render modes: its edges and its faces.
    private readonly int _boxVertices, _boxLineIndices, _boxFaceIndices, _boxLines, _boxFaces;

    // What a render mode changes in the materials for the pass being drawn.
    private MaterialOverride _override = MaterialOverride.None;

    private struct MaterialOverride
    {
        /// <summary>Paint everything in <see cref="Color"/>, untextured; unlit unless <see cref="ColorLit"/>.</summary>
        public bool UseColor;
        public Vector3 Color;

        /// <summary>Light the one colour, shading each face by how it turns to the lights.</summary>
        public bool ColorLit;

        /// <summary>Multiplies every opacity.</summary>
        public float OpacityFactor;

        /// <summary>A share of each surface's colour added as emitted light.</summary>
        public float AddedEmissive;

        /// <summary>Light line segments and points as surfaces are (the shaded wireframe and vertices).</summary>
        public bool LightMarks;

        public static MaterialOverride None => new() { OpacityFactor = 1 };
    }

    private enum Shape
    {
        /// <summary>The item's own primitives: triangles, line segments or points.</summary>
        Primitives,

        /// <summary>Every edge of the item's triangles.</summary>
        Edges,

        /// <summary>The item's vertices.</summary>
        Points
    }

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
    private bool[] _materialCutout = [];
    private int[] _textureOfMaterial = [], _layerOfMaterial = [];

    // Where the surfaces of each textured material sample its texture, taken while the scene still has its geometry.
    private TextureCoverage?[] _coverageOfMaterial = [];
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
        _uSkinOffset = gl.GetUniformLocation(_program, "uSkinOffset");
        _uBones = gl.GetUniformLocation(_program, "uBones");

        _outlines = new OutlineGpuResources(gl);
        _outlineProgram = gl.CreateProgram(header + Shaders.OutlineVertex, header + Shaders.OutlineFragment, Shaders.OutlineAttributes);
        _uOutlineModelView = gl.GetUniformLocation(_outlineProgram, "uModelView");
        _uOutlineProjection = gl.GetUniformLocation(_outlineProgram, "uProjection");
        _uOutlineNormalMatrix = gl.GetUniformLocation(_outlineProgram, "uNormalMatrix");
        _uOutlineCrease = gl.GetUniformLocation(_outlineProgram, "uCreaseCosine");
        _uOutlineOrthographic = gl.GetUniformLocation(_outlineProgram, "uOrthographic");
        _uOutlineColor = gl.GetUniformLocation(_outlineProgram, "uColor");

        // The unit cube of the bounding-box render modes, drawn with the main program: positions only.
        _boxVertices = gl.GenBuffer();
        gl.BindBuffer(GlApi.ArrayBuffer, _boxVertices);
        gl.BufferData(GlApi.ArrayBuffer, (ReadOnlySpan<float>)[0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1, 1]);
        _boxLineIndices = gl.GenBuffer();
        _boxFaceIndices = gl.GenBuffer();
        _boxLines = BoxArray(_boxLineIndices, [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7]);
        _boxFaces = BoxArray(_boxFaceIndices, [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 1, 2, 6, 1, 6, 5, 3, 0, 4, 3, 4, 7]);
        gl.BindBuffer(GlApi.ArrayBuffer, 0);

        int BoxArray(int indexBuffer, ReadOnlySpan<uint> indices)
        {
            int vertexArray = gl.GenVertexArray();
            gl.BindVertexArray(vertexArray);
            gl.BindBuffer(GlApi.ArrayBuffer, _boxVertices);
            gl.VertexAttribPointer(Shaders.AttributePosition, 3, GlApi.Float, false, 12, 0);
            gl.EnableVertexAttribArray(Shaders.AttributePosition);
            for (int attribute = 1; attribute < Shaders.Attributes.Length; attribute++)
            {
                gl.DisableVertexAttribArray(attribute);
            }

            gl.BindBuffer(GlApi.ElementArrayBuffer, indexBuffer);
            gl.BufferData(GlApi.ElementArrayBuffer, indices);
            gl.BindVertexArray(0);
            return vertexArray;
        }
    }

    /// <summary>Whether the scene's edges are on the GPU, for the render modes that draw them.</summary>
    public bool HasEdges => _outlines.IsLoaded || _resources.WireBytes > 0;

    /// <summary>Bytes of edges on the GPU: silhouette edges and every edge.</summary>
    public long EdgeBytes => _outlines.Bytes + _resources.WireBytes;

    /// <summary>Uploads the edges of the scene being drawn: silhouette edges and every edge.</summary>
    /// <exception cref="InvalidOperationException">The GPU refused the memory; there are no edges.</exception>
    public void SetEdges(OutlineEdges outlines, WireEdges wires)
    {
        if (_scene is not { } scene)
        {
            return;
        }

        _outlines.Upload(outlines, scene.Meshes.Length);
        _resources.UploadWires(scene, wires);
        _wireFirst = new int[scene.Parts.Length];
        _wireCount = new int[scene.Parts.Length];
        for (int part = 0; part < _wireFirst.Length; part++)
        {
            _wireFirst[part] = wires.First(part);
            _wireCount[part] = wires.Count(part);
        }
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
        _outlines.Release();
        _wireFirst = _wireCount = [];
        _resources.Upload(scene);
        _scene = scene;
        _drawListStale = true;

        // Which texture each material shows, and how each texture repeats (as its first user asks).
        TextureStreamer.TexturesOfMaterials(scene, out _textureOfMaterial, out _layerOfMaterial);
        _textureIds = new int[scene.Textures.Count];
        _materialHasAlphaTexture = new bool[scene.Materials.Length];
        _materialCutout = new bool[scene.Materials.Length];
        _repeatOfTexture = new byte[scene.Textures.Count];
        _coverageOfMaterial = new TextureCoverage?[scene.Materials.Length];
        for (int m = 0; m < _textureOfMaterial.Length; m++)
        {
            _coverageOfMaterial[m] = _textureOfMaterial[m] >= 0 ? TextureCoverage.Of(scene, m, _layerOfMaterial[m]) : null;
        }

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
        _outlines.Release();
        _wireFirst = _wireCount = [];
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
        _materialCutout = [];
        _coverageOfMaterial = [];
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

                // A texture with transparency moves the materials that show it to the transparent pass, unless its
                // alpha is only ever 0 or 255: a mask, which cuts the surface out and leaves it in the opaque pass,
                // where depth keeps the triangles of one mesh in their place.
                if (pixels.HasAlpha)
                {
                    for (int m = 0; m < _textureOfMaterial.Length; m++)
                    {
                        if (_textureOfMaterial[m] == index)
                        {
                            if (pixels.HasPartialAlpha && (_coverageOfMaterial[m] is not { } coverage || coverage.ShowsTransparencyOf(pixels)))
                            {
                                _materialHasAlphaTexture[m] = true;
                                _drawListStale = true;
                            }
                            else
                            {
                                _materialCutout[m] = true;
                            }
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
        UploadBones();

        gl.Enable(GlApi.DepthTest);
        gl.DepthFunc(GlApi.LessOrEqual);
        gl.Disable(GlApi.CullFace);
        gl.Disable(GlApi.Blend);

        gl.UseProgram(_program);
        gl.UniformMatrix(_uProjection, projection);
        gl.UniformMatrix(_uTexTransform, Matrix4x4.Identity);
        gl.Uniform(_uTexture, 0);
        gl.Uniform(_uBones, Shaders.BoneTextureUnit);
        gl.Uniform(_uSkinOffset, -1);

        // The points of a point set: four pixels across on a view 1080 high, never under three. OpenGL ES
        // always takes the size from the shader; desktop OpenGL has to be told to.
        gl.Uniform(_uPointSize, MathF.Max(3f, pixelHeight / 270f));
        if (!gl.IsEmbedded)
        {
            gl.Enable(GlApi.ProgramPointSize);
        }
        _shading = setup.Shading ?? Scene3DShading.AsAuthored;
        SetLights(scene, setup.View?.Lighting ?? SceneLighting.Artwork, view);

        var state = new DrawState { VertexArray = -1, Material = int.MinValue, Opacity = float.NaN, SkinOffset = -1, Orthographic = setup.Projection.Kind == SceneProjection.Orthographic };

        RenderModePlan plan = RenderModePlan.For(setup.RenderMode);
        Vector3 edgeColor = Sanitize(setup.View?.EdgeColor ?? Vector3.Zero);
        Vector3 faceColor = Sanitize(setup.View?.FaceColor ?? new Vector3(setup.Background.X, setup.Background.Y, setup.Background.Z));
        float addedOpacity = Unit(setup.View?.Opacity ?? 0.5f, 0.5f);
        bool silhouettes = plan.Silhouettes && _outlines.IsLoaded;
        bool edges = plan.Edges != MarkPass.None && _resources.WireBytes > 0;

        // Edges lie on the surfaces they bound: the surfaces are pushed back a little in depth, so that the
        // edges drawn after them are not half hidden by them.
        bool offset = plan.Surfaces != SurfacePass.None && (silhouettes || edges);
        if (offset)
        {
            gl.Enable(GlApi.PolygonOffsetFill);
            gl.PolygonOffset(1, 1);
        }

        switch (plan.Surfaces)
        {
            case SurfacePass.Lit:
                _override = MaterialOverride.None with { AddedEmissive = plan.AddedEmissive };
                DrawSurfaces(scene, view, ref state, allTransparent: false);
                break;
            case SurfacePass.Transparent:
                _override = MaterialOverride.None with { OpacityFactor = addedOpacity };
                DrawSurfaces(scene, view, ref state, allTransparent: true);
                break;
            case SurfacePass.Flat:
                _override = MaterialOverride.None with { UseColor = true, Color = faceColor };
                DrawSurfaces(scene, view, ref state, allTransparent: false);
                break;
            case SurfacePass.DepthOnly:
                // Only to hide what is behind: no colour is written.
                _override = MaterialOverride.None;
                gl.ColorMask(false);
                DrawSurfaces(scene, view, ref state, allTransparent: false, depthOnly: true);
                gl.ColorMask(true);
                break;
        }

        if (offset)
        {
            gl.Disable(GlApi.PolygonOffsetFill);
        }

        if (edges)
        {
            _override = plan.Edges == MarkPass.Flat ? MaterialOverride.None with { UseColor = true, Color = edgeColor } : MaterialOverride.None with { LightMarks = true };
            state.Material = int.MinValue;
            DrawMarks(scene, view, ref state, Shape.Edges);
        }

        if (plan.Points != MarkPass.None)
        {
            _override = plan.Points == MarkPass.Flat ? MaterialOverride.None with { UseColor = true, Color = edgeColor } : MaterialOverride.None with { LightMarks = true };
            state.Material = int.MinValue;

            // Every vertex of a model is many more points than a point set has: half their size.
            gl.Uniform(_uPointSize, MathF.Max(2f, pixelHeight / 540f));
            DrawMarks(scene, view, ref state, Shape.Points);
            gl.Uniform(_uPointSize, MathF.Max(3f, pixelHeight / 270f));
        }

        if (plan.BoxFaces)
        {
            // Lit, so that faces in the background colour (the default) still show as the box they make.
            _override = MaterialOverride.None with { UseColor = true, Color = faceColor, ColorLit = true, OpacityFactor = addedOpacity };
            state.Material = int.MinValue;
            gl.Enable(GlApi.Blend);
            gl.BlendFuncSeparate(GlApi.One, GlApi.OneMinusSrcAlpha, GlApi.One, GlApi.OneMinusSrcAlpha);
            gl.DepthMask(false);
            DrawBoxes(scene, view, ref state, faces: true);
            gl.DepthMask(true);
            gl.Disable(GlApi.Blend);
        }

        if (plan.BoxEdges)
        {
            _override = MaterialOverride.None with { UseColor = true, Color = edgeColor };
            state.Material = int.MinValue;
            DrawBoxes(scene, view, ref state, faces: false);
        }

        _override = MaterialOverride.None;
        if (silhouettes)
        {
            DrawOutlines(scene, setup, view, projection);
        }

        gl.BindVertexArray(0);
        gl.UseProgram(0);
    }

    /// <summary>
    /// The silhouette edges of every node drawn, over the surfaces, hidden where surfaces are in front:
    /// each node's once, though its mesh has a draw item per material.
    /// </summary>
    private void DrawOutlines(Scene scene, in FrameSetup setup, in Matrix4x4 view, in Matrix4x4 projection)
    {
        GlApi gl = _gl;
        gl.UseProgram(_outlineProgram);
        gl.UniformMatrix(_uOutlineProjection, projection);
        gl.Uniform(_uOutlineCrease, OutlineEdges.CreaseCosine(setup.View?.CreaseAngle ?? 45));
        gl.Uniform(_uOutlineOrthographic, setup.Projection.Kind == SceneProjection.Orthographic ? 1 : 0);
        gl.Uniform(_uOutlineColor, Sanitize(setup.View?.EdgeColor ?? Vector3.Zero));

        StartNodePass(scene);
        DrawOutlinesOf(_drawList.Opaque, view);
        DrawOutlinesOf(_drawList.Transparent, view);
    }

    private void DrawOutlinesOf(ReadOnlySpan<DrawItem> items, in Matrix4x4 view)
    {
        GlApi gl = _gl;
        foreach (ref readonly DrawItem item in items)
        {
            int vertexArray = _outlines.VertexArray(item.Mesh);
            if (vertexArray == 0)
            {
                continue;
            }

            if (!FirstOfNode(item.Node))
            {
                continue;
            }

            Matrix4x4 modelView = item.World * view;
            gl.UniformMatrix(_uOutlineModelView, modelView);
            NormalMatrix(modelView, _normalMatrix);
            gl.UniformMatrix3(_uOutlineNormalMatrix, _normalMatrix);
            gl.BindVertexArray(vertexArray);
            gl.DrawArrays(GlApi.Lines, 0, _outlines.Count(item.Mesh));
            LastDrawCalls++;
        }
    }

    /// <summary>The matrix normals go by: the inverse transpose, which differs from the matrix itself under uneven scaling.</summary>
    private static void NormalMatrix(in Matrix4x4 modelView, Span<float> n)
    {
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
    }

    /// <summary>
    /// The surfaces: opaque items, then transparent ones blended back to front. With
    /// <paramref name="allTransparent"/> every item is blended (the transparent render modes); with
    /// <paramref name="depthOnly"/> nothing is blended, as no colour is written.
    /// </summary>
    private void DrawSurfaces(Scene scene, in Matrix4x4 view, ref DrawState state, bool allTransparent, bool depthOnly = false)
    {
        GlApi gl = _gl;
        state.Material = int.MinValue;
        if (!allTransparent)
        {
            foreach (ref readonly DrawItem item in _drawList.Opaque)
            {
                DrawOne(scene, item, view, ref state);
            }
        }

        if (_drawList.Transparent.IsEmpty && !allTransparent)
        {
            return;
        }

        // Premultiplied colours over what is there; depth is tested but not written, so that one
        // transparent surface does not hide another behind it. Within a mesh the triangles would blend in
        // index order, so a closed shell (a bulb of glass) shows its near wall under its far wall in
        // patches: each mesh is drawn back faces first, then front faces, and a triangle is in one of the two.
        if (!depthOnly)
        {
            gl.Enable(GlApi.Blend);
            gl.BlendFuncSeparate(GlApi.One, GlApi.OneMinusSrcAlpha, GlApi.One, GlApi.OneMinusSrcAlpha);
            gl.DepthMask(false);
        }

        if (allTransparent)
        {
            DrawBlended(scene, _drawList.Opaque, view, ref state);
        }

        DrawBlended(scene, _drawList.Transparent, view, ref state);
        gl.Disable(GlApi.CullFace);
        gl.DepthMask(true);
        gl.Disable(GlApi.Blend);
    }

    private void DrawBlended(Scene scene, ReadOnlySpan<DrawItem> items, in Matrix4x4 view, ref DrawState state)
    {
        GlApi gl = _gl;
        foreach (ref readonly DrawItem item in items)
        {
            if (scene.Meshes[item.Mesh].Topology != SceneTopology.Triangles)
            {
                gl.Disable(GlApi.CullFace);
                DrawOne(scene, item, view, ref state);
                continue;
            }

            gl.Enable(GlApi.CullFace);
            gl.CullFaces(GlApi.Front);
            DrawOne(scene, item, view, ref state, countTriangles: false);
            gl.CullFaces(GlApi.Back);
            DrawOne(scene, item, view, ref state);
        }
    }

    /// <summary>Every edge, or every vertex, of every item; line segments and points are drawn as they are.</summary>
    private void DrawMarks(Scene scene, in Matrix4x4 view, ref DrawState state, Shape shape)
    {
        foreach (ref readonly DrawItem item in _drawList.Opaque)
        {
            DrawOne(scene, item, view, ref state, shape: shape);
        }

        foreach (ref readonly DrawItem item in _drawList.Transparent)
        {
            DrawOne(scene, item, view, ref state, shape: shape);
        }
    }

    /// <summary>The box of each node drawn, around its mesh as the mesh's own coordinates bound it: edges or faces.</summary>
    private void DrawBoxes(Scene scene, in Matrix4x4 view, ref DrawState state, bool faces)
    {
        GlApi gl = _gl;
        gl.BindVertexArray(faces ? _boxFaces : _boxLines);
        state.VertexArray = -1;
        if (state.SkinOffset != -1)
        {
            gl.Uniform(_uSkinOffset, -1);
            state.SkinOffset = -1;
        }

        StartNodePass(scene);
        DrawBoxesOf(scene, _drawList.Opaque, view, ref state, faces);
        DrawBoxesOf(scene, _drawList.Transparent, view, ref state, faces);
    }

    private void DrawBoxesOf(Scene scene, ReadOnlySpan<DrawItem> items, in Matrix4x4 view, ref DrawState state, bool faces)
    {
        GlApi gl = _gl;
        foreach (ref readonly DrawItem item in items)
        {
            if (!FirstOfNode(item.Node))
            {
                continue;
            }

            SceneBounds bounds = scene.Meshes[item.Mesh].Bounds;
            if (bounds.IsEmpty)
            {
                continue;
            }

            if (item.Material != state.Material)
            {
                SetMaterial(scene, item.Material, item.Opacity, SceneMeshFlags.None, state.Orthographic, lit: false);
                state.Material = item.Material;
                state.Opacity = item.Opacity;
            }

            // A flat box (a sheet) still gets a little depth, so that its faces and edges are drawn.
            Vector3 size = Vector3.Max(bounds.Max - bounds.Min, new Vector3(1e-6f));
            Matrix4x4 modelView = Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(bounds.Min) * item.World * view;
            gl.UniformMatrix(_uModelView, modelView);
            NormalMatrix(modelView, _normalMatrix);
            gl.UniformMatrix3(_uNormalMatrix, _normalMatrix);
            gl.DrawElements(faces ? GlApi.Triangles : GlApi.Lines, faces ? 36 : 24, GlApi.UnsignedInt, 0);
            LastDrawCalls++;
        }
    }

    /// <summary>Starts a pass that draws something once per node.</summary>
    private void StartNodePass(Scene scene)
    {
        if (_outlineDrawnInFrame.Length < scene.Nodes.Length)
        {
            _outlineDrawnInFrame = new int[scene.Nodes.Length];
        }

        if (++_outlineFrame == int.MaxValue)
        {
            Array.Clear(_outlineDrawnInFrame);
            _outlineFrame = 1;
        }
    }

    /// <summary>Whether this is the pass's first item of its node, and marks the node done.</summary>
    private bool FirstOfNode(int node)
    {
        if ((uint)node >= (uint)_outlineDrawnInFrame.Length)
        {
            return true;
        }

        if (_outlineDrawnInFrame[node] == _outlineFrame)
        {
            return false;
        }

        _outlineDrawnInFrame[node] = _outlineFrame;
        return true;
    }

    private struct DrawState
    {
        public int VertexArray;
        public int Material;
        public float Opacity;
        public SceneMeshFlags MeshFlags;
        public SceneTopology Topology;
        public int SkinOffset;
        public bool Orthographic;
    }

    /// <summary>
    /// Puts the bone matrices of the frame in the bone texture: the four rows of a matrix side by side, 256
    /// matrices to a row of the texture, in the order of <see cref="DrawList.SkinMatrices"/>.
    /// </summary>
    private void UploadBones()
    {
        ReadOnlySpan<Matrix4x4> matrices = _drawList.SkinMatrices;
        if (matrices.IsEmpty)
        {
            return;
        }

        const int perRow = Shaders.BoneTextureWidth / 4;
        int rows = (matrices.Length + perRow - 1) / perRow;
        int floats = rows * Shaders.BoneTextureWidth * 4;
        if (_boneTexels.Length < floats)
        {
            _boneTexels = new float[floats];
        }

        // A matrix's sixteen floats are its four rows, in the order the shader reads them as texels.
        System.Runtime.InteropServices.MemoryMarshal.Cast<Matrix4x4, float>(matrices).CopyTo(_boneTexels);
        _boneTexels.AsSpan(matrices.Length * 16, floats - matrices.Length * 16).Clear();

        if (_boneTexture == 0)
        {
            _boneTexture = _gl.GenTexture();
        }

        _gl.ActiveTexture(GlApi.Texture0 + Shaders.BoneTextureUnit);
        _gl.BindTexture(GlApi.Texture2D, _boneTexture);
        _gl.TexParameter(GlApi.TextureMinFilter, GlApi.Nearest);
        _gl.TexParameter(GlApi.TextureMagFilter, GlApi.Nearest);
        _gl.TexParameter(GlApi.TextureWrapS, GlApi.ClampToEdge);
        _gl.TexParameter(GlApi.TextureWrapT, GlApi.ClampToEdge);
        _gl.TexImage2DFloat(Shaders.BoneTextureWidth, rows, _boneTexels.AsSpan(0, floats));
        _gl.ActiveTexture(GlApi.Texture0);
    }

    private void DrawOne(Scene scene, in DrawItem item, in Matrix4x4 view, ref DrawState state, bool countTriangles = true, Shape shape = Shape.Primitives)
    {
        GlApi gl = _gl;
        SceneTopology topology = scene.Meshes[item.Mesh].Topology;

        // Every edge of a mesh of line segments or points is the mesh itself.
        if (shape == Shape.Edges && topology != SceneTopology.Triangles)
        {
            shape = Shape.Primitives;
        }

        int vertexArray = shape == Shape.Edges ? _resources.WireArray(item.Mesh) : _resources.VertexArray(item.Mesh);
        int first = item.IndexStart, count = item.IndexCount;
        if (shape == Shape.Edges)
        {
            first = (uint)item.Part < (uint)_wireFirst.Length ? _wireFirst[item.Part] : 0;
            count = (uint)item.Part < (uint)_wireCount.Length ? _wireCount[item.Part] : 0;
        }

        if (vertexArray == 0 || count == 0)
        {
            return;
        }

        if (vertexArray != state.VertexArray)
        {
            gl.BindVertexArray(vertexArray);
            state.VertexArray = vertexArray;
        }

        SceneMeshFlags meshFlags = scene.Meshes[item.Mesh].Flags;
        SceneTopology drawn = shape switch { Shape.Edges => SceneTopology.Lines, Shape.Points => SceneTopology.Points, _ => topology };
        if (item.Material != state.Material || item.Opacity != state.Opacity || meshFlags != state.MeshFlags || drawn != state.Topology)
        {
            // Line segments and points have no surface for light to fall on: they are drawn in their colour,
            // unless the render mode lights them as the surfaces they come from (shaded wireframe and vertices).
            bool lit = drawn == SceneTopology.Triangles || (_override.LightMarks && topology == SceneTopology.Triangles);
            topology = drawn;
            SetMaterial(scene, item.Material, item.Opacity, meshFlags, state.Orthographic, lit: lit);
            state.Material = item.Material;
            state.Opacity = item.Opacity;
            state.MeshFlags = meshFlags;
            state.Topology = topology;
        }

        // A mesh whose bones the GPU has is skinned from this item's matrices; any other is drawn as it is.
        int skinOffset = item.SkinOffset >= 0 && _resources.IsSkinned(item.Mesh) ? item.SkinOffset : -1;
        if (skinOffset != state.SkinOffset)
        {
            gl.Uniform(_uSkinOffset, skinOffset);
            state.SkinOffset = skinOffset;
        }

        Matrix4x4 modelView = item.World * view;
        gl.UniformMatrix(_uModelView, modelView);

        NormalMatrix(modelView, _normalMatrix);
        gl.UniformMatrix3(_uNormalMatrix, _normalMatrix);
        int mode = drawn switch { SceneTopology.Lines => GlApi.Lines, SceneTopology.Points => GlApi.Points, _ => GlApi.Triangles };
        gl.DrawElements(mode, count, GlApi.UnsignedInt, (nint)first * 4);
        LastDrawCalls++;
        LastTriangles += countTriangles && drawn == SceneTopology.Triangles ? count / 3 : 0;
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
        Vector3 diffuse = _shading.Diffuse(material.Diffuse == Vector3.Zero ? material.Ambient : material.Diffuse);
        float opacity = Unit(material.Opacity, 1) * nodeOpacity * Unit(_override.OpacityFactor, 1);

        // A render mode that paints in one colour (edges, points, boxes, illustrated surfaces): unlit, untextured.
        if (_override.UseColor)
        {
            gl.Uniform(_uFlags, (orthographic ? Shaders.FlagOrthographic : 0) | (_override.ColorLit ? Shaders.FlagLit : 0));
            gl.Uniform(_uDiffuse, _override.Color);
            gl.Uniform(_uSpecular, Vector3.Zero);
            gl.Uniform(_uEmissive, Vector3.Zero);
            gl.Uniform(_uOpacity, opacity);
            gl.Uniform(_uShininess, 1f);
            gl.Uniform(_uAlphaCutoff, 0.5f);
            return;
        }

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
            flags |= (uint)materialIndex < (uint)_materialCutout.Length && _materialCutout[materialIndex] ? Shaders.FlagCutout : 0;
            gl.UniformMatrix(_uTexTransform, IsFinite(layer.Transform) ? layer.Transform * FlipV : FlipV);
        }

        gl.Uniform(_uFlags, flags);
        gl.Uniform(_uDiffuse, Sanitize(diffuse));
        gl.Uniform(_uSpecular, Sanitize(_shading.Specular(material.Specular)));
        gl.Uniform(_uEmissive, Sanitize(material.Emissive + diffuse * _override.AddedEmissive));
        gl.Uniform(_uOpacity, opacity);
        gl.Uniform(_uShininess, _shading.Exponent(material.Reflectivity));
        gl.Uniform(_uAlphaCutoff, Unit(material.AlphaReference, 0.5f));
    }

    private void SetLights(Scene scene, SceneLighting scheme, in Matrix4x4 view)
    {
        int count = LightRigs.Build(scheme, scene, view, _lights, out Vector3 ambient);
        for (int i = 0; i < count; i++)
        {
            ShaderLight light = _lights[i];
            light.Position.CopyTo(_lightPositions, i * 4);
            _shading.LightColor(light.Color).CopyTo(_lightColors, i * 3);
            light.Attenuation.CopyTo(_lightAttenuations, i * 3);
            light.SpotDirection.CopyTo(_lightSpots, i * 4);
            _lightSpots[i * 4 + 3] = light.SpotCosine;
        }

        _gl.Uniform(_uAmbientLight, _shading.Ambient(ambient));
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
        _outlines.Release();
        _targets.Release();
        _gl.DeleteTexture(_boneTexture);
        _boneTexture = 0;
        _gl.DeleteProgram(_program);
        _gl.DeleteProgram(_outlineProgram);
        _gl.DeleteVertexArray(_boxLines);
        _gl.DeleteVertexArray(_boxFaces);
        _gl.DeleteBuffer(_boxVertices);
        _gl.DeleteBuffer(_boxLineIndices);
        _gl.DeleteBuffer(_boxFaceIndices);
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
