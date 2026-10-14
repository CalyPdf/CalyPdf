using System.Numerics;
using Caly.Avalonia.Scene3D.Internal;
using Scene3D;

namespace Caly.Avalonia.Scene3D.Tests;

public class DrawListTests
{
    private const int Default = 0, Red = 1, Glass = 2;

    /// <summary>
    /// Materials: 0 default, 1 red, 2 glass (half transparent).
    /// Meshes: "One" (a unit cube, one part) and "Two" (two parts, shading ids 0 and 1).
    /// Nodes:
    ///   0 Group                      at x = 0
    ///   1   A      One  [red]        at x = 0
    ///   2   B      Two  [glass, red] at x = 10
    ///   3     C    One  []           at x = 10 + 5  (child of B)
    ///   4 Hidden   One  [red]        at x = -10, not visible in the scene
    ///   5 D        One  [glass]      at x = 30
    ///   6 E        Two  [red]        at x = 50: second part unbound
    /// </summary>
    private static Scene Build()
    {
        using var builder = new SceneBuilder();
        builder.AddMaterial(new SceneMaterial { Ambient = new Vector3(0.75f), Opacity = 1, Flags = SceneMaterialFlags.Lit, Name = builder.AddName("") });
        builder.AddMaterial(new SceneMaterial { Diffuse = new Vector3(1, 0, 0), Opacity = 1, Flags = SceneMaterialFlags.Lit, Name = builder.AddName("Red") });
        builder.AddMaterial(new SceneMaterial { Diffuse = new Vector3(0, 0, 1), Opacity = 0.5f, Flags = SceneMaterialFlags.Lit, Name = builder.AddName("Glass") });

        int one = TestScenes.AddBox(builder, new Vector3(-0.5f), new Vector3(0.5f), "One");

        builder.BeginMesh(builder.AddName("Two"), SceneMeshFlags.None);
        new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0 }.CopyTo(builder.AllocateVertices(4).Positions);
        new uint[] { 0, 1, 2, 1, 3, 2 }.CopyTo(builder.AllocateIndices(6));
        builder.AddPart(3, 0);
        builder.AddPart(3, 1);
        int two = builder.EndMesh();

        int[] redOnly = [builder.AddBinding(Red)];
        int glassRed = builder.AddBinding(Glass);
        builder.AddBinding(Red);
        int glassOnly = builder.AddBinding(Glass);

        SceneNode Model(string name, int parent, float x, int mesh, int firstBinding, int bindingCount, bool visible = true)
        {
            SceneNode node = TestScenes.Node(builder.AddName(name), parent, Matrix4x4.CreateTranslation(x, 0, 0), mesh, visible: visible);
            node.FirstBinding = firstBinding;
            node.BindingCount = bindingCount;
            return node;
        }

        int group = builder.AddNode(TestScenes.Node(builder.AddName("Group")));
        builder.AddNode(Model("A", group, 0, one, redOnly[0], 1));
        int b = builder.AddNode(Model("B", group, 10, two, glassRed, 2));
        builder.AddNode(Model("C", b, 5, one, 0, 0));
        builder.AddNode(Model("Hidden", -1, -10, one, redOnly[0], 1, visible: false));
        builder.AddNode(Model("D", -1, 30, one, glassOnly, 1));
        builder.AddNode(Model("E", -1, 50, two, redOnly[0], 1));
        return builder.Build();
    }

    /// <summary>A camera far back on −Y that sees everything between x = −20 and x = 60.</summary>
    private static (Matrix4x4 View, Matrix4x4 ViewProjection) Camera(Vector3? eye = null, Vector3? target = null)
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye ?? new Vector3(20, -200, 0), target ?? new Vector3(20, 0, 0), Vector3.UnitZ);
        var projection = new SceneViewProjection(SceneProjection.Perspective, 40, SceneViewBinding.Min, 1, null, null);
        Assert.True(ViewCamera.TryProjectionMatrix(projection, new Vector2(800, 600), ViewRegion.Full, 1, 1000, out Matrix4x4 p));
        return (view, view * p);
    }

    private static DrawList Prepared(Scene scene, SceneView? view = null, bool[]? alpha = null)
    {
        var list = new DrawList();
        list.Prepare(scene, view, alpha);
        (Matrix4x4 v, Matrix4x4 vp) = Camera();
        list.Update(v, vp);
        return list;
    }

    private static string[] Names(Scene scene, ReadOnlySpan<DrawItem> items)
    {
        var names = new string[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            names[i] = $"{scene.GetName(scene.Nodes[items[i].Node].Name)}:{items[i].Material}";
        }

        return names;
    }

    [Fact]
    public void EveryVisiblePartIsDrawnOnceWithItsMaterial()
    {
        using Scene scene = Build();
        DrawList list = Prepared(scene);

        // Opaque, by material then mesh: C and the unbound second part of E fall back to material 0.
        Assert.Equal(["C:0", "E:0", "A:1", "B:1", "E:1"], Names(scene, list.Opaque));

        // Transparent, farthest first; both are equally far from this camera, so only the set matters.
        Assert.Equal(["B:2", "D:2"], Names(scene, list.Transparent).Order().ToArray());
        Assert.Equal(7, list.Count);
        Assert.Equal(0, list.Culled);

        // Index ranges are the parts': E's first part, the mesh "Two", starts at the cube's 36 indices.
        DrawItem e = list.Opaque.ToArray().Single(d => scene.GetName(scene.Nodes[d.Node].Name) == "E" && d.Material == Red);
        Assert.Equal((36, 3, 1), (e.IndexStart, e.IndexCount, e.Mesh));
        Assert.Equal(new Vector3(50, 0, 0), e.World.Translation);

        // A child's world matrix carries its parent's.
        DrawItem c = list.Opaque.ToArray().Single(d => scene.GetName(scene.Nodes[d.Node].Name) == "C");
        Assert.Equal(new Vector3(15, 0, 0), c.World.Translation);
        Assert.Equal(1f, c.Opacity);
    }

    [Fact]
    public void TheVisibleBoundsCoverWhatIsDrawn()
    {
        using Scene scene = Build();
        DrawList list = Prepared(scene);

        // From A's cube at x = −0.5 to E's quad at x = 51; "Hidden" at x = −10 is not part of it.
        Assert.Equal(new Vector3(-0.5f, -0.5f, -0.5f), list.VisibleBounds.Min);
        Assert.Equal(new Vector3(51, 1, 0.5f), list.VisibleBounds.Max);
    }

    [Fact]
    public void AViewCanHideAndShowNodes()
    {
        using Scene scene = Build();

        // Hiding B hides B and leaves its child C; showing "Hidden" shows it.
        var view = new SceneView { NodeOverrides = [new SceneNodeOverride("B", false, null, null), new SceneNodeOverride("Hidden", true, null, null)] };
        DrawList list = Prepared(scene, view);

        string[] drawn = Names(scene, list.Opaque).Concat(Names(scene, list.Transparent)).ToArray();
        Assert.DoesNotContain(drawn, n => n.StartsWith("B:"));
        Assert.Contains("C:0", drawn);
        Assert.Contains("Hidden:1", drawn);
        Assert.Equal(-10.5f, list.VisibleBounds.Min.X);

        // What a view says of a node is said of that node alone. The views of real documents hide the
        // node of an assembly (it has nothing of its own to draw) and show, or do not mention, its parts.
        view = new SceneView { NodeOverrides = [new SceneNodeOverride("Group", false, null, null), new SceneNodeOverride("A", true, null, null)] };
        list = Prepared(scene, view);
        Assert.Equal(["A:1", "B:1", "B:2", "C:0", "D:2", "E:0", "E:1"], Names(scene, list.Opaque).Concat(Names(scene, list.Transparent)).Order().ToArray());

        // A name that matches nothing changes nothing.
        list = Prepared(scene, new SceneView { NodeOverrides = [new SceneNodeOverride("Nobody", false, 0.1f, Matrix4x4.CreateScale(9))] });
        Assert.Equal(7, list.Count);
    }

    [Fact]
    public void AViewCanFadeNodes()
    {
        using Scene scene = Build();
        var view = new SceneView { NodeOverrides = [new SceneNodeOverride("Group", null, 0.5f, null), new SceneNodeOverride("C", null, 0.5f, null), new SceneNodeOverride("E", null, 0f, null)] };
        DrawList list = Prepared(scene, view);

        // Everything under the group is now transparent; C has both factors; E, fully faded, is not drawn.
        Assert.Empty(list.Opaque.ToArray());
        DrawItem[] items = list.Transparent.ToArray();
        Assert.Equal(["A:1", "B:1", "B:2", "C:0", "D:2"], Names(scene, items).Order().ToArray());
        Assert.Equal(0.5f, items.First(d => scene.GetName(scene.Nodes[d.Node].Name) == "A").Opacity);
        Assert.Equal(0.25f, items.First(d => scene.GetName(scene.Nodes[d.Node].Name) == "C").Opacity);
        Assert.Equal(1f, items.First(d => scene.GetName(scene.Nodes[d.Node].Name) == "D").Opacity);
    }

    [Fact]
    public void AViewCanMoveNodes()
    {
        using Scene scene = Build();
        var broken = Matrix4x4.Identity;
        broken.M11 = float.NaN;
        var view = new SceneView { NodeOverrides = [new SceneNodeOverride("B", null, null, Matrix4x4.CreateTranslation(0, 7, 0)), new SceneNodeOverride("D", null, null, broken)] };
        DrawList list = Prepared(scene, view);

        DrawItem[] all = list.Opaque.ToArray().Concat(list.Transparent.ToArray()).ToArray();
        Vector3 Position(string name) => all.First(d => scene.GetName(scene.Nodes[d.Node].Name) == name).World.Translation;

        // The override replaces B's own transform; C follows its parent; a transform that is not finite is ignored.
        Assert.Equal(new Vector3(0, 7, 0), Position("B"));
        Assert.Equal(new Vector3(5, 7, 0), Position("C"));
        Assert.Equal(new Vector3(30, 0, 0), Position("D"));
    }

    [Fact]
    public void TransparentDrawsAreOrderedBackToFront()
    {
        using Scene scene = Build();
        var list = new DrawList();
        list.Prepare(scene, null);

        // Looking along +X from the left: D (x = 30) is behind B (x = 10).
        (Matrix4x4 view, Matrix4x4 viewProjection) = Camera(new Vector3(-100, 0.2f, 0.2f), new Vector3(0, 0.2f, 0.2f));
        list.Update(view, viewProjection);
        Assert.Equal(["D:2", "B:2"], Names(scene, list.Transparent));
        Assert.True(list.Transparent[0].Depth > list.Transparent[1].Depth);

        // And from the right, the other way round.
        (view, viewProjection) = Camera(new Vector3(200, 0.2f, 0.2f), new Vector3(0, 0.2f, 0.2f));
        list.Update(view, viewProjection);
        Assert.Equal(["B:2", "D:2"], Names(scene, list.Transparent));
    }

    [Fact]
    public void ATextureWithTransparencyMakesItsMaterialTransparent()
    {
        using Scene scene = Build();
        DrawList list = Prepared(scene, null, [false, true]);

        Assert.Equal(["C:0", "E:0"], Names(scene, list.Opaque));
        Assert.Equal(5, list.Transparent.Length);
    }

    [Fact]
    public void WhatIsOutsideTheViewIsNotDrawn()
    {
        using Scene scene = Build();
        var list = new DrawList();
        list.Prepare(scene, null);

        // Close in on A: everything else is off to the right.
        (Matrix4x4 view, Matrix4x4 viewProjection) = Camera(new Vector3(0, -4, 0), Vector3.Zero);
        list.Update(view, viewProjection);
        Assert.Equal(["A:1"], Names(scene, list.Opaque));
        Assert.Empty(list.Transparent.ToArray());
        Assert.Equal(6, list.Culled);

        // Looking the other way: nothing.
        (view, viewProjection) = Camera(new Vector3(20, -200, 0), new Vector3(20, -400, 0));
        list.Update(view, viewProjection);
        Assert.Equal(0, list.Opaque.Length + list.Transparent.Length);
        Assert.Equal(7, list.Culled);
    }

    [Fact]
    public void ASceneWithoutMaterialsOrGeometryIsHarmless()
    {
        using Scene empty = TestScenes.Empty();
        DrawList list = Prepared(empty);
        Assert.Equal(0, list.Count);
        Assert.True(list.VisibleBounds.IsEmpty);

        using var builder = new SceneBuilder();
        int mesh = TestScenes.AddBox(builder, Vector3.Zero, Vector3.One);
        SceneNode node = TestScenes.Node(builder.AddName("N"), mesh: mesh);
        node.FirstBinding = 0;
        node.BindingCount = 1;
        builder.AddBinding(42); // a binding to a material that does not exist
        builder.AddNode(node);
        using Scene bare = builder.Build();

        list = Prepared(bare);
        Assert.Equal(-1, Assert.Single(list.Opaque.ToArray()).Material);
    }

    [Fact]
    public void UpdatingForANewCameraAllocatesNothing()
    {
        using Scene scene = Build();
        var list = new DrawList();
        list.Prepare(scene, new SceneView { NodeOverrides = [new SceneNodeOverride("Group", null, 0.5f, null)] });
        (Matrix4x4 view, Matrix4x4 viewProjection) = Camera();
        list.Update(view, viewProjection);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            Matrix4x4 turned = Matrix4x4.CreateRotationZ(i * 0.01f) * view;
            list.Update(turned, turned * viewProjection);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    /// <summary>A group "Arm" at x = 1 with a box "Hand" under it at x = 2, and a box "Still" at x = 20.</summary>
    private static Scene BuildAnimated(Action<SceneBuilder>? more = null)
    {
        using var builder = new SceneBuilder();
        int box = TestScenes.AddBox(builder, new Vector3(-0.5f), new Vector3(0.5f), "Box");
        int arm = builder.AddNode(TestScenes.Node(builder.AddName("Arm"), -1, Matrix4x4.CreateTranslation(1, 0, 0)));
        builder.AddNode(TestScenes.Node(builder.AddName("Hand"), arm, Matrix4x4.CreateTranslation(2, 0, 0), box));
        builder.AddNode(TestScenes.Node(builder.AddName("Still"), -1, Matrix4x4.CreateTranslation(20, 0, 0), box));

        // Arm slides 10 along X in one second and stays there.
        builder.AddAnimation(new SceneAnimation("slide", builder.AddName("Arm"),
            [new SceneKeyFrame(0, Vector3.Zero, Quaternion.Identity, Vector3.One), new SceneKeyFrame(1, new Vector3(10, 0, 0), Quaternion.Identity, Vector3.One)], false, 1, 0));
        more?.Invoke(builder);
        return builder.Build();
    }

    private static Vector3 WorldOf(Scene scene, DrawList list, string node)
    {
        foreach (DrawItem item in list.Opaque)
        {
            if (scene.GetName(scene.Nodes[item.Node].Name) == node)
            {
                return item.World.Translation;
            }
        }

        throw new InvalidOperationException($"{node} is not drawn.");
    }

    [Fact]
    public void WithoutATimeAnimationsDoNotMoveAnything()
    {
        using Scene scene = BuildAnimated();
        var list = new DrawList();
        list.Prepare(scene, null);
        (Matrix4x4 v, Matrix4x4 vp) = Camera();
        list.Update(v, vp);

        Assert.Equal(new Vector3(3, 0, 0), WorldOf(scene, list, "Hand"));
    }

    [Fact]
    public void AnAnimatedNodeMovesItsDescendantsWithIt()
    {
        using Scene scene = BuildAnimated();
        var list = new DrawList();
        list.Prepare(scene, null, default, animationTime: 0.5);
        (Matrix4x4 v, Matrix4x4 vp) = Camera();
        list.Update(v, vp);

        // Half way along the slide: the arm is 5 on, the hand moves with it; the other box stays.
        Assert.Equal(new Vector3(8, 0, 0), WorldOf(scene, list, "Hand"));
        Assert.Equal(new Vector3(20, 0, 0), WorldOf(scene, list, "Still"));
    }

    [Fact]
    public void TheAnimationIsAppliedBeforeTheNodesOwnPlacement()
    {
        // The arm is turned a quarter about Z where it stands: its motion happens in its own space, so a
        // slide along X goes along the arm's turned X, which is world Y.
        using Scene scene = BuildAnimated();
        var view = new SceneView
        {
            NodeOverrides = [new SceneNodeOverride("Arm", null, null, Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(1, 0, 0))]
        };
        var list = new DrawList();
        list.Prepare(scene, view, default, animationTime: 1.0);
        (Matrix4x4 v, Matrix4x4 vp) = Camera();
        list.Update(v, vp);

        Vector3 hand = WorldOf(scene, list, "Hand");
        Assert.Equal(1f, hand.X, 3);
        Assert.Equal(12f, hand.Y, 3);
    }

    [Fact]
    public void TheVisibleBoundsFollowTheAnimation()
    {
        using Scene scene = BuildAnimated();
        var list = new DrawList();

        list.Prepare(scene, null, default, animationTime: 0);
        float before = list.VisibleBounds.Min.X;
        list.Prepare(scene, null, default, animationTime: 1);

        Assert.Equal(2.5f, before, 3);
        Assert.Equal(12.5f, list.VisibleBounds.Min.X, 3);
        Assert.Equal(20.5f, list.VisibleBounds.Max.X, 3);
    }

    [Fact]
    public void TwoAnimationsOfOneNodeCombine()
    {
        using Scene scene = BuildAnimated(b => b.AddAnimation(new SceneAnimation("up", b.AddName("Arm"),
            [new SceneKeyFrame(0, Vector3.Zero, Quaternion.Identity, Vector3.One), new SceneKeyFrame(1, new Vector3(0, 4, 0), Quaternion.Identity, Vector3.One)], false, 1, 0)));
        var list = new DrawList();
        list.Prepare(scene, null, default, animationTime: 1);
        (Matrix4x4 v, Matrix4x4 vp) = Camera();
        list.Update(v, vp);

        Assert.Equal(new Vector3(13, 4, 0), WorldOf(scene, list, "Hand"));
    }

    [Fact]
    public void ANonFiniteTimeIsTheStartOfTheAnimation()
    {
        using Scene scene = BuildAnimated();
        var list = new DrawList();
        list.Prepare(scene, null, default, animationTime: double.NaN);
        (Matrix4x4 v, Matrix4x4 vp) = Camera();
        list.Update(v, vp);

        Assert.Equal(new Vector3(3, 0, 0), WorldOf(scene, list, "Hand"));
    }

    // ---- skinned meshes ----

    /// <summary>
    /// A skeleton of two bones end to end along X (5 long each), and a triangle whose first vertex follows
    /// the first bone and the other two the second. Bone 0 turns a quarter about Z at t = 1 (from 0 to 1).
    /// </summary>
    private static Scene BuildSkinned(bool withMotion = true, int nodes = 1)
    {
        using var builder = new SceneBuilder();
        int skeleton = builder.AddSkeleton(new SceneSkeleton([new SceneBone(0, -1, 5, Vector3.Zero, Quaternion.Identity), new SceneBone(0, 0, 5, Vector3.Zero, Quaternion.Identity)]));

        builder.BeginMesh(builder.AddName("Tri"), SceneMeshFlags.None);
        new float[] { 0, 0, 0, 5, 0, 0, 5, 1, 0 }.CopyTo(builder.AllocateVertices(3).Positions);
        new uint[] { 0, 1, 2 }.CopyTo(builder.AllocateIndices(3));
        builder.AddPart(3, 0);
        SceneBoneMotion? motion = withMotion
            ? new SceneBoneMotion("turn", [[new SceneKeyFrame(0, Vector3.Zero, Quaternion.Identity, Vector3.One), new SceneKeyFrame(1, Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), Vector3.One)], null], false, 1, 0, false)
            : null;
        builder.SetMeshSkin(builder.AddSkin(new SceneSkin(skeleton, motion, [SceneSkin.PackIndices(0, 0, 0, 0), SceneSkin.PackIndices(1, 0, 0, 0), SceneSkin.PackIndices(1, 0, 0, 0)], [SceneSkin.PackWeights(1, 0, 0, 0), SceneSkin.PackWeights(1, 0, 0, 0), SceneSkin.PackWeights(1, 0, 0, 0)])));
        int mesh = builder.EndMesh();

        for (int i = 0; i < nodes; i++)
        {
            builder.AddNode(TestScenes.Node(builder.AddName("Node" + i), -1, Matrix4x4.CreateTranslation(i * 30, 0, 0), mesh));
        }

        return builder.Build();
    }

    private static DrawList PreparedAt(Scene scene, double? time)
    {
        var list = new DrawList();
        list.Prepare(scene, null, default, time);
        (Matrix4x4 v, Matrix4x4 vp) = Camera();
        list.Update(v, vp);
        return list;
    }

    [Fact]
    public void ASkinnedItemNamesItsMatricesAtTheTime()
    {
        using Scene scene = BuildSkinned();
        DrawList list = PreparedAt(scene, 1.0);

        DrawItem item = Assert.Single(list.Opaque.ToArray());
        Assert.True(item.SkinOffset >= 0);

        // The second bone starts at x = 5; with the first turned a quarter it starts at y = 5.
        Matrix4x4 second = list.SkinMatrices[item.SkinOffset + 1];
        Assert.True(Vector3.Distance(new Vector3(0, 5, 0), Vector3.Transform(new Vector3(5, 0, 0), second)) < 1e-3f);
    }

    [Fact]
    public void WithoutATimeASkinnedMeshStandsInItsReferencePose()
    {
        using Scene scene = BuildSkinned();
        DrawList list = PreparedAt(scene, null);

        DrawItem item = Assert.Single(list.Opaque.ToArray());
        Assert.True(item.SkinOffset >= 0);
        Assert.True(Vector3.Distance(new Vector3(5, 0, 0), Vector3.Transform(new Vector3(5, 0, 0), list.SkinMatrices[item.SkinOffset + 1])) < 1e-3f);
    }

    [Fact]
    public void ASkinWithoutAMotionIsDrawnAsAPlainMesh()
    {
        using Scene scene = BuildSkinned(withMotion: false);
        DrawList list = PreparedAt(scene, 1.0);

        DrawItem item = Assert.Single(list.Opaque.ToArray());
        Assert.Equal(-1, item.SkinOffset);
        Assert.True(list.SkinMatrices.IsEmpty);
    }

    [Fact]
    public void TheBoundsOfASkinnedMeshFollowItsBones()
    {
        using Scene scene = BuildSkinned();

        DrawList start = PreparedAt(scene, 0);
        DrawList turned = PreparedAt(scene, 1.0);

        // At rest the triangle spans x 0..5, y 0..1; with the second bone turned up it reaches y = 5.
        Assert.Equal(5f, start.VisibleBounds.Max.X, 2);
        Assert.True(turned.VisibleBounds.Max.Y >= 5f - 1e-3f, turned.VisibleBounds.Max.ToString());
    }

    [Fact]
    public void NodesOfOneSkinShareItsMatrices()
    {
        using Scene scene = BuildSkinned(nodes: 3);
        DrawList list = PreparedAt(scene, 1.0);

        DrawItem[] items = list.Opaque.ToArray();
        Assert.Equal(3, items.Length);
        Assert.Single(items.Select(i => i.SkinOffset).Distinct());
        Assert.Equal(2, list.SkinMatrices.Length);
    }

    [Fact]
    public void AnimatedPreparationOfASkinnedSceneKeepsTheDrawingAllocationFree()
    {
        using Scene scene = BuildSkinned();
        var list = new DrawList();
        list.Prepare(scene, null, default, 0.5);
        (Matrix4x4 v, Matrix4x4 vp) = Camera();
        list.Update(v, vp);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            list.Update(v, vp);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
