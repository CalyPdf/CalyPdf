using Avalonia;
using Caly.Core.Services.Rendering;

namespace Caly.Tests;

/// <summary>
/// Covers the tile grid geometry the fallback search depends on. The render pass substitutes a
/// coarser tile for a missing one (and, on zoom-out, a block of finer tiles), and short-circuits
/// the search when a covering coarse tile is blank. Both are only sound if the containment
/// relations asserted here hold.
/// </summary>
public class TileGridTests
{
    private static readonly Size PageSize = new(1234, 987);

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(1, 2.0)]
    [InlineData(3, 8.0)]
    [InlineData(-1, 0.5)]
    [InlineData(-4, 0.0625)]
    public void GetTileLevelScale_IsTwoToThePowerOfTheLevel(int level, double expected)
    {
        Assert.Equal(expected, TileGrid.GetTileLevelScale(level));
    }

    [Theory]
    [InlineData(1.0, 0)]
    [InlineData(2.0, 1)]
    [InlineData(1.5, 1)]
    [InlineData(4.0, 2)]
    [InlineData(0.5, -1)]
    [InlineData(0.3, -1)]
    public void ComputeTileLevel_PicksTheLevelThatCoversTheZoom(double zoom, int expected)
    {
        Assert.Equal(expected, TileGrid.ComputeTileLevel(zoom));
    }

    [Fact]
    public void ComputeTileLevel_ClampsToMinTileLevelAndHandlesNonPositiveZoom()
    {
        Assert.Equal(TileGrid.MinTileLevel, TileGrid.ComputeTileLevel(0.0001));
        Assert.Equal(0, TileGrid.ComputeTileLevel(0));
        Assert.Equal(0, TileGrid.ComputeTileLevel(-1));
    }

    [Fact]
    public void GetGridDimensions_CoversThePageAndIsNeverEmpty()
    {
        // 1234 x 987 at level 0 with 512px tiles -> 3 x 2.
        var dims = TileGrid.GetGridDimensions(PageSize, 0);
        Assert.Equal(new PixelSize(3, 2), dims);

        // One level finer doubles each axis' pixel count: 2468 x 1974 -> 5 x 4.
        Assert.Equal(new PixelSize(5, 4), TileGrid.GetGridDimensions(PageSize, 1));

        // A page smaller than a tile still has one tile.
        Assert.Equal(new PixelSize(1, 1), TileGrid.GetGridDimensions(new Size(10, 10), 0));
    }

    [Fact]
    public void GetTileDisplayRect_TilesAreContiguousAndClampedToThePage()
    {
        var first = TileGrid.GetTileDisplayRect(0, 0, 0, PageSize);
        var second = TileGrid.GetTileDisplayRect(1, 0, 0, PageSize);

        Assert.Equal(0, first.Left);
        Assert.Equal(TileGrid.TilePixelSize, first.Right);

        // Adjacent tiles share an exact edge - no gaps, no overlap.
        Assert.Equal(first.Right, second.Left);

        // The last column is clipped to the page rather than running past it.
        var last = TileGrid.GetTileDisplayRect(2, 0, 0, PageSize);
        Assert.Equal(PageSize.Width, last.Right);
    }

    [Fact]
    public void GetTileDisplayRect_TilesEntirelyOutsideThePageAreEmpty()
    {
        var beyond = TileGrid.GetTileDisplayRect(99, 99, 0, PageSize);

        Assert.Equal(0, beyond.Width);
        Assert.Equal(0, beyond.Height);
    }

    [Fact]
    public void CoarserTileAlwaysContainsTheFineTileItStandsInFor()
    {
        // TryGetFallbackTile substitutes the tile at (col >> d, row >> d) on level (L - d) for a
        // missing tile, and treats a blank one there as proof that the fine tile's area is empty.
        // Both steps require strict containment for every level pair and grid position.
        for (int level = TileGrid.MinTileLevel + 1; level <= 4; level++)
        {
            var dims = TileGrid.GetGridDimensions(PageSize, level);

            for (int d = 1; level - d >= TileGrid.MinTileLevel; d++)
            {
                int coarserLevel = level - d;

                for (int row = 0; row < dims.Height; row++)
                {
                    for (int col = 0; col < dims.Width; col++)
                    {
                        var fine = TileGrid.GetTileDisplayRect(col, row, level, PageSize);
                        if (fine.Width <= 0 || fine.Height <= 0)
                        {
                            continue;
                        }

                        var coarse = TileGrid.GetTileDisplayRect(col >> d, row >> d, coarserLevel, PageSize);

                        Assert.True(coarse.Contains(fine),
                            $"level {level} tile ({col},{row}) {fine} is not contained by level {coarserLevel} tile ({col >> d},{row >> d}) {coarse}");
                    }
                }
            }
        }
    }

    [Fact]
    public void FinerTileBlockAlwaysCoversTheCoarseTileItStandsInFor()
    {
        // AddHigherLevelFallbackTiles fills a missing tile from the multiplier x multiplier block
        // of finer tiles starting at (col * multiplier, row * multiplier). The union of that block
        // must cover the missing tile's area, or zoom-out would leave uncovered strips.
        for (int level = TileGrid.MinTileLevel; level <= 3; level++)
        {
            var dims = TileGrid.GetGridDimensions(PageSize, level);

            for (int d = 1; d <= 2; d++)
            {
                int finerLevel = level + d;
                int multiplier = 1 << d;
                var finerDims = TileGrid.GetGridDimensions(PageSize, finerLevel);

                for (int row = 0; row < dims.Height; row++)
                {
                    for (int col = 0; col < dims.Width; col++)
                    {
                        var coarse = TileGrid.GetTileDisplayRect(col, row, level, PageSize);
                        if (coarse.Width <= 0 || coarse.Height <= 0)
                        {
                            continue;
                        }

                        int startCol = col * multiplier;
                        int startRow = row * multiplier;
                        int endCol = Math.Min(startCol + multiplier, finerDims.Width);
                        int endRow = Math.Min(startRow + multiplier, finerDims.Height);

                        Rect? union = null;
                        for (int r = startRow; r < endRow; r++)
                        {
                            for (int c = startCol; c < endCol; c++)
                            {
                                var piece = TileGrid.GetTileDisplayRect(c, r, finerLevel, PageSize);
                                if (piece.Width <= 0 || piece.Height <= 0)
                                {
                                    continue;
                                }

                                union = union is null ? piece : union.Value.Union(piece);
                            }
                        }

                        Assert.NotNull(union);
                        Assert.True(union.Value.Contains(coarse),
                            $"level {level} tile ({col},{row}) {coarse} is not covered by its level {finerLevel} block {union.Value}");
                    }
                }
            }
        }
    }

    [Fact]
    public void CreateRenderMatrix_MapsATilesPageRegionOntoTheTileSurface()
    {
        const double ppiScale = 2.0;
        const int level = 1;

        var matrix = TileGrid.CreateRenderMatrix(col: 1, row: 0, ppiScale, level);

        // The tile's own region of the page must land at the surface origin...
        double tileSizeInPageUnits = TileGrid.TilePixelSize / (ppiScale * TileGrid.GetTileLevelScale(level));
        var mapped = matrix.MapPoint((float)tileSizeInPageUnits, 0);

        Assert.Equal(0, mapped.X, 3);
        Assert.Equal(0, mapped.Y, 3);

        // ...and span exactly one tile surface.
        var far = matrix.MapPoint((float)(tileSizeInPageUnits * 2), (float)tileSizeInPageUnits);
        Assert.Equal(TileGrid.TilePixelSize, far.X, 3);
        Assert.Equal(TileGrid.TilePixelSize, far.Y, 3);
    }
}
