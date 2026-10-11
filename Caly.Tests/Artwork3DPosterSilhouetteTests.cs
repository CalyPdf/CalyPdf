using System.Diagnostics;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// A PRC model drawn through its document's default view covers what the document's own picture of it
/// (the poster) covers: the parts are where they belong and the camera is the document's. Drawing needs a
/// GPU, so the pictures come from the viewer tool; the test runs only where CALY_3D_SAMPLES names the
/// folder of sample documents and CALY_3D_VIEWER the built <c>Scene3DViewer</c> executable.
/// </summary>
public class Artwork3DPosterSilhouetteTests(ITestOutputHelper output)
{
    // The silhouettes are compared on a grid this wide.
    private const int GridWidth = 160;

    // Shares of the union of both silhouettes that both cover, measured when the test was written: grinder
    // 0.97, engine 0.96, saw 0.91 (its body is nearly the poster's white). A part put in the wrong place
    // (the saw's chuck beside the saw, before copies took their prototype's location) falls below.
    private const double MinimumOverlap = 0.85;

    // Not every document can be measured this way: a poster with white parts on a white page (the other
    // SpinFire samples), with scenery the default view hides (OfficeBuilding) or with a legend drawn
    // into it (Trolley) covers other cells than the model does. Those were compared by eye.

    [Theory]
    [InlineData("tetra4d/3dpdf-Industrial-Saw-V03042024.pdf")]
    [InlineData("tetra4d/3dpdf-Auto-Engine-V03042024.pdf")]
    [InlineData("tetra4d/3dpdf-Industrial-Grinder-V03042024.pdf")]
    [InlineData("MIL-STD-31000B_SpinFire.pdf")]                 // these three: compressed meshes
    [InlineData("PMI Analysis_SpinFire.pdf")]
    [InlineData("Technical Data Package_SpinFire.pdf")]
    public void TheModelCoversWhatItsPosterCovers(string document)
    {
        string? samples = Environment.GetEnvironmentVariable("CALY_3D_SAMPLES");
        string? viewer = Environment.GetEnvironmentVariable("CALY_3D_VIEWER");
        string path = string.IsNullOrEmpty(samples) ? "" : Path.Combine(samples, document);
        if (!File.Exists(path) || !File.Exists(viewer))
        {
            Assert.Skip("Set CALY_3D_SAMPLES to the folder of sample 3D PDFs and CALY_3D_VIEWER to Scene3DViewer.exe.");
        }

        string folder = Directory.CreateTempSubdirectory("caly-silhouette").FullName;
        try
        {
            // Without a size the viewer gives the view the proportions the document gives it.
            string live = Path.Combine(folder, "live.png"), poster = Path.Combine(folder, "live.poster.png");
            var start = new ProcessStartInfo(viewer) { UseShellExecute = false };
            start.ArgumentList.Add(path);
            start.ArgumentList.Add("--poster");
            start.ArgumentList.Add("--capture");
            start.ArgumentList.Add(live);
            using (Process process = Process.Start(start)!)
            {
                if (!process.WaitForExit(TimeSpan.FromSeconds(90)))
                {
                    process.Kill();
                    Assert.Fail("The viewer did not finish.");
                }

                Assert.Equal(0, process.ExitCode);
            }

            bool[] ofPoster = Silhouette(poster, out int posterHeight);
            bool[] ofModel = Silhouette(live, out int modelHeight);
            Assert.InRange(Math.Abs(posterHeight - modelHeight), 0, 1);

            int both = 0, either = 0;
            for (int i = 0; i < Math.Min(ofPoster.Length, ofModel.Length); i++)
            {
                both += ofPoster[i] && ofModel[i] ? 1 : 0;
                either += ofPoster[i] || ofModel[i] ? 1 : 0;
            }

            double overlap = either == 0 ? 0 : (double)both / either;
            output.WriteLine($"{document}: poster {ofPoster.Count(c => c)} cells, model {ofModel.Count(c => c)}, overlap {overlap:F3}");
            Assert.True(either > GridWidth, "Neither picture shows anything.");
            Assert.True(overlap >= MinimumOverlap, $"The model covers {overlap:P0} of what it and its poster cover together.");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// Which cells of a grid <see cref="GridWidth"/> wide a picture fills with something other than its
    /// background, the background being what its top-left corner shows.
    /// </summary>
    private static bool[] Silhouette(string path, out int height)
    {
        using SKBitmap decoded = SKBitmap.Decode(path);
        Assert.NotNull(decoded);

        // Over white, as a page shows it, then averaged down to the grid.
        height = Math.Max(1, (int)Math.Round((double)GridWidth * decoded.Height / decoded.Width));
        using var grid = new SKBitmap(new SKImageInfo(GridWidth, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(grid))
        {
            canvas.Clear(SKColors.White);
            using SKImage image = SKImage.FromBitmap(decoded);
            canvas.DrawImage(image, new SKRect(0, 0, GridWidth, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        SKColor background = grid.GetPixel(1, 1);
        var cells = new bool[GridWidth * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                SKColor c = grid.GetPixel(x, y);
                int difference = Math.Max(Math.Abs(c.Red - background.Red), Math.Max(Math.Abs(c.Green - background.Green), Math.Abs(c.Blue - background.Blue)));
                cells[y * GridWidth + x] = difference > 24;
            }
        }

        return cells;
    }
}
