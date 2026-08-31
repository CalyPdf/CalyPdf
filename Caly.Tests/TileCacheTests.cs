using Caly.Core.Services.Rendering;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// Covers the tile cache contract the render pass depends on: ref-counted image tiles with an LRU
/// memory budget, and blank tiles - tiles that rendered to nothing - which are recorded as bare
/// keys outside the budget, the LRU and the ref-counting machinery.
/// </summary>
public class TileCacheTests
{
    private const int TileSide = 64;

    /// <summary>Bytes a single <see cref="CreateTile"/> image occupies (64 x 64 x 4bpp).</summary>
    private const long TileBytes = TileSide * TileSide * 4;

    private static TileImage CreateTile(SKColor? color = null)
    {
        var info = new SKImageInfo(TileSide, TileSide, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(color ?? SKColors.Red);

        // Snapshot rather than SKImage.FromBitmap: the snapshot owns its own pixels, so the
        // surface can be disposed here without invalidating the image.
        return new TileImage(surface.Snapshot());
    }

    private static TileKey Key(int page = 1, int level = 0, int col = 0, int row = 0)
        => new(page, level, col, row);

    #region Image tiles: existing behaviour

    [Fact]
    public void Lookup_UnknownKey_ReportsMissing()
    {
        using var cache = new TileCache();

        var result = cache.Lookup(Key());

        Assert.Equal(TileCacheState.Missing, result.State);
        Assert.Null(result.Image);
        Assert.False(result.IsCached);
    }

    [Fact]
    public void Lookup_CachedTile_ReturnsClonedReferenceCallerMustDispose()
    {
        using var cache = new TileCache();
        var tile = CreateTile();
        cache.Add(Key(), tile);

        var first = cache.Lookup(Key());
        var second = cache.Lookup(Key());

        Assert.Equal(TileCacheState.Cached, first.State);
        Assert.Same(tile, first.Image!.Item);

        // The cache holds one reference; each lookup adds another.
        Assert.Equal(3, first.Image.RefCount);

        first.Image.Dispose();
        second.Image!.Dispose();
    }

    [Fact]
    public void Add_SameKeyTwice_KeepsFirstAndDisposesSecond()
    {
        using var cache = new TileCache();
        var first = CreateTile();
        var second = CreateTile();

        cache.Add(Key(), first);
        cache.Add(Key(), second);

        var result = cache.Lookup(Key());
        Assert.Same(first, result.Image!.Item);

        // The rejected tile must not be leaked: the cache disposes what it refuses to store.
        Assert.Equal(IntPtr.Zero, second.Image.Handle);

        result.Image.Dispose();
    }

    [Fact]
    public void Add_TileLargerThanWholeBudget_IsRejectedAndDisposed()
    {
        using var cache = new TileCache(maxMemoryBytes: TileBytes - 1);
        var tile = CreateTile();

        cache.Add(Key(), tile);

        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key()).State);
        Assert.Equal(IntPtr.Zero, tile.Image.Handle);
    }

    [Fact]
    public void Add_OverBudget_EvictsLeastRecentlyUsedTile()
    {
        // Room for exactly two tiles.
        using var cache = new TileCache(maxMemoryBytes: TileBytes * 2);

        cache.Add(Key(col: 0), CreateTile());
        cache.Add(Key(col: 1), CreateTile());
        cache.Add(Key(col: 2), CreateTile());

        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key(col: 0)).State);

        var b = cache.Lookup(Key(col: 1));
        var c = cache.Lookup(Key(col: 2));
        Assert.Equal(TileCacheState.Cached, b.State);
        Assert.Equal(TileCacheState.Cached, c.State);

        b.Image!.Dispose();
        c.Image!.Dispose();
    }

    [Fact]
    public void Lookup_RefreshesLruOrder_SoTheRefreshedTileSurvivesEviction()
    {
        using var cache = new TileCache(maxMemoryBytes: TileBytes * 2);

        cache.Add(Key(col: 0), CreateTile());
        cache.Add(Key(col: 1), CreateTile());

        // Touching tile 0 makes tile 1 the least recently used.
        cache.Lookup(Key(col: 0)).Image!.Dispose();

        cache.Add(Key(col: 2), CreateTile());

        var survivor = cache.Lookup(Key(col: 0));
        Assert.Equal(TileCacheState.Cached, survivor.State);
        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key(col: 1)).State);

        survivor.Image!.Dispose();
    }

    [Fact]
    public void EvictedTile_StaysAliveWhileTheRenderPassStillHoldsIt()
    {
        // This is the contract TileDrawEntry.DrawTile relies on: a draw entry owns its reference
        // for the whole draw, so eviction on another thread cannot free the image under it.
        using var cache = new TileCache(maxMemoryBytes: TileBytes);
        var tile = CreateTile();
        cache.Add(Key(col: 0), tile);

        var borrowed = cache.Lookup(Key(col: 0));

        // Force the borrowed tile out of the cache.
        cache.Add(Key(col: 1), CreateTile());
        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key(col: 0)).State);

        // The cache dropped its reference, but ours keeps the native image alive.
        Assert.True(borrowed.Image!.IsAlive);
        Assert.NotEqual(IntPtr.Zero, tile.Image.Handle);
        Assert.Equal(TileSide, borrowed.Image.Item.Width);

        // Releasing the last reference is what actually frees it.
        borrowed.Image.Dispose();
        Assert.Equal(IntPtr.Zero, tile.Image.Handle);
    }

    [Fact]
    public void Contains_ReportsCachedTilesWithoutDisturbingLruOrder()
    {
        using var cache = new TileCache(maxMemoryBytes: TileBytes * 2);

        cache.Add(Key(col: 0), CreateTile());
        cache.Add(Key(col: 1), CreateTile());

        Assert.True(cache.Contains(Key(col: 0)));
        Assert.False(cache.Contains(Key(col: 9)));

        // Contains must not count as a use: tile 0 is still the eviction victim.
        cache.Add(Key(col: 2), CreateTile());
        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key(col: 0)).State);
    }

    [Fact]
    public void TryGetRange_ReturnsHitsInRowMajorOrder()
    {
        using var cache = new TileCache();
        cache.Add(Key(col: 0, row: 0), CreateTile());
        cache.Add(Key(col: 1, row: 1), CreateTile());

        Span<TileCacheResult> results = new TileCacheResult[4];
        cache.TryGetRange(1, 0, 0, 0, 1, 1, results);

        Assert.Equal(TileCacheState.Cached, results[0].State);  // (0,0)
        Assert.Equal(TileCacheState.Missing, results[1].State); // (1,0)
        Assert.Equal(TileCacheState.Missing, results[2].State); // (0,1)
        Assert.Equal(TileCacheState.Cached, results[3].State);  // (1,1)

        foreach (var result in results)
        {
            result.Image?.Dispose();
        }
    }

    [Fact]
    public void FindMissing_ReportsOnlyUncachedTiles()
    {
        using var cache = new TileCache();
        cache.Add(Key(col: 0, row: 0), CreateTile());

        var missing = new List<TileCoord>();
        cache.FindMissing(1, 0, 0, 0, 1, 0, missing);

        Assert.Equal([new TileCoord(1, 0)], missing);
    }

    [Fact]
    public void InvalidatePage_RemovesOnlyThatPagesTiles()
    {
        using var cache = new TileCache();
        cache.Add(Key(page: 1), CreateTile());
        cache.Add(Key(page: 2), CreateTile());

        cache.InvalidatePage(1);

        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key(page: 1)).State);

        var other = cache.Lookup(Key(page: 2));
        Assert.Equal(TileCacheState.Cached, other.State);
        other.Image!.Dispose();
    }

    [Fact]
    public void InvalidatePage_DisposesTheImagesItDrops()
    {
        using var cache = new TileCache();
        var tile = CreateTile();
        cache.Add(Key(), tile);

        cache.InvalidatePage(1);

        Assert.Equal(IntPtr.Zero, tile.Image.Handle);
    }

    [Fact]
    public void EvictPageLevelsExcept_KeepsOnlyTheRequestedLevel()
    {
        using var cache = new TileCache();
        cache.Add(Key(level: 0), CreateTile());
        cache.Add(Key(level: 1), CreateTile());
        cache.Add(Key(level: 2), CreateTile());

        cache.EvictPageLevelsExcept(1, keepLevel: 1);

        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key(level: 0)).State);
        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key(level: 2)).State);

        var kept = cache.Lookup(Key(level: 1));
        Assert.Equal(TileCacheState.Cached, kept.State);
        kept.Image!.Dispose();
    }

    [Fact]
    public void GetCachedLevelsAbove_ReturnsLevelsStrictlyAboveInAscendingOrder()
    {
        using var cache = new TileCache();
        cache.Add(Key(level: 0), CreateTile());
        cache.Add(Key(level: 3), CreateTile());
        cache.Add(Key(level: 1), CreateTile());

        Assert.Equal<int[]>([1, 3], cache.GetCachedLevelsAbove(1, baseLevel: 0));
        Assert.Null(cache.GetCachedLevelsAbove(1, baseLevel: 3));
        Assert.Null(cache.GetCachedLevelsAbove(pageNumber: 99, baseLevel: 0));
    }

    [Fact]
    public void Dispose_DisposesEveryCachedImage()
    {
        var cache = new TileCache();
        var tile = CreateTile();
        cache.Add(Key(), tile);

        cache.Dispose();

        Assert.Equal(IntPtr.Zero, tile.Image.Handle);
        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key()).State);
    }

    #endregion

    #region Blank tiles

    [Fact]
    public void AddBlank_ThenLookup_ReportsBlankAndHandsOutNoReference()
    {
        using var cache = new TileCache();

        cache.AddBlank(Key());

        var result = cache.Lookup(Key());
        Assert.Equal(TileCacheState.Blank, result.State);
        Assert.Null(result.Image);
        Assert.False(result.IsCached);
    }

    [Fact]
    public void Contains_TreatsBlankTilesAsAlreadyRendered()
    {
        // The render worker early-outs on Contains. A blank tile has been rendered, so
        // re-rendering it would redo the work only to rediscover that it is empty.
        using var cache = new TileCache();

        cache.AddBlank(Key());

        Assert.True(cache.Contains(Key()));
    }

    [Fact]
    public void FindMissing_DoesNotRequestBlankTilesAgain()
    {
        using var cache = new TileCache();
        cache.Add(Key(col: 0), CreateTile());
        cache.AddBlank(Key(col: 1));

        var missing = new List<TileCoord>();
        cache.FindMissing(1, 0, 0, 0, 2, 0, missing);

        Assert.Equal([new TileCoord(2, 0)], missing);
    }

    [Fact]
    public void TryGetRange_DistinguishesBlankFromMissing()
    {
        using var cache = new TileCache();
        cache.Add(Key(col: 0), CreateTile());
        cache.AddBlank(Key(col: 1));

        Span<TileCacheResult> results = new TileCacheResult[3];
        cache.TryGetRange(1, 0, 0, 0, 2, 0, results);

        Assert.Equal(TileCacheState.Cached, results[0].State);
        Assert.NotNull(results[0].Image);

        Assert.Equal(TileCacheState.Blank, results[1].State);
        Assert.Null(results[1].Image);

        Assert.Equal(TileCacheState.Missing, results[2].State);
        Assert.Null(results[2].Image);

        results[0].Image!.Dispose();
    }

    [Fact]
    public void BlankTiles_SurviveMemoryPressureThatEvictsImages()
    {
        // The point of recording blanks separately: they cost no budget, so evicting one would
        // free nothing while discarding the knowledge that stops us re-rendering it.
        using var cache = new TileCache(maxMemoryBytes: TileBytes);

        for (int col = 0; col < 50; col++)
        {
            cache.AddBlank(Key(level: 1, col: col));
        }

        cache.Add(Key(col: 0), CreateTile());
        cache.Add(Key(col: 1), CreateTile());

        // The image tile was evicted under pressure...
        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key(col: 0)).State);

        // ...but every blank record is intact.
        for (int col = 0; col < 50; col++)
        {
            Assert.Equal(TileCacheState.Blank, cache.Lookup(Key(level: 1, col: col)).State);
        }

        cache.Lookup(Key(col: 1)).Image!.Dispose();
    }

    [Fact]
    public void GetCachedLevelsAbove_IgnoresLevelsThatHoldOnlyBlankTiles()
    {
        // A blank-only level can never contribute a pixel, so it must not be offered to the
        // finer-level fallback search as a candidate to scan.
        using var cache = new TileCache();
        cache.AddBlank(Key(level: 1));
        cache.Add(Key(level: 2), CreateTile());

        Assert.Equal<int[]>([2], cache.GetCachedLevelsAbove(1, baseLevel: 0));
    }

    [Fact]
    public void GetCachedLevelsAbove_StillReportsALevelHoldingBothBlankAndImageTiles()
    {
        using var cache = new TileCache();
        cache.AddBlank(Key(level: 1, col: 0));
        cache.Add(Key(level: 1, col: 1), CreateTile());

        Assert.Equal<int[]>([1], cache.GetCachedLevelsAbove(1, baseLevel: 0));
    }

    [Fact]
    public void Add_SupersedesAnEarlierBlankRecordForTheSameKey()
    {
        using var cache = new TileCache();
        cache.AddBlank(Key());

        cache.Add(Key(), CreateTile());

        var result = cache.Lookup(Key());
        Assert.Equal(TileCacheState.Cached, result.State);
        result.Image!.Dispose();
    }

    [Fact]
    public void AddBlank_YieldsToAnImageAlreadyCachedForTheSameKey()
    {
        using var cache = new TileCache();
        var tile = CreateTile();
        cache.Add(Key(), tile);

        cache.AddBlank(Key());

        var result = cache.Lookup(Key());
        Assert.Equal(TileCacheState.Cached, result.State);
        Assert.Same(tile, result.Image!.Item);
        result.Image.Dispose();
    }

    [Fact]
    public void AddBlank_IsIdempotent()
    {
        using var cache = new TileCache();

        cache.AddBlank(Key());
        cache.AddBlank(Key());

        Assert.Equal(TileCacheState.Blank, cache.Lookup(Key()).State);
    }

    [Fact]
    public void InvalidatePage_RemovesBlankRecords_EvenWhenThePageHasNoImages()
    {
        // Blank keys live outside _pageKeys, so the image-side early-out must not skip them.
        using var cache = new TileCache();
        cache.AddBlank(Key(page: 1));
        cache.AddBlank(Key(page: 2));

        cache.InvalidatePage(1);

        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key(page: 1)).State);
        Assert.False(cache.Contains(Key(page: 1)));
        Assert.Equal(TileCacheState.Blank, cache.Lookup(Key(page: 2)).State);
    }

    [Fact]
    public void EvictPageLevelsExcept_PrunesStaleLevelBlanksAndKeepsTheCurrentLevel()
    {
        using var cache = new TileCache();
        cache.AddBlank(Key(level: 0));
        cache.AddBlank(Key(level: 1));
        cache.AddBlank(Key(level: 2));

        cache.EvictPageLevelsExcept(1, keepLevel: 1);

        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key(level: 0)).State);
        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key(level: 2)).State);
        Assert.Equal(TileCacheState.Blank, cache.Lookup(Key(level: 1)).State);
    }

    [Fact]
    public void EvictPageLevelsExcept_LeavesOtherPagesBlanksAlone()
    {
        using var cache = new TileCache();
        cache.AddBlank(Key(page: 1, level: 0));
        cache.AddBlank(Key(page: 2, level: 0));

        cache.EvictPageLevelsExcept(1, keepLevel: 1);

        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key(page: 1, level: 0)).State);
        Assert.Equal(TileCacheState.Blank, cache.Lookup(Key(page: 2, level: 0)).State);
    }

    [Fact]
    public void Dispose_ClearsBlankRecords()
    {
        var cache = new TileCache();
        cache.AddBlank(Key());

        cache.Dispose();

        Assert.Equal(TileCacheState.Missing, cache.Lookup(Key()).State);
        Assert.False(cache.Contains(Key()));
    }

    #endregion
}
