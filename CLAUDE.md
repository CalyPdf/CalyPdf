# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Caly is a cross-platform PDF reader built with Avalonia UI and .NET. It targets Windows, Linux, macOS, Android, and iOS. The master branch uses NuGet packages; the develop branch uses git submodules in `external/` (run `git submodule update --init --recursive` after cloning).

## Build & Test Commands

```bash
# Build
dotnet build -c Debug
dotnet build -c Release

# Run tests
dotnet test Caly.Tests
dotnet test Caly.Tests -v detailed

# Run a single test class
dotnet test Caly.Tests --filter "FullyQualifiedName~SortedObservableCollectionTests"

# Run the printing tests (spooler tests skip unless CALY_TEST_PRINTER names a test queue)
dotnet test Caly.Printing/Caly.Printing.Tests

# Run benchmarks (Release required)
dotnet run --project Caly.Benchmarks -c Release

# Publish desktop (Release)
dotnet publish Caly.Desktop -r win-x64 -c Release -f net10.0

# Publish desktop (Native AOT)
dotnet publish Caly.Desktop -r win-x64 -c AOT -f net10.0
```

## Architecture

### Solution Structure

| Project | Purpose |
|---|---|
| `Caly.Core` | Shared UI, ViewModels, Services (Avalonia, MVVM) |
| `Caly.Desktop` | Windows/Linux/macOS entry point; single-instance logic |
| `Caly.Android` / `Caly.iOS` | Mobile platform heads |
| `Caly.Pdf` | PDF parsing, text extraction, layout analysis via PdfPig |
| `Caly.Tests` | xUnit unit tests |
| `Caly.Printing.Tests` | xUnit tests for `Caly.Printing.*`: unit tests, plus tests that print to a real spooler (Windows spooler, CUPS). The spooler tests run only in the `Printing` GitHub Actions workflow, which provisions the queue named by `CALY_TEST_PRINTER`; locally they skip, on CI a missing queue fails them |
| `Caly.Benchmarks` | BenchmarkDotNet performance benchmarks |

### Layered Architecture

```
Views / Controls (Avalonia XAML)
    ↕ data binding
ViewModels (CommunityToolkit.Mvvm)
    ↕ DI interfaces
Services (Microsoft.Extensions.DependencyInjection)
    ↕
Caly.Pdf (PdfPig + PdfPig.Rendering.Skia → SKPicture)
```

### Key Services

- **`IPdfDocumentsManagerService`** — manages the collection of open documents and handles file loading
- **`IPdfDocumentService`** — per-document operations: pagination, rendering, text extraction
- **`ITextSearchService`** — full-text search using `SearchValues` for performance
- **`ISettingsService`** — JSON-backed persistent settings (`CalySettings`)
- **`IDialogService`** — UI dialogs and toast notifications
- **`IFilesService`** — file open/save operations

### ViewModel Hierarchy

`MainViewModel` → `DocumentViewModel` (partial, split across 8 files by concern: TextSearch, Bookmarks, Zoom, Clipboard, Properties, Disposable) → `PageViewModel`

Inter-component communication uses `CommunityToolkit.Mvvm`'s `StrongReferenceMessenger` with custom message types in `Caly.Core/Services/Messages.cs`.

### Single-Instance Pattern

`Caly.Desktop/Program.cs` enforces one app instance via `FileMutex`. Subsequent launches pass their file argument to the running instance via a named pipe (`FilePipeStream`).

### Rendering Pipeline

Pages are **tiled**, not drawn as vector pictures per frame:

1. `PdfPig.Rendering.Skia` renders a page to an `SKPicture`, cached per page by `PdfPageService` (`_cachePictures`, ref-counted via `IRef<SKPicture>`). PdfPig reads one page at a time per document (a FIFO `SemaphoreSlim` in `PdfPigDocumentService`), so `PdfPageService`'s request queue takes **one request at a time, in priority order**, and hands the work that needs no PdfPig (text layout, drawing thumbnails) to the thread pool. Do not run requests in parallel again: requests in flight together queue for the lock first come, first served, and a page coming into view waited behind up to 3 thumbnail or neighbour parses (seconds on JPEG 2000 pages). `IPdfDocumentService.GetPageTextLayerAsync` therefore returns the raw `PageTextLayerContent`; `PdfPageService` builds the `PdfTextLayer`.
2. `TileRenderService` (`Caly.Core/Services/Rendering/`) rasterises regions of that picture into **512 px bitmap tiles** on background threads, via its own CPU `SKSurface`. Requests are prioritised so tiles nearest the viewport centre render first. Tiles that render to nothing are recorded as bare keys ("blank"), never as images.
3. `TileCache` holds the results — thread-safe LRU, 128 MB budget by default (`CalySettings.TileCacheSizeMB`, restricted to the presets in `TileCacheSizeOptionsMB` — any other value in the file is reset to the default on load — settings Rendering tab drop-down, read once per document when its `PdfPageService` is created), ref-counted `IRef<TileImage>` so a tile cannot be freed while a render pass still draws it. The budget is **soft for on-screen tiles**: eviction skips any tile whose ref count shows a draw operation still holds it (evicting it frees nothing and would leave a hole), so when the visible tiles alone exceed 128 MB — a large screen at high zoom — the cache grows past the budget and shrinks back as tiles leave the screen. `TiledPdfPageControl.Render` also re-requests any missing exact-level tile, because tiles are otherwise only requested when the visible tile range changes: a tile evicted before its first draw would stay a hole forever. There is **one cache per open document** (`PdfPageService` is DI-scoped, and `PdfDocumentsManagerService` creates one scope per document), so host-side tile memory scales with the number of *active* documents: `PdfPageService.CancelAndClear()` (run when a document is deactivated) calls `TileRenderService.Clear()`, which cancels pending requests and empties the cache. Page detach (`CancelPage`) alone only cancels in-flight requests and keeps the cached tiles. 128 MB was chosen empirically: a realistic reading-session workload (forward scroll, a near-range back-and-forth revisit, a 100%-600% zoom cycle, more forward scroll) measured a zero-wasted-re-render floor of 48 MB and a worst-case peak of 72 MB — the LRU evicts precisely enough that budgets well below the true peak still cost no extra re-renders, so 128 MB carries a ~77% margin over the measured peak, not a razor-thin one. Those figures predate tiles outliving their picture (below): the cache now fills to its budget over a session, since it is what holds recently read pages.

   **Tiles outlive their page's picture.** Evicting a picture (realised ±1 page in continuous view) keeps the page's tiles, and `TiledPdfPageControl` draws cached tiles with no picture, so scrolling back shows the page at once while it is parsed again; `PageViewModel.IsPageLoading` stays false while the page has image tiles (`TileCache.HasImages`), so the skeleton does not show through blank tiles. This is the retention that has a real byte budget: a picture's actual memory is unknown to Caly (`ApproximateBytesUsed` ignores decoded images) and was measured at 0.8–2 MB a page for text, 8 MB image-heavy, 23 MB for a map, 273 MB on `MOZILLA-11518-0.pdf` and 373 MB on a large map, so pictures are not kept beyond ±1. A fresh render whose `CullRect` and operation count differ from the page's previous picture (`_pictureFingerprints`) drops the page's tiles: a timed-out render's "could not display" picture followed by a successful one would otherwise keep showing the old message.
4. `TiledPdfPageControl` composes the visible tiles each frame. Tile level is `ceil(log2(zoom))`, so a missing tile falls back to a coarser cached tile (upscaled sub-rect) or a block of finer ones — zoom transitions never go blank. Tiles are sampled **bilinear, without mipmaps**, except that the **software renderer uses nearest-pixel while the scene moves** (`GetSamplingOptions`, chosen in the draw operation, the only place that knows the back end). Measured on `algo.pdf` (`docs/sampling-bench`, gitignored): on the CPU bilinear costs ~4× nearest per scaled 512 px tile (1.19 vs 0.29 ms at 1.5×; equal at exactly 1:1); on the GPU the two differ by 0–0.2 ms per draw. Mipmaps added nothing at the scales tiles are drawn at, except harm on the GPU: tiles shrunk below ~0.71× (a 100% display at 125% zoom) got mipmapped texture copies, +53 MB GPU cache (+39%) and a 2.7× slower first frame after scrolling stops.

**Single-page view** (`PageDisplayMode.SinglePage`, per document, toolbar toggle) swaps `PageItemsControl`'s items panel for `SinglePageVirtualizingPanel`, which realises one page and is deliberately not `ILogicalScrollable`, so zoom/pan inside the page are unchanged. Because nothing off the current page is ever visible, it keeps a ±3-page picture/text-layer window (`SinglePageCacheBuffer`) instead of ±1, so turning back within 3 pages needs no new parse. Mind the measured picture sizes above: 7 pictures of `MOZILLA-11518-0.pdf` come to about 1.9 GB.

**Neighbour prefetch** (both views): on every visible-range change `PdfPageService.RefreshPages` queues the pictures of the page just before and just after the visible range (`RenderRequestTypes.PrefetchPicture`, below text layers and above thumbnails). Without it a page's parse only starts once the page is on screen, and parsing dominates: ~2–7 ms for plain text but 70–430 ms for image-heavy or scanned pages and ~2 s for JPEG 2000 pages, against 2–9 ms for the page's tiles (AOT, 16 tile workers). Prefetches live in their own cancellation generation so the scroll or turn that brings the page into view does not cancel them, and are skipped at dequeue once their page has left the picture keep window. Tiles for neighbouring pages are **not** prefetched.

Thumbnails are separate: `PdfPageService.SetThumbnail` draws the `SKPicture` into its own small CPU surface and copies out to a `WriteableBitmap`.

Rendering mode is **GPU** on desktop — Avalonia's platform default, ANGLE/D3D on Windows (`Caly.Desktop/Program.cs`). Set `"UseSoftwareRendering": true` in the settings file (`%LOCALAPPDATA%\Caly\caly_settings`) to force the software renderer; an absent key means GPU. That is the escape hatch for broken drivers. Android is still software; iOS uses the platform default.

The renderer is chosen while the `AppBuilder` is still being built, before any window and therefore before `ISettingsService` can exist, so that one key is read directly from the JSON by `JsonSettingsService.TryReadBooleanSetting` — a `Utf8JsonReader` scan over a pooled buffer that stops at the first match and never materialises `CalySettings`. It is also a property on `CalySettings` so that saving settings preserves it (`Save()` rewrites the whole file).

Tile *rasterisation* is CPU-side regardless and must stay that way — a GPU surface needs a `GRContext`, which is render-thread-owned and not thread-safe. Only the per-frame tile composition is GPU-accelerated.

GPU rendering costs roughly **+200 MB peak working set** over software on an integrated GPU (measured on `algo.pdf`, scroll/zoom): ~80 MB is a fixed ANGLE/driver floor, ~100 MB is Avalonia's full-window compositor layer (structural, not Caly-specific — only the software `FramebufferRenderTarget` skips it), and ~60 MB is actual page textures in Skia's `GRContext` resource cache, tracking visible tiles. Of that last ~60 MB, `CalySettings.UseCompactTileFormat` (opt-in, off by default) recovers roughly a third by rendering tiles as `Rgb565` (2 bytes/pixel, no alpha) instead of `Bgra8888` (4 bytes/pixel) — measured ~60 MB lower peak working set, max per-channel colour error 5/255 on a smooth-gradient test page (`P.pdf` in the PdfPig.Rendering.Skia submodule), untested against PDF content with real alpha transparency.

`SkiaOptions.MaxGpuResourceSizeBytes` is raised from Avalonia's 28 MiB default via `GpuResourceBudgetBytes` in `Caly.Desktop/Program.cs`. That budget is **process-wide** (Avalonia creates one `Compositor`, hence one `GRContext`, per process regardless of window count), so it is sized against the tiles visible at once across all visible windows — deliberately independent of `TileCache`'s per-document budget, which they must not be tied to.

Set `"Debug": { "LogRenderTimings": true }` in the settings file (or tick it in the settings Debug tab) to dump render-path timings (back-end actually in use, draw-op count, mean/max draw time) to `%LOCALAPPDATA%\Caly\logs` at exit — see `Caly.Core/Services/Rendering/RenderTimings.cs`. Like `UseSoftwareRendering` it is read straight from the JSON, and is latched at the first draw, so it applies from the next launch.

### Zoom and fit modes

`DocumentViewModel.ZoomMode` (`Manual`, `FitPage`, `FitWidth`, `FitContent`, `FitContentWidth`) is per document and bound two-way to `PageItemsControl.ZoomMode`. The view model only holds the mode. `PageItemsControl.ZoomFit.cs` computes and applies each fit, because only the control knows the viewer's size.

- **Sticky:** a fit mode stays on until a zoom is picked by hand. Zoom in/out and actual size (view model commands), and Ctrl+wheel and pinch (`ZoomPanController`), switch it back to `Manual`. Zoom changes made by a fit never touch the mode.
- **Choosing the active mode again** goes through `Manual` (`DocumentViewModel.SetZoomMode`), so it re-applies the mode and snaps the page back into view.
- **The maths** is in pure `ZoomFit`:
  - **Reference page:** the current page in continuous view, kept while scrolling so a page of another size never re-zooms. The page shown in single-page view.
  - **1 px of slack:** layout rounding at scale 1 is a bare `Math.Ceiling`, so an exact fit would round over the viewport and bring a scroll bar.
  - **10 px margin** (`ZoomFit.FitMargin`) on either side of what is fitted: the page in fit page and fit width, the content box in the content modes. Fit content also keeps it above and below the box; fit page keeps none there. Fit page shares fit width's side margins, so it never zooms past fit width. The margin also absorbs pages that end up a little wider than the fitted page, which brought the horizontal scroll bar with the slack alone: a slightly wider neighbour, or the pages panel rounding a fractional page width up to a whole pixel before the zoom scales it.
  - **Scroll bars:** the four bar states are tried in turn, and the first consistent one wins.
  - **Content cap:** content modes are capped at `max(800%, the matching page-mode zoom)`.
- **Triggers:** each requests one coalesced fit (see Timing):
  - the mode changing;
  - the `ScrollViewer`'s outer size changing;
  - an extent change (pages streaming in, a size arriving, rotation);
  - the end of a tab-switch restore;
  - `PageItem.ContentBoundsChanged`.
  A viewport-only change, such as a scroll bar toggling, never triggers. The computation uses the outer size, not the viewport, so a fit cannot chase its own scroll bars. Fits that nobody asked for act only when the zoom moves by more than 0.1%.
- **Timing:** the fit runs from `LayoutUpdated`, after the layout pass and before the frame is drawn: Avalonia's `MediaContext` runs the layout passes queued during its render callbacks before it commits, so the zoom change is laid out in the same frame. `RequestFit` invalidates the control's arrange so that a layout pass always follows a request. A job at `Loaded` priority would run after the render: the frame showing a resize or new pages would show them at the old zoom, with the horizontal bar on a shrinking window. While a zoom is in flight (`ZoomPanController.IsZooming`, cleared at `Loaded`), the fit waits for a `Loaded` job instead, as running it from layout passes would loop within the frame. A fit for a new viewer size is the exception: run from `LayoutUpdated`, whose pass has laid out the zoom in flight, it zooms at once (`TryZoomTo(..., afterLayout: true)`), so a window resize step that comes before the last step's zoom has settled is fitted in its frame too. A zoom never changes the viewer's size, so that cannot feed itself. Tests read the committed frame with `Compositor.RequestCompositionUpdate` (`PageItemsControlZoomFitMaintenanceTests.FirstFrameAfter`).
- **Tab switches:** a mode that arrives with a tab switch (`_pendingScrollToPage`) keeps the restored position unless the zoom has to change.
- **Fit zooms keep the content at the viewport's top edge in place** (`ZoomPanController.TryZoomTo(zoom, keepTopInPlace: true)`), unlike the zoom buttons, which keep the centre. Saved positions and reading anchor at the top: zooming in around the centre after opening, or after enlarging the window, pushed the top of page 1 out of view.
- **Landing:**
  - Fit page uses `GoToPage(page, 0)`, which lands on the page's top.
  - Fit content uses `GoToPage(..., PageOffsetSpace.UnrotatedPage)`, the `GoToWord` path, to centre the content box.
  - The width modes set only the horizontal offset. `GoToPage`'s `ScrollIntoView` would snap a partly visible page to its top or bottom.
  - Horizontally, fit page and the width modes put the page or its content box `FitMargin` from the viewport's left edge (`AlignWithFitMargin`). The offset is relative to the document, because an offset relative to the page stops at the page's edge. A page narrower than the viewport cannot scroll: the scroll viewer centres it.
  - In single-page view, `GoToPage` fits the target page before it lands (`FitBeforeLanding`).
- **The content box** is `PageViewModel.ContentBounds`, the page picture's `CullRect` × `PpiScale`. It is kept after the picture is evicted. Pages are recorded with an R-tree, so Skia trims the cull rect to what was *drawn*. Clips are ignored and strokes and text are bounded loosely. Scans, page-sized white rectangles, `DrawPaint` fills and soft masks (a page-sized `DstIn` draw) therefore give the whole page.
- **Opening zoom:** `CalySettings.DefaultZoomMode` (Settings > Appearance > Documents) is applied in `LoadDocumentCore`. It is `Manual` (100%) by default. Settings aren't loaded on mobile yet, so mobile always opens at 100%.

### AOT Compatibility

The desktop project supports Native AOT. Key constraints enforced in `Caly.Desktop.csproj`:
- `JsonSerializerIsReflectionEnabledByDefault` = false — use source-generated JSON serialization
- `AvaloniaUseCompiledBindingsByDefault` = true in `Caly.Core` — avoid reflection-based bindings
- All assemblies are strong-name signed via `Caly.snk`

### Multi-targeting

All projects target `net10.0`. Mobile heads use `net10.0-android36.0` / `net10.0-ios`. There is no net9.0 target.
