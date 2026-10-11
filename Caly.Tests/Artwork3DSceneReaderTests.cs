using Caly.Pdf.Artwork;
using Scene3D;
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

    [Theory]
    [InlineData(Artwork3DFormat.Prc)]
    [InlineData(Artwork3DFormat.Unknown)]
    public void OtherFormatsAreNotSupported(Artwork3DFormat format)
    {
        Artwork3DReadResult result = Artwork3DSceneReader.TryRead(format, BoxPayload(), out Scene? scene, out string? detail);

        Assert.Equal(Artwork3DReadResult.UnsupportedFormat, result);
        Assert.Null(scene);
        Assert.Null(detail);
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
