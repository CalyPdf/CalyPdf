# Caly.Avalonia.Pdf.Rendering

Avalonia controls that render a recorded Skia `SKPicture` as a PDF page.

- **`SkiaPdfPageControl`** — draws an `SKPicture` directly onto the Avalonia Skia canvas (vector, sharp at any zoom).
- **`TiledPdfPageControl`** — draws pre-rendered bitmap tiles via a background `TileRenderService`, for smooth zoom/scroll on large pages.

You supply the `SKPicture` (e.g. via PdfPig.Rendering.Skia) wrapped with `PdfRef.Create(picture)`, plus the viewport (`VisibleArea`, `PpiScale`, and for tiling `ZoomLevel`/`PageNumber`/`PageDisplaySize`).

Set `PdfRenderDiagnostics.ExceptionLogger` to capture render-thread exceptions, or handle the `RenderFailed` event. Enable `ShowDiagnosticsOverlay` to draw tile/picture debug overlays in any build configuration.

Software rendering only (no GPU). Depends on Avalonia, Avalonia.Skia, SkiaSharp.
