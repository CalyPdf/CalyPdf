using System.Collections.Concurrent;
using Scene3D;

namespace Caly.Avalonia.Scene3D.Internal;

/// <summary>
/// Decodes a scene's textures off the UI thread and hands them over one at a time, so that a model shows
/// at once in its material colours and its textures arrive after.
/// <para>
/// Each <see cref="Start"/> begins a new generation; whatever an earlier generation was still decoding is
/// thrown away when it arrives, never handed over. Before decoding, every image's size is read from its
/// header and the longest side allowed is halved until the whole set fits the budget.
/// </para>
/// <para>
/// <see cref="Start"/>, <see cref="Stop"/> and <see cref="TryTake"/> are for one thread (the UI thread);
/// the decoding runs on the thread pool.
/// </para>
/// </summary>
internal sealed class TextureStreamer : IDisposable
{
    public const int DefaultMaxSide = 2048;
    public const long DefaultBudgetBytes = 256L << 20;
    public const long DefaultMaxSourcePixels = 32_000_000;

    private readonly Action _notify;
    private readonly int _maxSide;
    private readonly long _budgetBytes, _maxSourcePixels;
    private readonly ConcurrentQueue<(int Generation, int Texture, DecodedTexture? Pixels)> _results = new();

    private CancellationTokenSource? _cancellation;
    private int _generation;
    private int _pending, _failed, _dropped;

    /// <param name="notify">Called, on a thread-pool thread, each time a result is ready to be taken.</param>
    public TextureStreamer(Action notify, int maxSide = DefaultMaxSide, long budgetBytes = DefaultBudgetBytes, long maxSourcePixels = DefaultMaxSourcePixels)
    {
        _notify = notify;
        _maxSide = maxSide;
        _budgetBytes = budgetBytes;
        _maxSourcePixels = maxSourcePixels;
    }

    /// <summary>Textures of the current scene still being decoded or waiting to be taken.</summary>
    public int Pending => Volatile.Read(ref _pending);

    /// <summary>Textures of the current scene that could not be decoded.</summary>
    public int Failed => Volatile.Read(ref _failed);

    /// <summary>Results of earlier scenes that arrived late and were thrown away.</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    /// <summary>The longest side textures of the current scene are decoded to, after fitting the budget.</summary>
    public int PlannedMaxSide { get; private set; }

    /// <summary>
    /// For each material, the texture it shows as its base colour and the layer that names it: the first
    /// layer that uses the mesh's own texture coordinates and whose texture has a JPEG or PNG colour image.
    /// -1 where there is none.
    /// </summary>
    public static void TexturesOfMaterials(Scene scene, out int[] textureOfMaterial, out int[] layerOfMaterial)
    {
        ReadOnlySpan<SceneMaterial> materials = scene.Materials;
        ReadOnlySpan<SceneTextureLayer> layers = scene.TextureLayers;
        IReadOnlyList<SceneTexture> textures = scene.Textures;

        textureOfMaterial = new int[materials.Length];
        layerOfMaterial = new int[materials.Length];
        for (int m = 0; m < materials.Length; m++)
        {
            textureOfMaterial[m] = layerOfMaterial[m] = -1;
            for (int i = 0; i < materials[m].LayerCount; i++)
            {
                int layerIndex = materials[m].FirstLayer + i;
                if ((uint)layerIndex >= (uint)layers.Length)
                {
                    break;
                }

                SceneTextureLayer layer = layers[layerIndex];
                if (layer.Mode == 0 && (uint)layer.Texture < (uint)textures.Count && TextureDecoder.HasColorImage(textures[layer.Texture]))
                {
                    textureOfMaterial[m] = layer.Texture;
                    layerOfMaterial[m] = layerIndex;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The longest side to decode to so that textures of the given sizes, with their mipmaps, fit the
    /// budget: <paramref name="maxSide"/>, or that halved as often as it takes.
    /// </summary>
    public static int PlanMaxSide(ReadOnlySpan<(int Width, int Height)> sizes, int maxSide, long budgetBytes)
    {
        int side = Math.Max(1, maxSide);
        while (side > 1 && GpuBytes(sizes, side) > budgetBytes)
        {
            side /= 2;
        }

        return side;
    }

    /// <summary>GPU bytes of one texture with its mipmaps.</summary>
    public static long GpuBytes(int width, int height) => (long)width * height * 4 * 4 / 3;

    private static long GpuBytes(ReadOnlySpan<(int Width, int Height)> sizes, int maxSide)
    {
        long total = 0;
        foreach ((int width, int height) in sizes)
        {
            (int w, int h) = TextureDecoder.FitSize(width, height, maxSide);
            total += GpuBytes(w, h);
        }

        return total;
    }

    /// <summary>
    /// Begins decoding the textures the scene's materials show. Results of anything started before are
    /// abandoned.
    /// </summary>
    public void Start(Scene scene)
    {
        Stop();
        TexturesOfMaterials(scene, out int[] textureOfMaterial, out _);

        // The texture objects are immutable and outlive the scene's own lifetime rules: the workers
        // never touch the scene itself.
        IReadOnlyList<SceneTexture> all = scene.Textures;
        var wanted = new List<(int Index, SceneTexture Texture)>();
        var seen = new HashSet<int>();
        foreach (int index in textureOfMaterial)
        {
            if (index >= 0 && seen.Add(index))
            {
                wanted.Add((index, all[index]));
            }
        }

        PlannedMaxSide = _maxSide;
        if (wanted.Count == 0)
        {
            return;
        }

        int generation = _generation;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Volatile.Write(ref _pending, wanted.Count);
        _ = Task.Run(() => Decode(wanted, generation, cancellation.Token));
    }

    /// <summary>Abandons the current scene's textures: nothing more of it is handed over.</summary>
    public void Stop()
    {
        _generation++;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        Volatile.Write(ref _pending, 0);
        Volatile.Write(ref _failed, 0);

        // What is queued belongs to the generation just ended.
        while (_results.TryDequeue(out (int Generation, int Texture, DecodedTexture? Pixels) stale))
        {
            if (stale.Pixels is not null)
            {
                stale.Pixels.Dispose();
                Interlocked.Increment(ref _dropped);
            }
        }
    }

    /// <summary>
    /// The next decoded texture of the current scene, to upload and then dispose. False when none is
    /// ready. Results of earlier scenes are disposed here and never returned.
    /// </summary>
    public bool TryTake(out int textureIndex, out DecodedTexture pixels)
    {
        while (_results.TryDequeue(out (int Generation, int Texture, DecodedTexture? Pixels) result))
        {
            if (result.Generation != _generation)
            {
                if (result.Pixels is not null)
                {
                    result.Pixels.Dispose();
                    Interlocked.Increment(ref _dropped);
                }

                continue;
            }

            Interlocked.Decrement(ref _pending);
            if (result.Pixels is null)
            {
                Interlocked.Increment(ref _failed);
                continue;
            }

            textureIndex = result.Texture;
            pixels = result.Pixels;
            return true;
        }

        textureIndex = -1;
        pixels = null!;
        return false;
    }

    private void Decode(List<(int Index, SceneTexture Texture)> wanted, int generation, CancellationToken cancellation)
    {
        // First the sizes, from the headers alone, to fit the whole set into the budget.
        var sizes = new (int Width, int Height)[wanted.Count];
        for (int i = 0; i < wanted.Count && !cancellation.IsCancellationRequested; i++)
        {
            TextureDecoder.TryGetSize(wanted[i].Texture, _maxSourcePixels, out sizes[i].Width, out sizes[i].Height);
        }

        int maxSide = PlanMaxSide(sizes, _maxSide, _budgetBytes);
        if (generation == Volatile.Read(ref _generation))
        {
            PlannedMaxSide = maxSide;
        }

        // Then the pixels, a few images at a time.
        int next = -1;
        int workers = Math.Clamp(Environment.ProcessorCount - 1, 1, 4);
        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, _ =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                int i = Interlocked.Increment(ref next);
                if (i >= wanted.Count)
                {
                    return;
                }

                DecodedTexture? pixels = TextureDecoder.Decode(wanted[i].Texture, maxSide, _maxSourcePixels);
                if (cancellation.IsCancellationRequested)
                {
                    // Nobody will take it: the generation ended while it was being decoded.
                    if (pixels is not null)
                    {
                        pixels.Dispose();
                        Interlocked.Increment(ref _dropped);
                    }

                    return;
                }

                _results.Enqueue((generation, wanted[i].Index, pixels));
                _notify();
            }
        });
    }

    public void Dispose() => Stop();
}
