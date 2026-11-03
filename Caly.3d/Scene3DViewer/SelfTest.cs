using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Caly.Scene3D.Avalonia;
using Caly.Rendering.Scene3D;
using SkiaSharp;

namespace Scene3DViewer;

/// <summary>
/// The <c>--selftest</c> mode: the part of the control that <c>dotnet test</c> cannot reach, because it
/// needs a GPU. Small scenes made by hand are drawn, the framebuffer is read back, and pixels are compared
/// with what the scene must look like: colours, coverage, texture orientation, blending, the visible
/// region. Last, the control is taken out of the window and every GPU object must be gone.
/// </summary>
internal sealed class SelfTest
{
    private sealed class Frame(byte[] pixels, int width, int height)
    {
        public int Width => width;

        public int Height => height;

        /// <summary>The pixel at a position given in fractions of the frame, (0, 0) top-left.</summary>
        public (int R, int G, int B, int A) At(double x, double y)
        {
            int px = Math.Clamp((int)(x * width), 0, width - 1), py = Math.Clamp((int)(y * height), 0, height - 1);
            int at = (py * width + px) * 4;
            return (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]);
        }

        /// <summary>Fraction of the frame's pixels that are not the given colour.</summary>
        public double CoverageOver((int R, int G, int B) background)
        {
            long covered = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (Math.Abs(pixels[i] - background.R) + Math.Abs(pixels[i + 1] - background.G) + Math.Abs(pixels[i + 2] - background.B) > 12)
                {
                    covered++;
                }
            }

            return covered / (double)(width * height);
        }

        /// <summary>Number of pixels whose colour passes a test in a part of the frame given in fractions of it.</summary>
        public int CountWhereIn(double x0, double y0, double x1, double y1, Func<int, int, int, bool> test)
        {
            int count = 0;
            for (int y = (int)(y0 * height); y < Math.Min(height, (int)(y1 * height)); y++)
            {
                for (int x = (int)(x0 * width); x < Math.Min(width, (int)(x1 * width)); x++)
                {
                    int at = (y * width + x) * 4;
                    count += test(pixels[at], pixels[at + 1], pixels[at + 2]) ? 1 : 0;
                }
            }

            return count;
        }

        /// <summary>Number of the frame's pixels whose colour passes a test.</summary>
        public int CountWhere(Func<int, int, int, bool> test)
        {
            int count = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                count += test(pixels[i], pixels[i + 1], pixels[i + 2]) ? 1 : 0;
            }

            return count;
        }

        /// <summary>Number of the frame's pixels that are the given colour, to within a few levels.</summary>
        public int Count((int R, int G, int B) colour)
        {
            int count = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                count += Math.Abs(pixels[i] - colour.R) + Math.Abs(pixels[i + 1] - colour.G) + Math.Abs(pixels[i + 2] - colour.B) <= 12 ? 1 : 0;
            }

            return count;
        }
    }

    private sealed record Case(string Name, Func<Scene> Build, SceneView View, Rect Region, Action<Frame, Checks> Check);

    private sealed class Checks(string caseName)
    {
        public int Failed { get; private set; }

        public void Colour(string what, (int R, int G, int B, int A) actual, (int R, int G, int B) expected, int tolerance = 3)
        {
            bool ok = Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance && Math.Abs(actual.B - expected.B) <= tolerance;
            Report(ok, $"{what}: ({actual.R}, {actual.G}, {actual.B}), expected ({expected.R}, {expected.G}, {expected.B}) ± {tolerance}");
        }

        public void Near(string what, double actual, double expected, double tolerance)
        {
            Report(Math.Abs(actual - expected) <= tolerance, string.Create(CultureInfo.InvariantCulture, $"{what}: {actual:F4}, expected {expected:F4} ± {tolerance}"));
        }

        public void Report(bool ok, string message)
        {
            Failed += ok ? 0 : 1;
            Program.Log($"selftest {(ok ? "pass" : "FAIL")}  {caseName}: {message}");
        }
    }

    private static readonly Vector3 Background = new(0.2f, 0.4f, 0.6f);
    private static readonly (int, int, int) BackgroundBytes = (51, 102, 153);

    private readonly Scene3DControl _control;
    private readonly Decorator _host;
    private readonly Action<int> _finished;
    private readonly List<Case> _cases;
    private Scene? _scene;
    private int _index = -1, _failed;
    private bool _captureRequested;

    public SelfTest(Scene3DControl control, Decorator host, Action<int> finished)
    {
        _control = control;
        _host = host;
        _finished = finished;
        _control.FrameRendered += OnFrameRendered;
        _cases = BuildCases();
    }

    public void Start() => Next();

    private void Next()
    {
        _index++;
        _captureRequested = false;
        Scene? previous = _scene;
        if (_index >= _cases.Count)
        {
            Finish(previous);
            return;
        }

        Case current = _cases[_index];
        _scene = current.Build();
        _control.VisibleRegion = current.Region;
        _control.View = current.View;
        _control.Scene = _scene;
        previous?.Dispose();
    }

    private void OnFrameRendered(object? sender, EventArgs e)
    {
        if (_index < 0 || _index >= _cases.Count || _captureRequested || _control.Status != Scene3DStatus.Ready || _control.Statistics.TexturesPending > 0 || _control.IsFindingEdges)
        {
            return;
        }

        // Read back the frame after this one: the scene and its textures are certainly in it.
        _captureRequested = true;
        Case current = _cases[_index];
        _control.CaptureNextFrame = (pixels, width, height) =>
        {
            var checks = new Checks(current.Name);
            current.Check(new Frame(pixels, width, height), checks);
            _failed += checks.Failed;
            Dispatcher.UIThread.Post(Next);
        };
        _control.RequestNextFrameRendering();
    }

    private void Finish(Scene? lastScene)
    {
        // Out of the window: the control must give back every GPU object it made.
        int before = _control.Statistics.LiveGpuObjects;
        _host.Child = null;
        var checks = new Checks("release");
        checks.Report(before > 0, $"{before} GPU objects were alive while drawing");
        checks.Report(_control.LiveGpuObjectsAfterRelease == 0, $"{_control.LiveGpuObjectsAfterRelease} GPU objects alive after leaving the window");
        _failed += checks.Failed;
        lastScene?.Dispose();

        Program.Log(_failed == 0 ? "selftest: all checks passed" : $"selftest: {_failed} CHECKS FAILED");
        _finished(_failed == 0 ? 0 : 4);
    }

    // ---- the scenes ----

    /// <summary>A camera on the +Z axis looking at the origin, orthographic, the view's height showing 4 units.</summary>
    private static SceneView FrontView() => new()
    {
        Name = "front",
        CameraToWorld = Matrix4x4.CreateTranslation(0, 0, 10),
        OrbitDistance = 10,
        Projection = new SceneViewProjection(SceneProjection.Orthographic, 0, SceneViewBinding.Height, 0.25f, null, null),
        Background = Background,
        Lighting = SceneLighting.Headlamp
    };

    /// <summary><see cref="FrontView"/> drawing in a render mode with an edge colour.</summary>
    private static SceneView OutlineView(Vector3 edgeColor, SceneRenderMode mode = SceneRenderMode.SolidOutline, Vector3? faceColor = null) => new()
    {
        Name = "front, outlined",
        CameraToWorld = Matrix4x4.CreateTranslation(0, 0, 10),
        OrbitDistance = 10,
        Projection = new SceneViewProjection(SceneProjection.Orthographic, 0, SceneViewBinding.Height, 0.25f, null, null),
        Background = Background,
        Lighting = SceneLighting.Headlamp,
        RenderMode = mode,
        EdgeColor = edgeColor,
        FaceColor = faceColor
    };

    private static bool Green(int r, int g, int b) => g >= 96 && g > r + 48 && g > b + 48;

    /// <summary>Green pixels around the left side of the small square (x = −0.5) in "behind" scenes, away from both diagonals.</summary>
    private static int BehindLeftSide(Frame frame)
    {
        double x = 0.5 - 0.125 * frame.Height / frame.Width, dx = 0.01 * frame.Height / frame.Width;
        return frame.CountWhereIn(x - dx, 0.42, x + dx, 0.46, Green);
    }

    private static int AddMaterial(SceneBuilder builder, Vector3 colour, float opacity = 1, bool lit = false, int texture = -1, Vector3 emissive = default)
    {
        int firstLayer = 0, layers = 0;
        if (texture >= 0)
        {
            firstLayer = builder.AddTextureLayer(new SceneTextureLayer { Texture = texture, Intensity = 1, Transform = Matrix4x4.Identity, Repeat = 0 });
            layers = 1;
        }

        return builder.AddMaterial(new SceneMaterial
        {
            Diffuse = colour, Emissive = emissive, Opacity = opacity, Flags = lit ? SceneMaterialFlags.Lit : SceneMaterialFlags.None, FirstLayer = firstLayer, LayerCount = layers, Name = builder.AddName("material")
        });
    }

    /// <summary>A square in the plane z = <paramref name="z"/>, from −half to +half, facing +Z, with texture coordinates 0 – 1.</summary>
    private static void AddSquare(SceneBuilder builder, float half, float z, int material)
    {
        builder.BeginMesh(builder.AddName("square"), SceneMeshFlags.HasNormals | SceneMeshFlags.HasTexCoords);
        SceneVertexSpans v = builder.AllocateVertices(4);
        new[] { -half, -half, z, half, -half, z, half, half, z, -half, half, z }.CopyTo(v.Positions);
        new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 }.CopyTo(v.Normals);
        new float[] { 0, 0, 1, 0, 1, 1, 0, 1 }.CopyTo(v.TexCoords);
        new uint[] { 0, 1, 2, 0, 2, 3 }.CopyTo(builder.AllocateIndices(6));
        builder.AddPart(6, 0);
        int mesh = builder.EndMesh();

        int binding = builder.AddBinding(material);
        builder.AddNode(new SceneNode
        {
            Parent = -1, Local = Matrix4x4.Identity, Kind = SceneNodeKind.Model, Mesh = mesh, Light = -1, Camera = -1,
            FirstBinding = binding, BindingCount = 1, Name = builder.AddName("node"), Visible = true
        });
    }

    /// <summary>
    /// A square like <see cref="AddSquare"/> that follows one bone, which the motion's first key (at time 0)
    /// moves <paramref name="shift"/> along X: where it is drawn says whether the GPU skinned it.
    /// </summary>
    private static void AddSkinnedSquare(SceneBuilder builder, float half, float shift, int material)
    {
        int skeleton = builder.AddSkeleton(new SceneSkeleton([new SceneBone(builder.AddName("bone"), -1, 1, Vector3.Zero, Quaternion.Identity)]));
        var motion = new SceneBoneMotion("shift", [[new SceneKeyFrame(0, new Vector3(shift, 0, 0), Quaternion.Identity, Vector3.One), new SceneKeyFrame(1, new Vector3(shift, 0, 0), Quaternion.Identity, Vector3.One)]], true, 1, 0, false);

        builder.BeginMesh(builder.AddName("skinned"), SceneMeshFlags.HasNormals);
        SceneVertexSpans v = builder.AllocateVertices(4);
        new[] { -half, -half, 0, half, -half, 0, half, half, 0, -half, half, 0 }.CopyTo(v.Positions);
        new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 }.CopyTo(v.Normals);
        new uint[] { 0, 1, 2, 0, 2, 3 }.CopyTo(builder.AllocateIndices(6));
        builder.AddPart(6, 0);
        uint[] bones = [0, 0, 0, 0];
        uint[] weights = [255, 255, 255, 255];
        builder.SetMeshSkin(builder.AddSkin(new SceneSkin(skeleton, motion, bones, weights)));
        int mesh = builder.EndMesh();

        int binding = builder.AddBinding(material);
        builder.AddNode(new SceneNode
        {
            Parent = -1, Local = Matrix4x4.Identity, Kind = SceneNodeKind.Model, Mesh = mesh, Light = -1, Camera = -1,
            FirstBinding = binding, BindingCount = 1, Name = builder.AddName("node"), Visible = true
        });
    }

    /// <summary>Line segments or points through the given positions, one index each in order.</summary>
    private static void AddPrimitives(SceneBuilder builder, SceneTopology topology, float[] positions, int material)
    {
        builder.BeginMesh(builder.AddName(topology.ToString()), SceneMeshFlags.None, topology);
        positions.CopyTo(builder.AllocateVertices(positions.Length / 3).Positions);
        Span<uint> indices = builder.AllocateIndices(positions.Length / 3);
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = (uint)i;
        }

        builder.AddPart(indices.Length, 0);
        int mesh = builder.EndMesh();
        int binding = builder.AddBinding(material);
        builder.AddNode(new SceneNode
        {
            Parent = -1, Local = Matrix4x4.Identity, Kind = SceneNodeKind.Model, Mesh = mesh, Light = -1, Camera = -1,
            FirstBinding = binding, BindingCount = 1, Name = builder.AddName("node"), Visible = true
        });
    }

    private static Scene Build(Action<SceneBuilder> fill)
    {
        using var builder = new SceneBuilder();
        builder.AddMaterial(new SceneMaterial { Ambient = new Vector3(0.75f), Opacity = 1, Flags = SceneMaterialFlags.Lit, Name = builder.AddName("") });
        fill(builder);
        return builder.Build();
    }

    /// <summary>An image whose quarters are red (top left), green (top right), blue (bottom left) and white (bottom right).</summary>
    private static SceneTexture Quarters()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Opaque));
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                bitmap.SetPixel(x, y, y < 32 ? (x < 32 ? SKColors.Red : new SKColor(0, 255, 0)) : (x < 32 ? SKColors.Blue : SKColors.White));
            }
        }

        using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new SceneTexture("quarters", 64, 64, SceneTextureChannels.Rgb, [new SceneTextureImage(SceneImageEncoding.Png, SceneTextureChannels.Rgb, png.ToArray())]);
    }

    /// <summary>
    /// An image with a transparent block in a corner (so that it has an alpha channel) and nothing in between: the left
    /// half red, the right half blue, every alpha 0 or 255, as the packed logo texture of the Shoe sample.
    /// </summary>
    private static SceneTexture RedBlueWithAHole()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                bitmap.SetPixel(x, y, y < 8 && x < 8 ? SKColors.Transparent : (x < 32 ? SKColors.Red : SKColors.Blue));
            }
        }

        using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new SceneTexture("red blue hole", 64, 64, SceneTextureChannels.Rgb | SceneTextureChannels.Alpha, [new SceneTextureImage(SceneImageEncoding.Png, SceneTextureChannels.Rgb | SceneTextureChannels.Alpha, png.ToArray())]);
    }

    /// <summary>
    /// One mesh of two squares, the one in front (red) listed before the one behind it (blue): drawn in index order
    /// without depth, the blue one would cover the red.
    /// </summary>
    private static void AddFrontThenBack(SceneBuilder builder, int material, bool backFacesAway = false)
    {
        builder.BeginMesh(builder.AddName("two squares"), SceneMeshFlags.HasNormals | SceneMeshFlags.HasTexCoords);
        SceneVertexSpans v = builder.AllocateVertices(8);
        new float[] { -1, -1, 1, 1, -1, 1, 1, 1, 1, -1, 1, 1, -1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0 }.CopyTo(v.Positions);
        for (int i = 0; i < 8; i++)
        {
            v.Normals[i * 3 + 2] = 1;
        }

        // The front square samples the red half of the image, the one behind the blue half.
        new float[] { 0.1f, 0.1f, 0.4f, 0.1f, 0.4f, 0.9f, 0.1f, 0.9f, 0.6f, 0.1f, 0.9f, 0.1f, 0.9f, 0.9f, 0.6f, 0.9f }.CopyTo(v.TexCoords);
        // The square behind turns its back on the viewer when it is the far wall of a closed shell.
        (backFacesAway ? new uint[] { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6 } : new uint[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 }).CopyTo(builder.AllocateIndices(12));
        builder.AddPart(12, 0);
        int mesh = builder.EndMesh();

        int binding = builder.AddBinding(material);
        builder.AddNode(new SceneNode
        {
            Parent = -1, Local = Matrix4x4.Identity, Kind = SceneNodeKind.Model, Mesh = mesh, Light = -1, Camera = -1,
            FirstBinding = binding, BindingCount = 1, Name = builder.AddName("node"), Visible = true
        });
    }

    private static List<Case> BuildCases()
    {
        var whole = new Rect(0, 0, 1, 1);
        return
        [
            // A red square 2 units across in a view 4 units high: a quarter of the height squared, of the area.
            new Case("unlit square",
                () => Build(b => AddSquare(b, 1, 0, AddMaterial(b, new Vector3(1, 0, 0)))),
                FrontView(), whole,
                (frame, checks) =>
                {
                    checks.Colour("centre", frame.At(0.5, 0.5), (255, 0, 0));
                    checks.Colour("corner is background", frame.At(0.03, 0.03), BackgroundBytes);
                    double expected = (frame.Height / 2.0) * (frame.Height / 2.0) / ((double)frame.Width * frame.Height);
                    checks.Near("covered fraction of the frame", frame.CoverageOver(BackgroundBytes), expected, 0.004);
                    checks.Colour("just inside the square's left edge", frame.At(0.5 - 0.24 * frame.Height / frame.Width, 0.5), (255, 0, 0));
                    checks.Colour("just outside the square's left edge", frame.At(0.5 - 0.26 * frame.Height / frame.Width, 0.5), BackgroundBytes);
                }),

            // Texture coordinates run from the bottom left of the image: (0, 0) is its bottom-left corner.
            new Case("textured square",
                () => Build(b => AddSquare(b, 1, 0, AddMaterial(b, Vector3.One, texture: b.AddTexture(Quarters())))),
                FrontView(), whole,
                (frame, checks) =>
                {
                    double dx = 0.125 * frame.Height / frame.Width;
                    checks.Colour("top left of the square", frame.At(0.5 - dx, 0.375), (255, 0, 0));
                    checks.Colour("top right", frame.At(0.5 + dx, 0.375), (0, 255, 0));
                    checks.Colour("bottom left", frame.At(0.5 - dx, 0.625), (0, 0, 255));
                    checks.Colour("bottom right", frame.At(0.5 + dx, 0.625), (255, 255, 255));
                }),

            // A texture modulates the whole lit colour, emissive included: a surface that glows white under a
            // picture shows the picture (as the Shoe of the sample documents does), not white added to it.
            new Case("emissive under a texture",
                () => Build(b => AddSquare(b, 1, 0, AddMaterial(b, Vector3.One, lit: true, texture: b.AddTexture(Quarters()), emissive: Vector3.One))),
                FrontView(), whole,
                (frame, checks) =>
                {
                    double dx = 0.125 * frame.Height / frame.Width;
                    checks.Colour("top left of the square", frame.At(0.5 - dx, 0.375), (255, 0, 0));
                    checks.Colour("top right", frame.At(0.5 + dx, 0.375), (0, 255, 0));
                    checks.Colour("bottom left", frame.At(0.5 - dx, 0.625), (0, 0, 255));
                }),

            // A texture whose alpha is only ever 0 or 255 is a cut-out, drawn with depth like any opaque surface: of
            // two squares in one mesh, the one in front wins whatever the order of their triangles (it was drawn
            // as a blended surface without depth, and the one behind covered it).
            new Case("cut-out texture keeps its depth",
                () => Build(b => AddFrontThenBack(b, AddMaterial(b, Vector3.One, texture: b.AddTexture(RedBlueWithAHole())))),
                FrontView(), whole,
                (frame, checks) => checks.Colour("the square in front", frame.At(0.5, 0.5), (255, 0, 0))),

            // The two walls of a closed translucent shell, the near one listed first. Back faces are blended before
            // front faces, whatever the index order: half red (near) over half blue (far) over the background.
            new Case("translucent shell, far wall first",
                () => Build(b => AddFrontThenBack(b, AddMaterial(b, Vector3.One, opacity: 0.5f, texture: b.AddTexture(RedBlueWithAHole())), backFacesAway: true)),
                FrontView(), whole,
                (frame, checks) => checks.Colour("near wall over far wall", frame.At(0.5, 0.5), (140, 25, 102), 4)),

            // Half-transparent blue in front of opaque red: half of each.
            new Case("transparency",
                () => Build(b =>
                {
                    AddSquare(b, 0.5f, 1, AddMaterial(b, new Vector3(0, 0, 1), opacity: 0.5f));
                    AddSquare(b, 1, 0, AddMaterial(b, new Vector3(1, 0, 0)));
                }),
                FrontView(), whole,
                (frame, checks) =>
                {
                    checks.Colour("blue over red", frame.At(0.5, 0.5), (128, 0, 128));
                    checks.Colour("red beside it", frame.At(0.5, 0.3), (255, 0, 0));
                }),

            // A lit grey surface facing the headlamp: ambient 0.1 plus the light's (0.8, 0.8, 0.9), times 0.5.
            new Case("lit square",
                () => Build(b => AddSquare(b, 1, 0, AddMaterial(b, new Vector3(0.5f), lit: true))),
                FrontView(), whole,
                (frame, checks) => checks.Colour("centre", frame.At(0.5, 0.5), (115, 115, 128))),

            // The middle half of the view: the square that covered half the height now fills the frame.
            new Case("visible region",
                () => Build(b => AddSquare(b, 1, 0, AddMaterial(b, Vector3.One, texture: b.AddTexture(Quarters())))),
                FrontView(), new Rect(0.25, 0.25, 0.5, 0.5),
                (frame, checks) =>
                {
                    double inset = 0.5 * (1 - (double)frame.Height / frame.Width);
                    checks.Colour("top left of the frame's square", frame.At(inset + 0.05, 0.05), (255, 0, 0));
                    checks.Colour("top right", frame.At(1 - inset - 0.05, 0.05), (0, 255, 0));
                    checks.Colour("bottom left", frame.At(inset + 0.05, 0.95), (0, 0, 255));
                    checks.Colour("bottom right", frame.At(1 - inset - 0.05, 0.95), (255, 255, 255));
                }),

            // A red square 2 units across that its bone moves 1 unit right: it covers the right of the centre only,
            // as much of the frame as the square that stands still.
            new Case("skinned square",
                () => Build(b => AddSkinnedSquare(b, 1, 1, AddMaterial(b, new Vector3(1, 0, 0)))),
                FrontView(), whole,
                (frame, checks) =>
                {
                    checks.Colour("middle of the moved square", frame.At(0.5 + 0.25 * frame.Height / frame.Width, 0.5), (255, 0, 0));
                    checks.Colour("where the square stands unmoved", frame.At(0.5 - 0.25 * frame.Height / frame.Width, 0.5), BackgroundBytes);
                    double expected = (frame.Height / 2.0) * (frame.Height / 2.0) / ((double)frame.Width * frame.Height);
                    checks.Near("covered fraction of the frame", frame.CoverageOver(BackgroundBytes), expected, 0.004);
                }),

            // A green line 2 units long in a view 4 units high: half the height in pixels, one pixel wide,
            // and green though its material asks for light (a line has no surface to light).
            new Case("line segment",
                () => Build(b => AddPrimitives(b, SceneTopology.Lines, [-1, 0.013f, 0, 1, 0.013f, 0], AddMaterial(b, new Vector3(0, 1, 0), lit: true))),
                FrontView(), whole,
                (frame, checks) =>
                {
                    int green = frame.Count((0, 255, 0));
                    checks.Near("green pixels along the line", green, frame.Height / 2.0, frame.Height * 0.06);
                    double touched = frame.CoverageOver(BackgroundBytes) * frame.Width * frame.Height;
                    checks.Report(touched >= green && touched <= green * 4.0, string.Create(CultureInfo.InvariantCulture, $"a thin line, its edges smoothed: {touched:F0} pixels touched for {green} of the line itself"));
                }),

            // Three white points, each a few pixels across; the one at the origin covers the centre.
            new Case("points",
                () => Build(b => AddPrimitives(b, SceneTopology.Points, [0, 0, 0, -1, 1, 0, 1, -1, 0], AddMaterial(b, Vector3.One))),
                FrontView(), whole,
                (frame, checks) =>
                {
                    checks.Colour("the point at the centre", frame.At(0.5, 0.5), (255, 255, 255));
                    double size = Math.Max(3.0, frame.Height / 270.0);
                    checks.Near("white pixels of three points", frame.Count((255, 255, 255)), 3 * size * size, 3 * size * size * 0.7);
                    checks.Colour("between the points is background", frame.At(0.5, 0.3), BackgroundBytes);
                }),

            // A red square 2 units across in a view that draws silhouette edges in green: its four sides, the
            // rim of an open sheet, are edges (about twice the frame's height in pixels); the diagonal between
            // its two triangles, which lie in one plane, is not, and the centre it crosses stays red.
            new Case("outline",
                () => Build(b => AddSquare(b, 1, 0, AddMaterial(b, new Vector3(1, 0, 0)))),
                OutlineView(new Vector3(0, 1, 0)), whole,
                (frame, checks) =>
                {
                    int greenish = frame.CountWhere((r, g, b) => g >= 96 && g > r + 48 && g > b + 48);
                    checks.Near("greenish pixels along the four sides", greenish, frame.Height * 2.0, frame.Height * 1.0);
                    checks.Colour("the centre, on the diagonal", frame.At(0.5, 0.5), (255, 0, 0));
                    checks.Colour("inside the square, off its sides", frame.At(0.5, 0.4), (255, 0, 0));
                }),

            // Wireframe: the square's four sides and its diagonal in green, nothing of its surface.
            new Case("wireframe",
                () => Build(b => AddSquare(b, 1, 0, AddMaterial(b, new Vector3(1, 0, 0)))),
                OutlineView(new Vector3(0, 1, 0), SceneRenderMode.Wireframe), whole,
                (frame, checks) =>
                {
                    double dx = 0.02 * frame.Height / frame.Width;
                    checks.Report(frame.CountWhereIn(0.5 - dx, 0.48, 0.5 + dx, 0.52, Green) > 0, "the diagonal crosses the centre");
                    checks.Colour("inside the square, off its edges", frame.At(0.5 - 0.1 * frame.Height / frame.Width, 0.4), BackgroundBytes);
                    checks.Report(frame.CountWhere((r, _, _) => r > 128) == 0, "no red: the surface is not drawn");
                }),

            // A small square behind a big one: every edge in a wireframe, the small one's hidden in a hidden wireframe.
            new Case("wireframe shows what is behind",
                () => Build(b => { AddSquare(b, 1, 1, AddMaterial(b, new Vector3(1, 0, 0))); AddSquare(b, 0.5f, 0, AddMaterial(b, new Vector3(1, 0, 0))); }),
                OutlineView(new Vector3(0, 1, 0), SceneRenderMode.Wireframe), whole,
                (frame, checks) => checks.Report(BehindLeftSide(frame) > 0, "the small square's left side shows")),
            new Case("hidden wireframe hides what is behind",
                () => Build(b => { AddSquare(b, 1, 1, AddMaterial(b, new Vector3(1, 0, 0))); AddSquare(b, 0.5f, 0, AddMaterial(b, new Vector3(1, 0, 0))); }),
                OutlineView(new Vector3(0, 1, 0), SceneRenderMode.HiddenWireframe), whole,
                (frame, checks) =>
                {
                    checks.Report(BehindLeftSide(frame) == 0, "the small square's left side is hidden");
                    checks.Colour("the big square's surface writes no colour", frame.At(0.5 - 0.1 * frame.Height / frame.Width, 0.4), BackgroundBytes);
                }),

            // Vertices: the square's four corners, a few pixels each, and nothing between them.
            new Case("vertices",
                () => Build(b => AddSquare(b, 0.9f, 0, AddMaterial(b, new Vector3(1, 0, 0)))),
                OutlineView(new Vector3(0, 1, 0), SceneRenderMode.Vertices), whole,
                (frame, checks) =>
                {
                    double size = Math.Max(2.0, frame.Height / 540.0);
                    checks.Near("green pixels of four points", frame.CountWhere(Green), 4 * size * size, 4 * size * size * 0.8);
                    checks.Colour("the centre", frame.At(0.5, 0.5), BackgroundBytes);
                }),

            // The box of a flat square is the square: its rim in green, its inside empty.
            new Case("bounding box",
                () => Build(b => AddSquare(b, 1, 0, AddMaterial(b, new Vector3(1, 0, 0)))),
                OutlineView(new Vector3(0, 1, 0), SceneRenderMode.BoundingBox), whole,
                (frame, checks) =>
                {
                    checks.Near("green pixels along the four sides", frame.CountWhere(Green), frame.Height * 2.0, frame.Height * 1.0);
                    checks.Colour("the centre", frame.At(0.5, 0.5), BackgroundBytes);
                }),

            // Transparent: the opaque red square at the added opacity of 0.5, over the background.
            new Case("transparent",
                () => Build(b => AddSquare(b, 1, 0, AddMaterial(b, new Vector3(1, 0, 0)))),
                OutlineView(new Vector3(0, 1, 0), SceneRenderMode.Transparent), whole,
                (frame, checks) => checks.Colour("red half over the background", frame.At(0.5, 0.4), ((255 + BackgroundBytes.Item1) / 2, BackgroundBytes.Item2 / 2, BackgroundBytes.Item3 / 2), 3)),

            // Illustration: the surface all in the face colour, unlit, its rim in the edge colour.
            new Case("illustration",
                () => Build(b => AddSquare(b, 1, 0, AddMaterial(b, new Vector3(1, 0, 0), lit: true))),
                OutlineView(new Vector3(0, 1, 0), SceneRenderMode.Illustration, new Vector3(1, 1, 0)), whole,
                (frame, checks) =>
                {
                    checks.Colour("the face colour", frame.At(0.5, 0.4), (255, 255, 0));
                    // Over yellow, a pixel the edge half covers is still greenish: only the edge's own pixels are counted.
                    checks.Near("green pixels along the four sides", frame.CountWhere((r, g, _) => r < 64 && g > 192), frame.Height * 2.0, frame.Height * 1.0);
                }),

            // The same square and edge colour in a solid view: no edges.
            new Case("no outline when solid",
                () => Build(b => AddSquare(b, 1, 0, AddMaterial(b, new Vector3(1, 0, 0)))),
                OutlineView(new Vector3(0, 1, 0), SceneRenderMode.Solid), whole,
                (frame, checks) => checks.Report(frame.CountWhere((r, g, b) => g >= 96 && g > r + 48 && g > b + 48) == 0, "no green pixel"))
        ];
    }
}
