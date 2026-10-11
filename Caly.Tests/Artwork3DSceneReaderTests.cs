using Caly.Pdf.Artwork;
using Scene3D;
using Scene3D.Prc;
using UglyToad.PdfPig.Artwork;

namespace Caly.Tests;

public class Artwork3DSceneReaderTests
{
    internal static ReadOnlyMemory<byte> BoxPayload()
    {
        using Artwork3DTestDocument document = Artwork3DTestDocument.OpenBox();
        return Artwork3DContentReader.Read(document.Artworks[0]).Payload;
    }

    [Fact]
    public void AU3DPayloadBecomesASceneTheCallerOwns()
    {
        Artwork3DReadResult result = Artwork3DSceneReader.TryRead(Artwork3DFormat.U3D, BoxPayload(), out Scene? scene, out string? detail);

        Assert.Equal(Artwork3DReadResult.Ok, result);
        Assert.Null(detail);
        Assert.NotNull(scene);
        using (scene)
        {
            Assert.True(scene.HasGeometry);
            Assert.False(scene.Indices.IsEmpty);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4096)]
    public void BytesThatAreNotU3DAreMalformedAndNothingIsThrown(int length)
    {
        var garbage = new byte[length];
        new Random(7).NextBytes(garbage);

        Artwork3DReadResult result = Artwork3DSceneReader.TryRead(Artwork3DFormat.U3D, garbage, out Scene? scene, out string? detail);

        Assert.Equal(Artwork3DReadResult.Malformed, result);
        Assert.Null(scene);
        Assert.False(string.IsNullOrEmpty(detail));
    }

    [Fact]
    public void ACutPayloadGivesNoScene()
    {
        ReadOnlyMemory<byte> payload = BoxPayload();

        Artwork3DReadResult result = Artwork3DSceneReader.TryRead(Artwork3DFormat.U3D, payload[..(payload.Length / 3)], out Scene? scene, out _);

        Assert.Contains(result, new[] { Artwork3DReadResult.Malformed, Artwork3DReadResult.NoGeometry });
        Assert.Null(scene);
    }

    [Fact]
    public void AFormatWithoutAReaderIsNotSupported()
    {
        Artwork3DReadResult result = Artwork3DSceneReader.TryRead(Artwork3DFormat.Unknown, BoxPayload(), out Scene? scene, out string? detail);

        Assert.Equal(Artwork3DReadResult.UnsupportedFormat, result);
        Assert.Null(scene);
        Assert.Null(detail);
    }

    /// <summary>
    /// A small PRC file from <c>Documents</c>: written by the tests of Scene3D
    /// (<c>PrcFuzzTests.TheSeedFilesAreWrittenOutOnRequest</c>), which is where such files can be made.
    /// </summary>
    internal static byte[] PrcPayload(string name)
    {
        return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Documents", $"prc_{name}.prc"));
    }

    [Fact]
    public void APrcPayloadBecomesASceneTheCallerOwns()
    {
        Artwork3DReadResult result = Artwork3DSceneReader.TryRead(Artwork3DFormat.Prc, PrcPayload("simple"), out Scene? scene, out string? detail);

        Assert.Equal(Artwork3DReadResult.Ok, result);
        Assert.Null(detail);
        Assert.NotNull(scene);
        using (scene)
        {
            Assert.True(scene.HasGeometry);
            Assert.Equal(3, scene.Indices.Length);
            Assert.Equal(5, scene.Nodes.Length);
        }
    }

    /// <summary>A model is shown whole or not at all: with parts in the compressed form, it stays a poster.</summary>
    [Fact]
    public void APrcModelWithCompressedMeshesIsNotShownYet()
    {
        Artwork3DReadResult result = Artwork3DSceneReader.TryRead(Artwork3DFormat.Prc, PrcPayload("mixed"), out Scene? scene, out string? detail);

        Assert.Equal(Artwork3DReadResult.NotYetSupported, result);
        Assert.Null(scene);
        Assert.Null(detail);
    }

    [Fact]
    public void APrcModelOfExactGeometryOnlyHasNoDisplayMesh()
    {
        Artwork3DReadResult result = Artwork3DSceneReader.TryRead(Artwork3DFormat.Prc, PrcPayload("exact"), out Scene? scene, out string? detail);

        Assert.Equal(Artwork3DReadResult.NoDisplayMesh, result);
        Assert.Null(scene);
        Assert.Null(detail);
    }

    /// <summary>
    /// A view names the nodes of a PRC model by an identifier after a display name; where the display
    /// name differs from the scene's, the view is given the scene's.
    /// </summary>
    [Fact]
    public void AViewsNodesAreCalledAsThePrcSceneCallsThem()
    {
        Assert.Equal(Artwork3DReadResult.Ok, Artwork3DSceneReader.TryRead(Artwork3DFormat.Prc, PrcPayload("simple"), out Scene? scene, out _));
        using (scene)
        {
            // The node of the occurrence "left", called something else by whoever wrote the view.
            string left = Enumerable.Range(0, scene!.Nodes.Length).Select(i => scene.GetName(scene.Nodes[i].Name)).First(n => n.StartsWith("left.", StringComparison.Ordinal));
            string identifier = PrcNodeNames.IdentifierOf(left).ToString();
            var asWritten = new SceneView
            {
                Name = "Exploded",
                NodeOverrides = [new SceneNodeOverride("Left Plate." + identifier, false, null, null), new SceneNodeOverride("no such node", true, null, null)],
            };
            var plain = new SceneView { Name = "Front" };

            IReadOnlyList<SceneView> resolved = Artwork3DSceneReader.ResolveViews(Artwork3DFormat.Prc, scene, [asWritten, plain]);

            Assert.Equal(left, resolved[0].NodeOverrides[0].Name);
            Assert.False(resolved[0].NodeOverrides[0].Visible);
            Assert.Equal("no such node", resolved[0].NodeOverrides[1].Name);
            Assert.Equal("Exploded", resolved[0].Name);
            Assert.Same(plain, resolved[1]);
            Assert.True(scene.FindNode([resolved[0].NodeOverrides[0].Name]) >= 0);

            // Nothing to change: the views come back as they are; so they do for U3D, whatever they name.
            IReadOnlyList<SceneView> same = [plain];
            Assert.Same(same, Artwork3DSceneReader.ResolveViews(Artwork3DFormat.Prc, scene, same));
            IReadOnlyList<SceneView> other = [asWritten];
            Assert.Same(other, Artwork3DSceneReader.ResolveViews(Artwork3DFormat.U3D, scene, other));
        }
    }

    [Fact]
    public void BytesThatAreNotPrcAreMalformedAndNothingIsThrown()
    {
        Artwork3DReadResult result = Artwork3DSceneReader.TryRead(Artwork3DFormat.Prc, BoxPayload(), out Scene? scene, out string? detail);

        Assert.Equal(Artwork3DReadResult.Malformed, result);
        Assert.Null(scene);
        Assert.False(string.IsNullOrEmpty(detail));

        byte[] cut = PrcPayload("simple");
        result = Artwork3DSceneReader.TryRead(Artwork3DFormat.Prc, cut.AsMemory(0, cut.Length / 2), out scene, out detail);
        Assert.Equal(Artwork3DReadResult.Malformed, result);
        Assert.Null(scene);
    }

    [Fact]
    public void CancellationIsNotSwallowed()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            Artwork3DSceneReader.TryRead(Artwork3DFormat.U3D, BoxPayload(), out _, out _, cancelled.Token));
    }
}
