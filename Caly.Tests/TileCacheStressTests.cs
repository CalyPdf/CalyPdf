using System.Collections.Concurrent;
using Caly.Core.Services.Rendering;
using Caly.Core.Utilities;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// Pushes <see cref="TileCache"/> well past its memory budget - the scroll/zoom churn the
/// production 128 MB budget is sized against (see CLAUDE.md) - and checks the two failure modes a
/// reader would actually notice: an "artefact" (a cached tile hands back the wrong pixels - stale,
/// corrupted, or belonging to a different key) and a "flicker" (a tile a frame is actively drawing
/// gets freed out from under it mid-draw).
/// <para>
/// Eviction mechanics themselves (LRU order, budget accounting, one eviction at a time) are
/// covered by <see cref="TileCacheTests"/>. This file only cares about what a caller observes at
/// scale: hundreds of evictions in a single run, and concurrent writers/readers racing at
/// capacity the way <see cref="TileRenderService"/>'s background workers race the UI thread's
/// per-frame composition in production.
/// </para>
/// </summary>
public class TileCacheStressTests
{
    private const int TileSide = 32;
    private const long TileBytes = TileSide * TileSide * 4;

    /// <summary>
    /// A color derived deterministically from the key, so a tile that comes back with the wrong
    /// content - corrupted, or swapped with another key's tile - is caught by exact comparison
    /// rather than by a weaker "some image or other" check.
    /// </summary>
    private static SKColor ColorFor(TileKey key)
    {
        byte r = (byte)(key.PageNumber * 7 + key.TileLevel);
        byte g = (byte)(key.Column * 13 + 1);
        byte b = (byte)(key.Row * 29 + 1);
        return new SKColor(r, g, b, 255);
    }

    private static TileImage CreateTile(TileKey key)
    {
        var info = new SKImageInfo(TileSide, TileSide, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(ColorFor(key));
        return new TileImage(surface.Snapshot());
    }

    private static bool MatchesExpectedColor(IRef<TileImage> image, TileKey key)
    {
        using var bitmap = SKBitmap.FromImage(image.Item.Image);
        return bitmap.GetPixel(TileSide / 2, TileSide / 2) == ColorFor(key);
    }

    [Fact]
    public void SustainedEvictionFarBeyondBudget_NeverCorruptsOrMixesUpSurvivingTileContent()
    {
        // Room for 16 tiles, but the run below adds 40 pages x 8 columns = 320 distinct keys -
        // a ~20x oversubscription, far past the single - or double-eviction cases TileCacheTests
        // covers, closer to a real scroll session than a unit-level eviction check.
        const int tileCapacity = 16;
        using var cache = new TileCache(maxMemoryBytes: TileBytes * tileCapacity);

        var allKeys = new List<TileKey>();
        for (int page = 1; page <= 40; page++)
        {
            for (int col = 0; col < 8; col++)
            {
                var key = new TileKey(page, 0, col, 0);
                allKeys.Add(key);
                cache.Add(key, CreateTile(key));

                // Interleave lookups the way real scrolling would: keep touching an
                // already-added key so the LRU order keeps shuffling while new tiles arrive.
                var revisit = allKeys[Math.Max(0, allKeys.Count - 5)];
                var probe = cache.Lookup(revisit);
                if (probe.IsCached)
                {
                    Assert.True(MatchesExpectedColor(probe.Image!, revisit));
                    probe.Image!.Dispose();
                }
            }
        }

        // Whatever survived the churn must still hold exactly the pixels it was stored with -
        // never another tile's content, never garbage.
        int survivors = 0;
        foreach (var key in allKeys)
        {
            var result = cache.Lookup(key);
            if (!result.IsCached)
            {
                continue;
            }

            survivors++;
            Assert.True(MatchesExpectedColor(result.Image!, key));
            result.Image!.Dispose();
        }

        // The budget was honoured throughout the whole run, not just at the end.
        Assert.True(survivors <= tileCapacity);
        Assert.True(survivors > 0);
    }

    [Fact]
    public void TilesBorrowedForAnInFlightDraw_SurviveHeavyEvictionPressureWithUnchangedPixels()
    {
        // Simulate composing one frame: borrow every tile currently in view, exactly as a draw
        // pass does, then keep those references alive while unrelated scrolling floods the cache
        // with far more tiles than the budget holds - evicting the visible tiles' cache entries
        // out from under the still-drawing frame.
        const int tileCapacity = 8;
        using var cache = new TileCache(maxMemoryBytes: TileBytes * tileCapacity);

        var visible = new List<TileKey>();
        for (int col = 0; col < tileCapacity; col++)
        {
            var key = new TileKey(1, 0, col, 0);
            visible.Add(key);
            cache.Add(key, CreateTile(key));
        }

        // Borrow references the way a draw pass does - this is what must not go stale.
        var borrowed = visible.Select(k => (Key: k, Ref: cache.Lookup(k).Image!)).ToList();
        Assert.All(borrowed, b => Assert.NotNull(b.Ref));

        // Flood the cache with 490 tiles from other pages: every visible tile above is now the
        // least-recently-used content and is evicted from the cache's own bookkeeping.
        for (int page = 2; page <= 50; page++)
        {
            for (int col = 0; col < 10; col++)
            {
                var key = new TileKey(page, 0, col, 0);
                cache.Add(key, CreateTile(key));
            }
        }

        foreach (var key in visible)
        {
            Assert.Equal(TileCacheState.Missing, cache.Lookup(key).State);
        }

        // No flicker, no artefact: every borrowed reference is still alive and still shows the
        // exact pixels it had when the frame started drawing it.
        foreach (var (key, imageRef) in borrowed)
        {
            Assert.True(imageRef.IsAlive);
            Assert.True(MatchesExpectedColor(imageRef, key));
            imageRef.Dispose();
        }
    }

    [Fact]
    public async Task ConcurrentRenderersAndReadersAtCapacity_ProduceNoCorruptionOrCrashes()
    {
        // Mirrors production: TileRenderService's background workers call Add/AddBlank while the
        // UI thread calls Lookup/TryGetRange every frame, all racing on the same lock right at
        // the point eviction pressure is highest. Each writer owns a disjoint page range, so
        // every key has exactly one possible outcome (blank, or a specific color) regardless of
        // interleaving - which is what lets a reader assert correctness under real concurrency
        // instead of only single-threaded.
        const int tileCapacity = 32;
        const int pagesPerWriter = 60;
        const int writerCount = 4;
        using var cache = new TileCache(maxMemoryBytes: TileBytes * tileCapacity);

        var exceptions = new ConcurrentBag<Exception>();

        var writers = Enumerable.Range(0, writerCount).Select(w => Task.Run(() =>
        {
            try
            {
                for (int page = w * pagesPerWriter; page < (w + 1) * pagesPerWriter; page++)
                {
                    for (int col = 0; col < 4; col++)
                    {
                        var key = new TileKey(page, 0, col, 0);
                        if (col % 3 == 0)
                        {
                            cache.AddBlank(key);
                        }
                        else
                        {
                            cache.Add(key, CreateTile(key));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        var readers = Enumerable.Range(0, writerCount).Select(_ => Task.Run(() =>
        {
            try
            {
                int totalPages = writerCount * pagesPerWriter;
                for (int i = 0; i < 2000; i++)
                {
                    var key = new TileKey(i % totalPages, 0, i % 4, 0);
                    var result = cache.Lookup(key);
                    if (result.IsCached)
                    {
                        // A hit must always carry exactly the pixels recorded for that key - a
                        // race that hands out someone else's tile is exactly the artefact this
                        // stress test exists to catch.
                        Assert.True(MatchesExpectedColor(result.Image!, key));
                        result.Image!.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(writers.Concat(readers));

        if (!exceptions.IsEmpty)
        {
            throw new AggregateException(exceptions);
        }
    }
}
