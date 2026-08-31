using Avalonia;
using Avalonia.Skia;
using Caly.Core.Controls.Rendering;
using Caly.Core.Services.Rendering;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// Covers what the render pass decides to draw: <see cref="TiledPdfPageControl.ComposeTileDrawEntries"/>
/// turns a tile range plus cache contents into the ordered list of draws, choosing between an
/// exact-level tile, nothing at all (blank), a coarser tile upscaled, or a block of finer tiles.
/// </summary>
public class TileDrawCompositionTests
{
    /// <summary>1024 x 1024 gives a 2 x 2 grid at level 0, 4 x 4 at level 1 and 8 x 8 at level 2.</summary>
    private static readonly Size PageSize = new(1024, 1024);

    /// <summary>Full-size tile images, so the fallback sub-rect maths is exercised for real.</summary>
    private static TileImage CreateTile()
    {
        var info = new SKImageInfo(TileGrid.TilePixelSize, TileGrid.TilePixelSize,
            SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Red);
        return new TileImage(surface.Snapshot());
    }

    private static SKRect DisplayRect(int col, int row, int level)
        => TileGrid.GetTileDisplayRect(col, row, level, PageSize).ToSKRect();

    private static List<TiledPdfPageControl.TileDrawEntry> NewEntries() => [];

    private static void DisposeEntries(List<TiledPdfPageControl.TileDrawEntry> entries)
    {
        foreach (var entry in entries)
        {
            entry.Dispose();
        }
    }

    [Fact]
    public void ExactLevelTile_IsDrawnWholeOntoItsOwnDisplayRect()
    {
        using var cache = new TileCache();
        cache.Add(new TileKey(1, 0, 0, 0), CreateTile());

        var entries = NewEntries();
        bool allCached = TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 0, 0, 0, 0, 0, PageSize, entries);

        Assert.True(allCached);
        var entry = Assert.Single(entries);
        Assert.Equal(new SKRect(0, 0, TileGrid.TilePixelSize, TileGrid.TilePixelSize), entry.SrcRect);
        Assert.Equal(DisplayRect(0, 0, 0), entry.DestRect);

        DisposeEntries(entries);
    }

    [Fact]
    public void BlankExactLevelTile_ProducesNoDrawButStillCountsAsCached()
    {
        using var cache = new TileCache();
        cache.AddBlank(new TileKey(1, 0, 0, 0));

        var entries = NewEntries();
        bool allCached = TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 0, 0, 0, 0, 0, PageSize, entries);

        Assert.Empty(entries);

        // Nothing is outstanding, so stale levels are safe to evict.
        Assert.True(allCached);
    }

    [Fact]
    public void BlankExactLevelTile_SuppressesTheFallbackSearchEntirely()
    {
        // A coarser tile with content exists, but the exact-level tile is known blank: that is
        // authoritative for this area, so drawing the coarser tile would show stale pixels.
        using var cache = new TileCache();
        cache.Add(new TileKey(1, 0, 0, 0), CreateTile());
        cache.AddBlank(new TileKey(1, 1, 0, 0));

        var entries = NewEntries();
        TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 1, 0, 0, 0, 0, PageSize, entries);

        Assert.Empty(entries);
    }

    [Fact]
    public void MissingTile_FallsBackToTheCoarserTilesMatchingSubRegion()
    {
        using var cache = new TileCache();
        cache.Add(new TileKey(1, 0, 0, 0), CreateTile());

        // Level-1 tile (1,1) is missing; level-0 tile (0,0) covers it.
        var entries = NewEntries();
        bool allCached = TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 1, 1, 1, 1, 1, PageSize, entries);

        Assert.False(allCached);
        var entry = Assert.Single(entries);

        // Bottom-right quadrant of the coarse image, drawn over the missing tile's area.
        Assert.Equal(new SKRect(256, 256, 512, 512), entry.SrcRect);
        Assert.Equal(DisplayRect(1, 1, 1), entry.DestRect);

        DisposeEntries(entries);
    }

    [Fact]
    public void MissingTile_CoveredByABlankCoarserTile_DrawsNothing()
    {
        // The coarse tile covering this area rendered empty, so the fine tile's area is empty too:
        // no coarser and no finer tile can add pixels there.
        using var cache = new TileCache();
        cache.AddBlank(new TileKey(1, 0, 0, 0));

        // A finer level holds content that must NOT be consulted once the area is known blank.
        cache.Add(new TileKey(1, 2, 0, 0), CreateTile());

        var entries = NewEntries();
        bool allCached = TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 1, 0, 0, 0, 0, PageSize, entries);

        Assert.Empty(entries);
        Assert.False(allCached);
    }

    [Fact]
    public void MissingTile_FallsBackToTheBlockOfFinerTilesWhenNoCoarserTileExists()
    {
        using var cache = new TileCache();

        // Level-1 block covering the missing level-0 tile (0,0): cols 0..1, rows 0..1.
        for (int row = 0; row <= 1; row++)
        {
            for (int col = 0; col <= 1; col++)
            {
                cache.Add(new TileKey(1, 1, col, row), CreateTile());
            }
        }

        var entries = NewEntries();
        bool allCached = TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 0, 0, 0, 0, 0, PageSize, entries);

        Assert.False(allCached);
        Assert.Equal(4, entries.Count);

        // Each finer tile is drawn whole onto its own, smaller, display rect.
        Assert.Equal(
            [DisplayRect(0, 0, 1), DisplayRect(1, 0, 1), DisplayRect(0, 1, 1), DisplayRect(1, 1, 1)],
            entries.Select(e => e.DestRect));

        DisposeEntries(entries);
    }

    [Fact]
    public void BlankFinerTiles_DoNotStopTheSearchAtALevelThatContributesNothing()
    {
        // Regression: a blank tile at level 1 used to be added as a draw entry and mark the level
        // as "found", breaking the search before level 2 was consulted - so the area rendered
        // white even though level 2 held content covering it.
        using var cache = new TileCache();

        // The whole level-1 block covering the missing level-0 tile (0,0) is blank...
        for (int row = 0; row <= 1; row++)
        {
            for (int col = 0; col <= 1; col++)
            {
                cache.AddBlank(new TileKey(1, 1, col, row));
            }
        }

        // ...but level 1 still holds content elsewhere on the page, so it is a candidate level.
        cache.Add(new TileKey(1, 1, 3, 3), CreateTile());

        // Level 2 covers the missing tile's area in full: 4 x 4 tiles.
        for (int row = 0; row <= 3; row++)
        {
            for (int col = 0; col <= 3; col++)
            {
                cache.Add(new TileKey(1, 2, col, row), CreateTile());
            }
        }

        var entries = NewEntries();
        TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 0, 0, 0, 0, 0, PageSize, entries);

        // All 16 level-2 tiles are drawn; not one blank level-1 entry, and not an empty list.
        Assert.Equal(16, entries.Count);
        Assert.All(entries, e => Assert.Equal(128, e.DestRect.Width));

        DisposeEntries(entries);
    }

    [Fact]
    public void FinerFallback_StopsAtTheClosestLevelThatActuallyHasTiles()
    {
        using var cache = new TileCache();

        // Both level 1 and level 2 cover the missing level-0 tile; level 1 is closer.
        for (int row = 0; row <= 1; row++)
        {
            for (int col = 0; col <= 1; col++)
            {
                cache.Add(new TileKey(1, 1, col, row), CreateTile());
            }
        }

        for (int row = 0; row <= 3; row++)
        {
            for (int col = 0; col <= 3; col++)
            {
                cache.Add(new TileKey(1, 2, col, row), CreateTile());
            }
        }

        var entries = NewEntries();
        TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 0, 0, 0, 0, 0, PageSize, entries);

        Assert.Equal(4, entries.Count);
        Assert.All(entries, e => Assert.Equal(256, e.DestRect.Width));

        DisposeEntries(entries);
    }

    [Fact]
    public void CoarserFallbackIsPreferredOverFinerTiles()
    {
        using var cache = new TileCache();
        cache.Add(new TileKey(1, 0, 0, 0), CreateTile());

        for (int row = 0; row <= 1; row++)
        {
            for (int col = 0; col <= 1; col++)
            {
                cache.Add(new TileKey(1, 2, col, row), CreateTile());
            }
        }

        var entries = NewEntries();
        TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 1, 0, 0, 0, 0, PageSize, entries);

        // One upscaled coarse draw, not a patchwork of finer tiles.
        var entry = Assert.Single(entries);
        Assert.Equal(new SKRect(0, 0, 256, 256), entry.SrcRect);

        DisposeEntries(entries);
    }

    [Fact]
    public void MissingTileWithNothingCached_DrawsNothingAndReportsWorkOutstanding()
    {
        using var cache = new TileCache();

        var entries = NewEntries();
        bool allCached = TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 0, 0, 0, 0, 0, PageSize, entries);

        Assert.Empty(entries);
        Assert.False(allCached);
    }

    [Fact]
    public void EntriesAreProducedInRowMajorOrderAcrossTheRange()
    {
        using var cache = new TileCache();
        for (int row = 0; row <= 1; row++)
        {
            for (int col = 0; col <= 1; col++)
            {
                cache.Add(new TileKey(1, 0, col, row), CreateTile());
            }
        }

        var entries = NewEntries();
        bool allCached = TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 0, 0, 0, 1, 1, PageSize, entries);

        Assert.True(allCached);
        Assert.Equal(
            [DisplayRect(0, 0, 0), DisplayRect(1, 0, 0), DisplayRect(0, 1, 0), DisplayRect(1, 1, 0)],
            entries.Select(e => e.DestRect));

        DisposeEntries(entries);
    }

    [Fact]
    public void MixedRange_DrawsHits_SkipsBlanks_AndFallsBackForMisses()
    {
        using var cache = new TileCache();
        cache.Add(new TileKey(1, 1, 0, 0), CreateTile());   // exact hit
        cache.AddBlank(new TileKey(1, 1, 1, 0));            // blank: nothing to draw
        cache.Add(new TileKey(1, 0, 0, 0), CreateTile());   // coarse cover for the misses

        var entries = NewEntries();
        bool allCached = TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 1, 0, 0, 1, 1, PageSize, entries);

        // (0,0) hit + (1,0) blank + (0,1) and (1,1) falling back to the coarse tile.
        Assert.False(allCached);
        Assert.Equal(3, entries.Count);

        Assert.Equal(DisplayRect(0, 0, 1), entries[0].DestRect);
        Assert.Equal(DisplayRect(0, 1, 1), entries[1].DestRect);
        Assert.Equal(DisplayRect(1, 1, 1), entries[2].DestRect);

        // The two fallbacks read their own quadrants of the coarse image.
        Assert.Equal(new SKRect(0, 256, 256, 512), entries[1].SrcRect);
        Assert.Equal(new SKRect(256, 256, 512, 512), entries[2].SrcRect);

        DisposeEntries(entries);
    }

    [Fact]
    public void ComposingClearsAnyEntriesLeftFromAPreviousFrame()
    {
        using var cache = new TileCache();
        cache.Add(new TileKey(1, 0, 0, 0), CreateTile());

        var entries = NewEntries();
        TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 0, 0, 0, 0, 0, PageSize, entries);
        DisposeEntries(entries);

        TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 0, 0, 0, 0, 0, PageSize, entries);

        Assert.Single(entries);

        DisposeEntries(entries);
    }

    [Fact]
    public void EveryEntryOwnsALiveReferenceForTheWholeDraw()
    {
        using var cache = new TileCache();
        cache.Add(new TileKey(1, 0, 0, 0), CreateTile());

        var entries = NewEntries();
        TiledPdfPageControl.ComposeTileDrawEntries(cache, 1, 0, 0, 0, 0, 0, PageSize, entries);

        // Eviction while the frame is in flight must not invalidate what we are about to draw.
        cache.InvalidatePage(1);

        var entry = Assert.Single(entries);
        Assert.Equal(new SKRect(0, 0, TileGrid.TilePixelSize, TileGrid.TilePixelSize), entry.SrcRect);

        DisposeEntries(entries);
    }
}
