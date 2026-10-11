using Caly.Pdf.Artwork;
using Scene3D;
using Scene3D.U3D;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Artwork;

namespace Scene3DViewer;

/// <summary>A scene and the views authored for it, from a .u3d file or from the 3D artwork of a PDF.</summary>
internal sealed class LoadedModel
{
    public required string Title { get; init; }

    public required Scene Scene { get; init; }

    public required IReadOnlyList<SceneView> Views { get; init; }

    /// <summary>Index of the document's default view in <see cref="Views"/>, or -1.</summary>
    public int DefaultView { get; init; } = -1;

    /// <summary>Width over height of the area the document shows the artwork in; 0 if unknown.</summary>
    public double AspectRatio { get; init; }

    public double LoadMilliseconds { get; init; }
}

internal static class ModelSource
{
    /// <summary>
    /// Loads a model. For a PDF, <paramref name="artworkIndex"/> picks among its 3D artworks in page order.
    /// </summary>
    /// <exception cref="InvalidDataException">The file holds nothing this viewer can show.</exception>
    public static LoadedModel Load(string path, int artworkIndex = 0)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        byte[] file = File.ReadAllBytes(path);
        string name = Path.GetFileName(path);

        if (U3DReader.IsU3D(file))
        {
            Scene scene = U3DReader.Read(file);
            return new LoadedModel { Title = name, Scene = scene, Views = [], LoadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds };
        }

        using PdfDocument document = PdfDocument.Open(file);
        int seen = 0;
        foreach (UglyToad.PdfPig.Content.Page page in document.GetPages())
        {
            foreach (Annotation annotation in page.GetAnnotations())
            {
                if (!Artwork3D.TryCreate(annotation, document.Structure.TokenScanner, document.Structure.FilterProvider, out Artwork3D? artwork) || seen++ != artworkIndex)
                {
                    continue;
                }

                if (artwork.Format != Artwork3DFormat.U3D)
                {
                    throw new InvalidDataException($"The 3D artwork of '{name}' is {artwork.FormatName ?? "of an unknown format"}; only U3D can be shown.");
                }

                // The 3D view box if the annotation has one, else the annotation's rectangle.
                double width = artwork.ViewBox?.Width ?? annotation.Rectangle.Width, height = artwork.ViewBox?.Height ?? annotation.Rectangle.Height;

                var views = new List<SceneView>();
                Artwork3DView? defaultView = artwork.DefaultView;
                int defaultIndex = -1;
                foreach (Artwork3DView view in artwork.Views)
                {
                    if (ReferenceEquals(view, defaultView))
                    {
                        defaultIndex = views.Count;
                    }

                    views.Add(Artwork3DViewConverter.Convert(view, width, height));
                }

                // A default view given inline on the annotation is not one of the stream's views.
                if (defaultIndex < 0 && defaultView is not null)
                {
                    defaultIndex = views.Count;
                    views.Add(Artwork3DViewConverter.Convert(defaultView, width, height));
                }

                Scene scene = U3DReader.Read(artwork.GetData());
                return new LoadedModel
                {
                    Title = $"{name}, page {page.Number}",
                    Scene = scene,
                    Views = views,
                    DefaultView = defaultIndex,
                    AspectRatio = height > 0 ? width / height : 0,
                    LoadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds
                };
            }
        }

        throw new InvalidDataException(seen == 0 ? $"'{name}' has no 3D artwork." : $"'{name}' has {seen} 3D artworks; there is no number {artworkIndex}.");
    }
}
