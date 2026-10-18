using System.Numerics;
using Caly.Pdf.Artwork;
using Caly.Rendering.Scene3D;
using Caly.Rendering.Scene3D.U3D;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Artwork;

namespace Caly.Tests;

/// <summary>
/// The camera conventions, proven on real documents (which are not part of the repository: set
/// CALY_3D_SAMPLES to the folder holding them). SimLab's exporter writes every view twice: as the name of
/// a U3D view node and as a camera-to-world matrix. A scene camera made from either must be the same
/// camera — which pins both the direction a U3D view node looks in and the handedness of the conversion
/// from PDF's camera space.
/// </summary>
public class Artwork3DCameraConventionTests
{
    internal static IReadOnlyList<string> CorpusDocuments(string subfolder)
    {
        string? root = Environment.GetEnvironmentVariable("CALY_3D_SAMPLES");
        string folder = string.IsNullOrEmpty(root) ? "" : Path.Combine(root, subfolder);
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.pdf", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToArray() : [];
    }

    internal static IEnumerable<(Annotation Annotation, Artwork3D Artwork)> Artworks(PdfDocument document)
    {
        foreach (UglyToad.PdfPig.Content.Page page in document.GetPages())
        {
            foreach (Annotation annotation in page.GetAnnotations())
            {
                if (Artwork3D.TryCreate(annotation, document.Structure.TokenScanner, document.Structure.FilterProvider, out Artwork3D? artwork))
                {
                    yield return (annotation, artwork);
                }
            }
        }
    }

    [Fact]
    public void AViewNodeAndItsCameraMatrixAreTheSameCamera()
    {
        IReadOnlyList<string> documents = CorpusDocuments("simlab");
        if (documents.Count == 0)
        {
            Assert.Skip("Set CALY_3D_SAMPLES to the folder of sample 3D PDFs.");
        }

        int compared = 0, views = 0;
        foreach (string path in documents)
        {
            using PdfDocument document = PdfDocument.Open(path);
            foreach ((Annotation annotation, Artwork3D artwork) in Artworks(document))
            {
                if (artwork.Format != Artwork3DFormat.U3D)
                {
                    continue;
                }

                using Scene scene = U3DReader.Read(artwork.GetData());
                SceneBounds bounds = scene.ComputeWorldBounds();
                float size = (bounds.Max - bounds.Min).Length();

                foreach (Artwork3DView source in artwork.Views)
                {
                    views++;
                    SceneView view = Artwork3DViewConverter.Convert(source, annotation.Rectangle.Width, annotation.Rectangle.Height);
                    if (view.CameraToWorld is not { } fromMatrix || view.CameraNodePath.Count == 0)
                    {
                        continue;
                    }

                    string where = $"{Path.GetFileName(path)}, view '{view.Name}'";
                    int node = scene.FindNode(view.CameraNodePath.ToArray(), SceneNodeKind.Camera);
                    Assert.True(node >= 0, $"{where}: no camera node '{string.Join("/", view.CameraNodePath)}'");
                    Matrix4x4 fromNode = scene.GetWorldMatrix(node);

                    Assert.True(Vector3.Distance(fromMatrix.Translation, fromNode.Translation) <= 1e-3f * size, $"{where}: positions differ");
                    foreach (Vector3 axis in new[] { -Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX })
                    {
                        Vector3 a = Vector3.Normalize(Vector3.TransformNormal(axis, fromMatrix));
                        Vector3 b = Vector3.Normalize(Vector3.TransformNormal(axis, fromNode));
                        double degrees = Math.Acos(Math.Clamp(Vector3.Dot(a, b), -1f, 1f)) * 180 / Math.PI;
                        Assert.True(degrees < 0.5, $"{where}: camera axis {axis} differs by {degrees:F2}°");
                    }

                    // The projection dictionary repeats the view node's field of view.
                    SceneCamera camera = scene.Cameras[scene.Nodes[node].Camera];
                    if (view.Projection is { Kind: SceneProjection.Perspective } projection && camera.Projection == SceneProjection.Perspective)
                    {
                        Assert.Equal(camera.FieldOfView, projection.FieldOfView, 1e-3f);
                    }

                    compared++;
                }
            }
        }

        // Every SimLab view carries both.
        Assert.True(compared > 0);
        Assert.Equal(views, compared);
    }
}
