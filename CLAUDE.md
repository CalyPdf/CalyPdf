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
| `Caly.Avalonia.Scene3D` | `Scene3DControl`: draws a `Scene3D` scene (3D artwork) with OpenGL |
| `Caly.Avalonia.Scene3D.Tests` | xUnit tests of the control's GPU-free parts |
| `tools/Scene3DViewer` | Host for the 3D control: viewer, benchmark, GPU self-test |
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

1. `PdfPig.Rendering.Skia` renders a page to an `SKPicture`, cached per page by `PdfPageService` (`_cachePictures`, ref-counted via `IRef<SKPicture>`).
2. `TileRenderService` (`Caly.Core/Services/Rendering/`) rasterises regions of that picture into **512 px bitmap tiles** on background threads, via its own CPU `SKSurface`. Requests are prioritised so tiles nearest the viewport centre render first. Tiles that render to nothing are recorded as bare keys ("blank"), never as images.
3. `TileCache` holds the results — thread-safe LRU, 128 MB budget by default (`CalySettings.TileCacheSizeMB`, restricted to the presets in `TileCacheSizeOptionsMB` — any other value in the file is reset to the default on load — settings Rendering tab drop-down, read once per document when its `PdfPageService` is created), ref-counted `IRef<TileImage>` so a tile cannot be freed while a render pass still draws it. The budget is **soft for on-screen tiles**: eviction skips any tile whose ref count shows a draw operation still holds it (evicting it frees nothing and would leave a hole), so when the visible tiles alone exceed 128 MB — a large screen at high zoom — the cache grows past the budget and shrinks back as tiles leave the screen. `TiledPdfPageControl.Render` also re-requests any missing exact-level tile, because tiles are otherwise only requested when the visible tile range changes: a tile evicted before its first draw would stay a hole forever. There is **one cache per open document** (`PdfPageService` is DI-scoped, and `PdfDocumentsManagerService` creates one scope per document), so host-side tile memory scales with the number of *active* documents: `PdfPageService.CancelAndClear()` (run when a document is deactivated) calls `TileRenderService.Clear()`, which cancels pending requests and empties the cache. Page detach (`CancelPage`) alone only cancels in-flight requests and keeps the cached tiles. 128 MB was chosen empirically: a realistic reading-session workload (forward scroll, a near-range back-and-forth revisit, a 100%-600% zoom cycle, more forward scroll) measured a zero-wasted-re-render floor of 48 MB and a worst-case peak of 72 MB — the LRU evicts precisely enough that budgets well below the true peak still cost no extra re-renders, so 128 MB carries a ~77% margin over the measured peak, not a razor-thin one.
4. `TiledPdfPageControl` composes the visible tiles each frame. Tile level is `ceil(log2(zoom))`, so a missing tile falls back to a coarser cached tile (upscaled sub-rect) or a block of finer ones — zoom transitions never go blank.

**Single-page view** (`PageDisplayMode.SinglePage`, per document, toolbar toggle) swaps `PageItemsControl`'s items panel for `SinglePageVirtualizingPanel`, which realises one page and is deliberately not `ILogicalScrollable`, so zoom/pan inside the page are unchanged. Because nothing off the current page is ever visible, `PdfPageService` prefetches the neighbouring pages' pictures (`RenderRequestTypes.PrefetchPicture`) and keeps a ±3-page picture/text-layer window (`SinglePageCacheBuffer`) instead of ±1. Picture eviction also invalidates that page's tiles, so this window is also what lets `TileCache` serve a turn back to a recently read page. Tiles for neighbouring pages are **not** prefetched.

Thumbnails are separate: `PdfPageService.SetThumbnail` draws the `SKPicture` into its own small CPU surface and copies out to a `WriteableBitmap`.

`SkiaPdfPageControl` is **legacy and unused** — no XAML references it. It drew the `SKPicture` directly per frame, which the tile pipeline replaced.

Rendering mode is **GPU** on desktop — Avalonia's platform default, ANGLE/D3D on Windows (`Caly.Desktop/Program.cs`). Set `"UseSoftwareRendering": true` in the settings file (`%LOCALAPPDATA%\Caly\caly_settings`) to force the software renderer; an absent key means GPU. That is the escape hatch for broken drivers. Android is still software; iOS uses the platform default.

The renderer is chosen while the `AppBuilder` is still being built, before any window and therefore before `ISettingsService` can exist, so that one key is read directly from the JSON by `JsonSettingsService.TryReadBooleanSetting` — a `Utf8JsonReader` scan over a pooled buffer that stops at the first match and never materialises `CalySettings`. It is also a property on `CalySettings` so that saving settings preserves it (`Save()` truncates and rewrites the whole file).

Tile *rasterisation* is CPU-side regardless and must stay that way — a GPU surface needs a `GRContext`, which is render-thread-owned and not thread-safe. Only the per-frame tile composition is GPU-accelerated.

GPU rendering costs roughly **+200 MB peak working set** over software on an integrated GPU (measured on `algo.pdf`, scroll/zoom): ~80 MB is a fixed ANGLE/driver floor, ~100 MB is Avalonia's full-window compositor layer (structural, not Caly-specific — only the software `FramebufferRenderTarget` skips it), and ~60 MB is actual page textures in Skia's `GRContext` resource cache, tracking visible tiles. Of that last ~60 MB, `CalySettings.UseCompactTileFormat` (opt-in, off by default) recovers roughly a third by rendering tiles as `Rgb565` (2 bytes/pixel, no alpha) instead of `Bgra8888` (4 bytes/pixel) — measured ~60 MB lower peak working set, max per-channel colour error 5/255 on a smooth-gradient test page (`P.pdf` in the PdfPig.Rendering.Skia submodule), untested against PDF content with real alpha transparency.

`SkiaOptions.MaxGpuResourceSizeBytes` is raised from Avalonia's 28 MiB default via `GpuResourceBudgetBytes` in `Caly.Desktop/Program.cs`. That budget is **process-wide** (Avalonia creates one `Compositor`, hence one `GRContext`, per process regardless of window count), so it is sized against the tiles visible at once across all visible windows — deliberately independent of `TileCache`'s per-document budget, which they must not be tied to.

Set `"Debug": { "LogRenderTimings": true }` in the settings file (or tick it in the settings Debug tab) to dump render-path timings (back-end actually in use, draw-op count, mean/max draw time) to `%LOCALAPPDATA%\Caly\logs` at exit — see `Caly.Core/Services/Rendering/RenderTimings.cs`. Like `UseSoftwareRendering` it is read straight from the JSON, and is latched at the first draw, so it applies from the next launch.

### 3D Artwork (U3D / PRC)

Work in progress on branches named `3d` (Caly, `external/PdfPig`, `external/PdfPig.Rendering.Skia`, `external/Scene3D`); planning documents, specs, plans and sample surveys live outside the repository in `C:\caly-work\3d\claude` (start at its `README.md`).

What exists today: **the app recognises 3D artwork; U3D can be decoded and drawn, but only in a separate viewer tool** — the control is not yet placed on Caly's pages.

In the app (recognition only):

- **PdfPig** (`UglyToad.PdfPig.Artwork`): `Artwork3D.TryCreate(annotation, scanner, filterProvider, out artwork)` is the single entry point. It reads a 3D annotation (ISO 32000-2 §13.6) or the 3D asset of a RichMedia annotation (§13.7) into a read-only model: format (`U3D`/`Prc`), `GetData()` (the payload bytes, decoded on each call), authored views (`Artwork3DView`: camera matrix or U3D path, projection, background, render mode, lighting scheme, cross sections, per-node overrides), default-view resolution (`3DV` → `DV` → first view), activation settings. Views are parsed lazily; malformed entries become "absent", never exceptions. Scripts are exposed as bytes and never run.
- **`Caly.Pdf`**: `TextLayerStreamProcessor.DrawAnnotations` calls it for annotations of type `Artwork3D`/`RichMedia` only, producing `PdfArtwork3D` items carried by `PageTextLayerContent.Artworks3D` → `PdfTextLayer.Artworks3D` (`null` when the page has none, so pages without 3D pay nothing). `PdfTextLayerHelper.GetTextLayer` must not return `PdfTextLayer.Empty` when artworks exist.
- **`Caly.Core`**: `PageInteractiveLayerControl` draws a "3D" badge at the artwork's top-left corner (constant on-screen size: drawn at the inverse of the page zoom) and, on hover, a flyout with the format and authored view names (`TextSelectionInputHandler.HandleMouseMoveOver` checks artworks before annotations).

`HasNormalAppearance` is `false` for a quarter of real-world 3D annotations (no `AP` at all, or an empty one): the page shows nothing there and only the badge marks the artwork. Where a poster exists it need not picture the model: SimLab's are the view's background colour and nothing else.

Decoding and drawing (not yet wired into the app):

- **`external/Scene3D`** (a stand-alone local repository, net8.0, BCL only, not yet a submodule — the `3d` branch builds only where that folder exists): `Scene3D` is the format-neutral scene (flat pooled arrays, unified vertices, `SceneView` for a way of looking at it, a GLB writer); `Scene3D.U3D` has `U3DReader.Read(payload)`. Only the *base* level of U3D meshes is decoded; every real file seen keeps all its geometry there. Its README describes it as built.
- **`Caly.Pdf/Artwork/Artwork3DViewConverter`**: PdfPig `Artwork3DView` → `SceneView`. It reads lazily parsed parts of the view, so call it while the page is processed, under the document lock. A PDF camera looks along +Z with +X to the viewer's *left* (the files are right-handed whatever ISO 32000 says); the converter negates X and Z. This is asserted on 88 real views that give their camera both ways (`Artwork3DCameraConventionTests`, needs `CALY_3D_SAMPLES`).
- **`Caly.Avalonia.Scene3D`**: `Scene3DControl`, an `OpenGlControlBase` that draws a `Scene` for a `SceneView` with orbit/pan/zoom. See its README, in particular "Things a host must know": GPU only (reports `Unavailable` under the software renderer), the scene's geometry moves to the GPU and the control reports `NeedsScene` after it left the visual tree, and `VisibleRegion` is how a zoomed page keeps the framebuffer no larger than the window. Everything with logic is GPU-free and unit-tested (`Caly.Avalonia.Scene3D.Tests`); the GL code is checked by `tools/Scene3DViewer --selftest` on a real GPU, because there is no headless OpenGL.
- **`tools/Scene3DViewer`**: opens a `.u3d` or a PDF's 3D artwork; view list, poster beside the live view, page-zoom emulation, `--benchmark`, `--capture`, `--selftest`. Native AOT: `dotnet publish tools/Scene3DViewer -r win-x64 -c Release -p:ViewerAot=true` (not `-p:PublishAot=true`, which reaches the netstandard2.0 projects it references).

Inside any namespace starting with `Caly.`, the simple name `Avalonia` now binds to the namespace `Caly.Avalonia` wherever `Caly.Avalonia.Scene3D` is referenced: write `global::Avalonia.X` or rely on `using` directives.

Decisions that constrain future work: PRC's compressed tessellation (`PRC_TYPE_TESS_3D_Compressed`) is **not** to be implemented (patent); nanoPRC (AGPL) is never to be opened; scripts are not executed (Jint is the designated engine if that ever changes, ask first); new 3D libraries target .NET 8+ and are written for speed and low allocation; commits stay local unless told otherwise.

### AOT Compatibility

The desktop project supports Native AOT. Key constraints enforced in `Caly.Desktop.csproj`:
- `JsonSerializerIsReflectionEnabledByDefault` = false — use source-generated JSON serialization
- `AvaloniaUseCompiledBindingsByDefault` = true in `Caly.Core` — avoid reflection-based bindings
- All assemblies are strong-name signed via `Caly.snk`

### Multi-targeting

All projects target `net10.0`. Mobile heads use `net10.0-android36.0` / `net10.0-ios`. There is no net9.0 target.
