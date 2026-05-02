
using System;
using Avalonia;
using SkiaSharp;

namespace Caly.Core.Services.Rendering;

/// <summary>
/// A (column, row) coordinate in the tile grid.
/// </summary>
public readonly record struct TileCoord(int Column, int Row);

/// <summary>
/// Identifies a single tile in the tile grid for a given page and zoom level.
/// </summary>
public readonly record struct TileKey(int PageNumber, int TileLevel, int Column, int Row);

/// <summary>
/// Computes tile layout geometry for a page at a given tile level.
/// </summary>
public static class TileGrid
{
    /// <summary>
    /// The size of each tile in pixels (at the tile level's resolution).
    /// </summary>
    public const int TilePixelSize = 256 * 2;
    
    /// <summary>
    /// Hard floor on the tile level. Below this, the page shrinks into sub-pixel territory,
    /// <see cref="TileRenderService"/> would render a zero-sized surface, and tiles stop
    /// being useful. Also serves as the floor for coarser-tile fallback lookups during
    /// rapid zoom-out (see <c>TryGetFallbackTile</c> in <c>TiledPdfPageControl</c>).
    /// </summary>
    public const int MinTileLevel = -4;

    /// <summary>
    /// Computes the tile level for a given zoom level.
    /// <para>
    /// Tile level is <c>ceil(log2(zoomLevel))</c>, so tiles are rendered at or above the
    /// needed display resolution in both directions:
    /// </para>
    /// <list type="bullet">
    /// <item><description>zoom &gt; 1 (zoom-in) → positive levels, finer tiles.</description></item>
    /// <item><description>zoom == 1 → level 0 (1:1 with <c>ppiScale</c>).</description></item>
    /// <item><description>zoom &lt; 1 (zoom-out) → negative levels, coarser tiles (each tile covers a larger slice of the page at lower pixel density), so overview views cache ~4× fewer bytes per level of zoom-out.</description></item>
    /// </list>
    /// Clamped to <see cref="MinTileLevel"/> to avoid degenerate sub-pixel renders.
    /// </summary>
    public static int ComputeTileLevel(double zoomLevel)
    {
        if (zoomLevel <= 0)
        {
            return 0;
        }

        int level = (int)Math.Ceiling(Math.Log2(zoomLevel));
        return Math.Max(MinTileLevel, level);
    }

    /// <summary>
    /// Gets the scale factor for a given tile level: 2^tileLevel. Supports negative levels
    /// (fractional scale, for zoom-out).
    /// </summary>
    public static double GetTileLevelScale(int tileLevel)
    {
        // 2 ^ tileLevel
        return tileLevel >= 0
            ? 1L << tileLevel
            : 1.0 / (1L << -tileLevel);
    }

    /// <summary>
    /// Gets the number of tile columns and rows for a page at a given tile level.
    /// </summary>
    /// <param name="pageDisplaySize">Page size in display coordinates (already scaled by ppiScale).</param>
    /// <param name="tileLevel">The tile level.</param>
    /// <returns>Number of columns (width) and rows (height) in the tile grid.</returns>
    public static PixelSize GetGridDimensions(in Size pageDisplaySize, int tileLevel)
    {
        double tileScale = GetTileLevelScale(tileLevel);

        // We use a long for crazy dimensions and avoid int overflowing.
        // The final render will still be wrong.
        long pixelWidth = Math.Min(int.MaxValue, (long)Math.Ceiling(pageDisplaySize.Width * tileScale));
        long pixelHeight = Math.Min(int.MaxValue, (long)Math.Ceiling(pageDisplaySize.Height * tileScale));

        int columns = (int)((pixelWidth + TilePixelSize - 1) / TilePixelSize);
        int rows = (int)((pixelHeight + TilePixelSize - 1) / TilePixelSize);

        return new PixelSize(Math.Max(1, columns), Math.Max(1, rows));
    }

    /// <summary>
    /// Gets the rectangle in display coordinates that a tile covers,
    /// clamped to the page bounds for edge tiles.
    /// Adjacent tiles share exact edge coordinates (same expression for right/left),
    /// so seam prevention is handled at draw time by disabling edge anti-aliasing.
    /// </summary>
    public static Rect GetTileDisplayRect(int col, int row, int tileLevel, in Size pageDisplaySize)
    {
        double invScale = 1.0 / GetTileLevelScale(tileLevel);
        double tileDisplaySize = TilePixelSize * invScale;

        double left = col * tileDisplaySize;
        double top = row * tileDisplaySize;
        double right = Math.Min((col + 1) * tileDisplaySize, pageDisplaySize.Width);
        double bottom = Math.Min((row + 1) * tileDisplaySize, pageDisplaySize.Height);

        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>
    /// Creates the SKMatrix to render a tile from an SKPicture.
    /// The matrix translates and scales so that the tile's region of the page
    /// maps to a (0, 0, TilePixelSize, TilePixelSize) output surface.
    /// </summary>
    /// <param name="col">Tile column.</param>
    /// <param name="row">Tile row.</param>
    /// <param name="ppiScale">PPI scale factor (e.g. 2.0).</param>
    /// <param name="tileLevel">The tile level.</param>
    /// <returns>Matrix to apply before drawing the SKPicture onto a tile-sized surface.</returns>
    public static SKMatrix CreateRenderMatrix(int col, int row, double ppiScale, int tileLevel)
    {
        float renderScale = (float)(ppiScale * GetTileLevelScale(tileLevel));

        // For a PDF point (x, y) we need:
        //   surfaceX = x * renderScale - col * TilePixelSize
        //   surfaceY = y * renderScale - row * TilePixelSize
        //
        // This is a scale followed by a translation, expressed directly
        // to avoid ambiguity with SKMatrix.Concat argument order.
        return new SKMatrix(
            renderScale, 0, -col * TilePixelSize,
            0, renderScale, -row * TilePixelSize,
            0, 0, 1);
    }
}
