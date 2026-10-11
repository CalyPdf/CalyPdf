using System.Numerics;
using Caly.Avalonia.Scene3D.Internal;
using Scene3D;
using SkiaSharp;

namespace Caly.Avalonia.Scene3D.Tests;

public class TextureStreamerTests
{
    private static byte[] Png(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(color);
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SceneTexture Texture(string name, byte[] data, SceneImageEncoding encoding = SceneImageEncoding.Png)
    {
        return new SceneTexture(name, 1, 1, SceneTextureChannels.Rgb, [new SceneTextureImage(encoding, SceneTextureChannels.Rgb, data)]);
    }

    /// <summary>A scene with the given textures and one material per entry of <paramref name="layersOfMaterials"/>, each a list of (texture, mode) layers.</summary>
    private static Scene Build(SceneTexture[] textures, params (int Texture, byte Mode)[][] layersOfMaterials)
    {
        using var builder = new SceneBuilder();
        foreach (SceneTexture texture in textures)
        {
            builder.AddTexture(texture);
        }

        int name = builder.AddName("M");
        foreach ((int Texture, byte Mode)[] layers in layersOfMaterials)
        {
            int first = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                int index = builder.AddTextureLayer(new SceneTextureLayer { Texture = layers[i].Texture, Mode = layers[i].Mode, Intensity = 1, Transform = Matrix4x4.Identity });
                first = i == 0 ? index : first;
            }

            builder.AddMaterial(new SceneMaterial { Diffuse = Vector3.One, Opacity = 1, FirstLayer = first, LayerCount = layers.Length, Name = name });
        }

        return builder.Build();
    }

    /// <summary>Takes results until none is pending, or fails after a few seconds.</summary>
    private static List<(int Texture, int Width, int Height)> Drain(TextureStreamer streamer, SemaphoreSlim ready)
    {
        var taken = new List<(int, int, int)>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (streamer.Pending > 0)
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "textures did not arrive");
            ready.Wait(50);
            while (streamer.TryTake(out int index, out DecodedTexture pixels))
            {
                taken.Add((index, pixels.Width, pixels.Height));
                pixels.Dispose();
            }
        }

        return taken;
    }

    [Fact]
    public void AMaterialShowsItsFirstLayerThatHasAUsableTexture()
    {
        SceneTexture good = Texture("Good", Png(4, 4, SKColors.Red));
        SceneTexture tiff = Texture("Tiff", Png(4, 4, SKColors.Red), SceneImageEncoding.Tiff);
        SceneTexture external = new("External", 4, 4, SceneTextureChannels.Rgb, [new SceneTextureImage(SceneImageEncoding.Png, SceneTextureChannels.Rgb, ReadOnlyMemory<byte>.Empty)]);

        using Scene scene = Build(
            [good, tiff, external],
            [],                                  // 0: no layers
            [(0, 0)],                            // 1: the texture
            [(1, 0), (2, 0), (-1, 0), (0, 0)],   // 2: unusable, external, missing, then the good one
            [(0, 3), (9, 0)],                    // 3: generated coordinates, then a texture that is not there
            [(0, 1), (0, 0)]);                   // 4: generated coordinates, then the mesh's own

        TextureStreamer.TexturesOfMaterials(scene, out int[] textures, out int[] layers);

        Assert.Equal([-1, 0, 0, -1, 0], textures);
        Assert.Equal(-1, layers[0]);
        Assert.Equal(scene.Materials[1].FirstLayer, layers[1]);
        Assert.Equal(scene.Materials[2].FirstLayer + 3, layers[2]);
        Assert.Equal(-1, layers[3]);
        Assert.Equal(scene.Materials[4].FirstLayer + 1, layers[4]);
    }

    [Fact]
    public void TheLongestSideIsHalvedUntilTheSetFitsTheBudget()
    {
        (int, int)[] sizes = [(4096, 4096), (1024, 512), (100, 100)];
        long Bytes(int side) => sizes.Sum(s =>
        {
            (int w, int h) = TextureDecoder.FitSize(s.Item1, s.Item2, side);
            return TextureStreamer.GpuBytes(w, h);
        });

        Assert.Equal(2048, TextureStreamer.PlanMaxSide(sizes, 2048, long.MaxValue));
        Assert.Equal(2048, TextureStreamer.PlanMaxSide(sizes, 2048, Bytes(2048)));
        Assert.Equal(1024, TextureStreamer.PlanMaxSide(sizes, 2048, Bytes(2048) - 1));
        Assert.Equal(256, TextureStreamer.PlanMaxSide(sizes, 2048, Bytes(256)));
        Assert.Equal(1, TextureStreamer.PlanMaxSide(sizes, 2048, 0));
        Assert.Equal(2048, TextureStreamer.PlanMaxSide([], 2048, 0));

        // A texture and its mipmaps: a third more than its top level.
        Assert.Equal(1024 * 1024 * 4 * 4 / 3, TextureStreamer.GpuBytes(1024, 1024));
    }

    [Fact]
    public void EveryTextureInUseArrivesOnceAndBrokenOnesAreCounted()
    {
        var random = new Random(5);
        var garbage = new byte[500];
        random.NextBytes(garbage);
        SceneTexture[] textures =
        [
            Texture("A", Png(8, 4, SKColors.Red)), Texture("Unused", Png(16, 16, SKColors.Blue)),
            Texture("B", Png(2, 6, SKColors.Green)), Texture("Broken", garbage)
        ];
        using Scene scene = Build(textures, [(0, 0)], [(2, 0)], [(0, 0)], [(3, 0)]);

        using var ready = new SemaphoreSlim(0);
        int notified = 0;
        using var streamer = new TextureStreamer(() => { Interlocked.Increment(ref notified); ready.Release(); });
        streamer.Start(scene);
        List<(int Texture, int Width, int Height)> taken = Drain(streamer, ready);

        Assert.Equal([(0, 8, 4), (2, 2, 6)], taken.Order().ToArray());
        Assert.Equal(1, streamer.Failed);
        Assert.Equal(0, streamer.Pending);
        Assert.Equal(3, notified);
        Assert.Equal(TextureStreamer.DefaultMaxSide, streamer.PlannedMaxSide);
        Assert.False(streamer.TryTake(out _, out _));
    }

    [Fact]
    public void ASceneOverTheTextureBudgetGetsSmallerTextures()
    {
        SceneTexture[] textures = [Texture("A", Png(256, 256, SKColors.Red)), Texture("B", Png(256, 128, SKColors.Green))];
        using Scene scene = Build(textures, [(0, 0)], [(1, 0)]);

        // Room for the pair at a quarter of their size.
        long budget = TextureStreamer.GpuBytes(64, 64) + TextureStreamer.GpuBytes(64, 32);
        using var ready = new SemaphoreSlim(0);
        using var streamer = new TextureStreamer(() => ready.Release(), 2048, budget);
        streamer.Start(scene);
        List<(int Texture, int Width, int Height)> taken = Drain(streamer, ready);

        Assert.Equal([(0, 64, 64), (1, 64, 32)], taken.Order().ToArray());
        Assert.Equal(64, streamer.PlannedMaxSide);
    }

    /// <summary>
    /// Review focus: a scene replaced while its textures are still being decoded. Nothing of the old scene
    /// may be handed over as if it were the new scene's.
    /// </summary>
    [Fact]
    public void TexturesOfAReplacedSceneAreThrownAway()
    {
        // Large enough that the first scene is still decoding when the second starts.
        var first = new SceneTexture[12];
        var firstMaterials = new (int, byte)[12][];
        for (int i = 0; i < first.Length; i++)
        {
            first[i] = Texture("Old" + i, Png(700, 700, SKColors.Red));
            firstMaterials[i] = [(i, 0)];
        }

        using Scene old = Build(first, firstMaterials);
        using Scene replacement = Build([Texture("New", Png(5, 3, SKColors.Blue))], [(0, 0)]);

        using var ready = new SemaphoreSlim(0);
        using var streamer = new TextureStreamer(() => ready.Release());
        streamer.Start(old);
        streamer.Start(replacement);
        List<(int Texture, int Width, int Height)> taken = Drain(streamer, ready);

        // Only the new scene's one texture: index 0 at 5 x 3, not an old 700 x 700 one that is also index 0.
        Assert.Equal([(0, 5, 3)], taken);

        // Give the abandoned work time to finish, then make sure none of it surfaces.
        Thread.Sleep(600);
        Assert.False(streamer.TryTake(out _, out _));
        Assert.Equal(0, streamer.Failed);

        streamer.Stop();
        Assert.Equal(0, streamer.Pending);
        Assert.False(streamer.TryTake(out _, out _));
    }

    [Fact]
    public void ASceneWithoutTexturesStartsNothing()
    {
        using Scene scene = Build([], [], []);
        int notified = 0;
        using var streamer = new TextureStreamer(() => notified++);

        streamer.Start(scene);

        Assert.Equal(0, streamer.Pending);
        Assert.False(streamer.TryTake(out _, out _));
        Thread.Sleep(50);
        Assert.Equal(0, notified);
    }
}
