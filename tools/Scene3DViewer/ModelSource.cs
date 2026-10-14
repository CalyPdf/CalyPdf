using Avalonia.Media.Imaging;
using Caly.Pdf.Artwork;
using Caly.Rendering.Scene3D;
using Caly.Rendering.Scene3D.Prc;
using Caly.Rendering.Scene3D.U3D;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Artwork;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Rendering.Skia;

namespace Scene3DViewer;

/// <summary>A scene and the views authored for it, from a .u3d or .prc file or from the 3D artwork of a PDF.</summary>
internal sealed class LoadedModel
{
    public required string Title { get; init; }

    public required Scene Scene { get; init; }

    public required IReadOnlyList<SceneView> Views { get; init; }

    /// <summary>Index of the document's default view in <see cref="Views"/>, or -1.</summary>
    public int DefaultView { get; init; } = -1;

    /// <summary>Width over height of the area the document shows the artwork in; 0 if unknown.</summary>
    public double AspectRatio { get; init; }

    /// <summary>What the page shows where the artwork is, before it is activated; null without a PDF or an appearance.</summary>
    public Bitmap? Poster { get; init; }

    public double LoadMilliseconds { get; init; }
}

internal static class ModelSource
{
    /// <summary>
    /// Loads a model. For a PDF, <paramref name="artworkIndex"/> picks among its 3D artworks in page order.
    /// </summary>
    /// <param name="posterWidth">Width in pixels to draw the poster at; 0 for none.</param>
    /// <exception cref="InvalidDataException">The file holds nothing this viewer can show.</exception>
    public static LoadedModel Load(string path, int artworkIndex = 0, int posterWidth = 0)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        byte[] file = File.ReadAllBytes(path);
        string name = Path.GetFileName(path);

        if (U3DReader.IsU3D(file) || PrcReader.IsPrc(file))
        {
            Scene scene = Read(U3DReader.IsU3D(file) ? Artwork3DFormat.U3D : Artwork3DFormat.Prc, file, name);
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

                // As Caly reads it: a PRC model is shown whole or not at all, and its views name its nodes
                // by identifiers that have to be matched to the scene's.
                Scene scene = Read(artwork.Format, artwork.GetData(), name);
                double load = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                return new LoadedModel
                {
                    Title = $"{name}, page {page.Number}",
                    Scene = scene,
                    Views = Artwork3DSceneReader.ResolveViews(artwork.Format, scene, views),
                    DefaultView = defaultIndex,
                    AspectRatio = height > 0 ? width / height : 0,
                    Poster = posterWidth > 0 && annotation.HasNormalAppearance() ? RenderPoster(document, page, annotation.Rectangle, posterWidth) : null,
                    LoadMilliseconds = load
                };
            }
        }

        throw new InvalidDataException(seen == 0 ? $"'{name}' has no 3D artwork." : $"'{name}' has {seen} 3D artworks; there is no number {artworkIndex}.");
    }

    /// <exception cref="InvalidDataException">The data cannot be shown; the message says why.</exception>
    private static Scene Read(Artwork3DFormat format, ReadOnlyMemory<byte> data, string name)
    {
        Artwork3DReadResult result = Artwork3DSceneReader.TryRead(format, data, out Scene? scene, out string? detail);
        return result == Artwork3DReadResult.Ok && scene is not null
            ? scene
            : throw new InvalidDataException($"The 3D model of '{name}' ({format}) cannot be shown: {result}{(detail is null ? "" : " — " + detail)}.");
    }

    private static bool HasNormalAppearance(this Annotation annotation)
    {
        return annotation.AnnotationDictionary.TryGet(UglyToad.PdfPig.Tokens.NameToken.Ap, out UglyToad.PdfPig.Tokens.IToken? _);
    }

    /// <summary>
    /// The page as PdfPig.Rendering.Skia draws it, cut to the annotation's rectangle: the document's own
    /// picture of the artwork. Assumes an unrotated page.
    /// </summary>
    private static Bitmap? RenderPoster(PdfDocument document, UglyToad.PdfPig.Content.Page page, PdfRectangle rectangle, int posterWidth)
    {
        try
        {
            if (!(rectangle.Width > 0) || !(rectangle.Height > 0))
            {
                return null;
            }

            float scale = (float)(posterWidth / rectangle.Width);
            document.AddSkiaPageFactory();
            using SKBitmap whole = document.GetPageAsSKBitmap(page.Number, scale, SKColors.White);

            PdfRectangle crop = page.CropBox.Bounds;
            var area = SKRectI.Round(new SKRect(
                (float)((rectangle.Left - crop.Left) * scale),
                (float)((crop.Top - rectangle.Top) * scale),
                (float)((rectangle.Right - crop.Left) * scale),
                (float)((crop.Top - rectangle.Bottom) * scale)));
            area.Intersect(new SKRectI(0, 0, whole.Width, whole.Height));
            if (area.Width <= 0 || area.Height <= 0)
            {
                return null;
            }

            using var cut = new SKBitmap(area.Width, area.Height);
            if (!whole.ExtractSubset(cut, area))
            {
                return null;
            }

            using SKData png = cut.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(png.ToArray());
            return new Bitmap(stream);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Program.Log("poster: could not be drawn — " + e.Message);
            return null;
        }
    }
}
