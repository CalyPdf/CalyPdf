using System.Numerics;
using Caly.Pdf.Artwork;
using Scene3D;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Artwork;

namespace Caly.Tests;

public class Artwork3DViewConverterTests
{
    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < 1e-5f, $"expected {expected}, got {actual}");
    }

    /// <summary>
    /// A PDF camera looks along its +Z axis with +Y up (and, the system being right-handed, +X to the
    /// viewer's left); a scene camera looks along −Z with +Y up and +X to the right. Same place, same
    /// view direction, same up.
    /// </summary>
    [Fact]
    public void ACameraMatrixKeepsPositionViewDirectionAndUp()
    {
        // At (0, -10, 0), looking along world +Y, world +Z up. PDF X = Y × Z of the camera = up × forward.
        var forward = new Vector3(0, 1, 0);
        var up = new Vector3(0, 0, 1);
        Vector3 pdfX = Vector3.Cross(up, forward);
        var c2w = new Artwork3DMatrix(pdfX.X, pdfX.Y, pdfX.Z, up.X, up.Y, up.Z, forward.X, forward.Y, forward.Z, 0, -10, 0);

        Matrix4x4 camera = Artwork3DViewConverter.ToCameraToWorld(c2w);

        Assert.Equal(new Vector3(0, -10, 0), camera.Translation);
        AssertClose(forward, Vector3.TransformNormal(-Vector3.UnitZ, camera));
        AssertClose(up, Vector3.TransformNormal(Vector3.UnitY, camera));
        AssertClose(Vector3.Cross(forward, up), Vector3.TransformNormal(Vector3.UnitX, camera));
        Assert.Equal(1f, camera.GetDeterminant(), 1e-5f);
        Assert.Equal((0f, 0f, 0f, 1f), (camera.M14, camera.M24, camera.M34, camera.M44));
    }

    [Fact]
    public void ANodeMatrixIsTakenRowByRow()
    {
        Matrix4x4 m = Artwork3DViewConverter.ToTransform(new Artwork3DMatrix(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12));

        Assert.Equal(new Matrix4x4(1, 2, 3, 0, 4, 5, 6, 0, 7, 8, 9, 0, 10, 11, 12, 1), m);

        // The convention of 3D matrices in PDF, as of Matrix4x4: a point is a row, the translation the last row.
        Assert.Equal(new Vector3(10, 11, 12), Vector3.Transform(Vector3.Zero, m));
    }

    [Theory]
    [InlineData(Artwork3DScaling.Width, SceneViewBinding.Width)]
    [InlineData(Artwork3DScaling.Height, SceneViewBinding.Height)]
    [InlineData(Artwork3DScaling.Min, SceneViewBinding.Min)]
    [InlineData(Artwork3DScaling.Max, SceneViewBinding.Max)]
    public void APerspectiveFieldOfViewSpansTheExtentItsScalingNames(Artwork3DScaling scaling, SceneViewBinding expected)
    {
        SceneViewProjection p = Artwork3DViewConverter.ToProjection(
            Artwork3DProjectionType.Perspective, 30, scaling, null, 1, Artwork3DScaling.Absolute, Artwork3DClippingStyle.AutomaticNearFar, 5, 50, 400, 100);

        Assert.Equal(SceneProjection.Perspective, p.Kind);
        Assert.Equal(30f, p.FieldOfView);
        Assert.Equal(expected, p.Binding);

        // Automatic clipping: the numbers in the dictionary mean nothing.
        Assert.Null(p.Near);
        Assert.Null(p.Far);
    }

    /// <summary>
    /// A numeric PS is the diameter, in the annotation's units, that the field-of-view cone covers: on a
    /// view 100 high, a 60° cone covering 200 is a narrower cone covering the height.
    /// </summary>
    [Fact]
    public void AnExplicitPerspectiveDiameterBecomesAFieldOfViewOverTheHeight()
    {
        SceneViewProjection p = Artwork3DViewConverter.ToProjection(
            Artwork3DProjectionType.Perspective, 60, Artwork3DScaling.Value, 200, 1, Artwork3DScaling.Absolute, Artwork3DClippingStyle.AutomaticNearFar, null, null, 400, 100);

        Assert.Equal(SceneViewBinding.Height, p.Binding);
        double expected = 2 * Math.Atan(Math.Tan(Math.PI / 6) * 100 / 200) * 180 / Math.PI;
        Assert.Equal(expected, p.FieldOfView, 1e-3);

        // Without the size of the view there is nothing to measure the diameter against.
        SceneViewProjection unknown = Artwork3DViewConverter.ToProjection(
            Artwork3DProjectionType.Perspective, 60, Artwork3DScaling.Value, 200, 1, Artwork3DScaling.Absolute, Artwork3DClippingStyle.AutomaticNearFar, null, null, 0, 0);
        Assert.Equal((60f, SceneViewBinding.Width), (unknown.FieldOfView, unknown.Binding));
    }

    [Fact]
    public void AnOrthographicScaleIsBoundToAnExtentOfTheView()
    {
        SceneViewProjection width = Artwork3DViewConverter.ToProjection(
            Artwork3DProjectionType.Orthographic, null, Artwork3DScaling.Width, null, 2, Artwork3DScaling.Width, Artwork3DClippingStyle.AutomaticNearFar, null, null, 400, 100);
        Assert.Equal((SceneProjection.Orthographic, SceneViewBinding.Width, 2f), (width.Kind, width.Binding, width.OrthographicScale));

        // Absolute: 2 annotation units per scene unit, so a view 100 high shows 50 scene units: the scale
        // that makes the height show 50 is 1/50.
        SceneViewProjection absolute = Artwork3DViewConverter.ToProjection(
            Artwork3DProjectionType.Orthographic, null, Artwork3DScaling.Width, null, 2, Artwork3DScaling.Absolute, Artwork3DClippingStyle.AutomaticNearFar, null, null, 400, 100);
        Assert.Equal(SceneViewBinding.Height, absolute.Binding);
        Assert.Equal(0.02f, absolute.OrthographicScale, 1e-7f);

        SceneViewProjection unknown = Artwork3DViewConverter.ToProjection(
            Artwork3DProjectionType.Orthographic, null, Artwork3DScaling.Width, null, 2, Artwork3DScaling.Absolute, Artwork3DClippingStyle.AutomaticNearFar, null, null, 0, 0);
        Assert.Equal((SceneViewBinding.Min, 2f), (unknown.Binding, unknown.OrthographicScale));
    }

    [Fact]
    public void ExplicitClipPlanesAreKept()
    {
        SceneViewProjection p = Artwork3DViewConverter.ToProjection(
            Artwork3DProjectionType.Perspective, 30, Artwork3DScaling.Width, null, 1, Artwork3DScaling.Absolute, Artwork3DClippingStyle.ExplicitNearFar, 0.5, 80, 400, 100);
        Assert.Equal((0.5f, 80f), (p.Near, p.Far));

        SceneViewProjection noFar = Artwork3DViewConverter.ToProjection(
            Artwork3DProjectionType.Perspective, 30, Artwork3DScaling.Width, null, 1, Artwork3DScaling.Absolute, Artwork3DClippingStyle.ExplicitNearFar, 0.5, null, 400, 100);
        Assert.Equal(0.5f, noFar.Near);
        Assert.Null(noFar.Far);
    }

    [Theory]
    [InlineData(Artwork3DLightingStyle.Unknown, SceneLighting.Artwork)]
    [InlineData(Artwork3DLightingStyle.Artwork, SceneLighting.Artwork)]
    [InlineData(Artwork3DLightingStyle.None, SceneLighting.None)]
    [InlineData(Artwork3DLightingStyle.White, SceneLighting.White)]
    [InlineData(Artwork3DLightingStyle.Day, SceneLighting.Day)]
    [InlineData(Artwork3DLightingStyle.Night, SceneLighting.Night)]
    [InlineData(Artwork3DLightingStyle.Hard, SceneLighting.Hard)]
    [InlineData(Artwork3DLightingStyle.Primary, SceneLighting.Primary)]
    [InlineData(Artwork3DLightingStyle.Blue, SceneLighting.Blue)]
    [InlineData(Artwork3DLightingStyle.Red, SceneLighting.Red)]
    [InlineData(Artwork3DLightingStyle.Cube, SceneLighting.Cube)]
    [InlineData(Artwork3DLightingStyle.CAD, SceneLighting.Cad)]
    [InlineData(Artwork3DLightingStyle.Headlamp, SceneLighting.Headlamp)]
    public void EveryLightingStyleHasItsScheme(Artwork3DLightingStyle style, SceneLighting expected)
    {
        Assert.Equal(expected, Artwork3DViewConverter.ToLighting(style));
    }

    /// <summary>The whole conversion, on the view of a real document.</summary>
    [Fact]
    public void AViewOfARealDocumentConverts()
    {
        // box_base.pdf comes from the Universal 3D Sample Software (Samples/TestScenes), Apache License 2.0.
        using PdfDocument document = PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "Documents", "box_base.pdf"));
        Annotation annotation = document.GetPage(1).GetAnnotations().Single(a => a.Type == AnnotationType.Artwork3D);
        Assert.True(Artwork3D.TryCreate(annotation, document.Structure.TokenScanner, document.Structure.FilterProvider, out Artwork3D? artwork));
        Artwork3DView source = artwork.DefaultView!;

        SceneView view = Artwork3DViewConverter.Convert(source, annotation.Rectangle.Width, annotation.Rectangle.Height);

        Assert.Equal("DefaultView", view.Name);
        Assert.Equal(source.CameraToWorld is not null && source.MatrixSource != Artwork3DMatrixSource.None, view.CameraToWorld is not null);
        Assert.Equal(source.MatrixSource == Artwork3DMatrixSource.U3DPath ? source.U3DPath : [], view.CameraNodePath);
        Assert.Equal((float?)source.CenterOfOrbit, view.OrbitDistance);
        Assert.Equal(source.Projection is not null, view.Projection is not null);
        Assert.Equal(source.Background is not null, view.Background is not null);
        Assert.Equal(source.Nodes.Count, view.NodeOverrides.Count);
    }
}
