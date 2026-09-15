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
3. `TileCache` holds the results — thread-safe LRU, 256 MB budget, ref-counted `IRef<TileImage>` so a tile cannot be freed while a render pass still draws it. There is **one cache per open document** (`PdfPageService` is DI-scoped, and `PdfDocumentsManagerService` creates one scope per document), so host-side tile memory scales with the number of open documents.
4. `TiledPdfPageControl` composes the visible tiles each frame. Tile level is `ceil(log2(zoom))`, so a missing tile falls back to a coarser cached tile (upscaled sub-rect) or a block of finer ones — zoom transitions never go blank.

Thumbnails are separate: `PdfPageService.SetThumbnail` draws the `SKPicture` into its own small CPU surface and copies out to a `WriteableBitmap`.

`SkiaPdfPageControl` is **legacy and unused** — no XAML references it. It drew the `SKPicture` directly per frame, which the tile pipeline replaced.

Rendering mode is **GPU** on desktop — Avalonia's platform default, ANGLE/D3D on Windows (`Caly.Desktop/Program.cs`). Set `"UseSoftwareRendering": true` in the settings file (`%LOCALAPPDATA%\Caly\caly_settings`) to force the software renderer; an absent key means GPU. That is the escape hatch for broken drivers. Android is still software; iOS uses the platform default.

The renderer is chosen while the `AppBuilder` is still being built, before any window and therefore before `ISettingsService` can exist, so that one key is read directly from the JSON by `JsonSettingsService.TryReadBooleanSetting` — a `Utf8JsonReader` scan over a pooled buffer that stops at the first match and never materialises `CalySettings`. It is also a property on `CalySettings` so that saving settings preserves it (`Save()` truncates and rewrites the whole file).

Tile *rasterisation* is CPU-side regardless and must stay that way — a GPU surface needs a `GRContext`, which is render-thread-owned and not thread-safe. Only the per-frame tile composition is GPU-accelerated.

`SkiaOptions.MaxGpuResourceSizeBytes` is raised from Avalonia's 28 MiB default via `GpuResourceBudgetBytes` in `Caly.Desktop/Program.cs`. That budget is **process-wide** (Avalonia creates one `Compositor`, hence one `GRContext`, per process regardless of window count), so it is sized against the tiles visible at once across all visible windows — deliberately independent of `TileCache`'s per-document budget, which they must not be tied to.

Set `"Debug": { "LogRenderTimings": true }` in the settings file (or tick it in the settings Debug tab) to dump render-path timings (back-end actually in use, draw-op count, mean/max draw time) to `%LOCALAPPDATA%\Caly\logs` at exit — see `Caly.Core/Services/Rendering/RenderTimings.cs`. Like `UseSoftwareRendering` it is read straight from the JSON, and is latched at the first draw, so it applies from the next launch. It also reports memory: the OS peak working set (`Process.PeakWorkingSet64` — not `Environment.WorkingSet`, which is the *current* value and at `ProcessExit` is taken after the windows have closed), and peaks of private bytes, managed heap, total `TileCache` bytes across documents, and Skia's GPU resource cache (`GRContext.GetResourceCacheUsage`). A background thread samples these every 250 ms into `render_memory_*.csv` alongside the summary.

### GPU memory cost (measured 2026-09-15)

GPU rendering costs **~+200 MB peak working set and private bytes** over software. Measured on a hybrid laptop (Intel UHD iGPU + RTX 4070 laptop; ANGLE picks the iGPU), 3200×2000 at 150 %, maximised window, Release, `Caly.Tests/Documents/algo.pdf`, scripted scroll (45 × PageDown) and zoom (100 % ↔ 600 % × 3), 2–3 runs per config, run-to-run spread ±5 MB. Peak WS / peak private, MB:

| Config | Scroll | Zoom | Mean draw op |
|---|---|---|---|
| Software | 295 / 208 | 311 / 234 | 1.8–2.4 ms |
| ANGLE (default) | 506 / 442 | 544 / 472 | 0.38–0.40 ms |
| WGL (`Win32RenderingMode.Wgl`) | ~450 / ~275 | ~507 / ~333 | 0.23–0.27 ms |
| ANGLE on the dGPU (per-app GPU preference) | ~290 / ~335 | ~340 / ~395 | 0.36–0.38 ms |

**Where the ANGLE delta goes** (from address-space snapshots via `VirtualQueryEx`/`QueryWorkingSetEx`, and window-size experiments):

- **~80 MB fixed driver/device floor** (Intel D3D11 UMD, ANGLE, `D3DCompiler_47`) — present with no document and an 800×600 window.
- **~100 MB of full-window GPU buffers** — private bytes with no document fall from 222 MB maximised to 122 MB at 800×600 (software: 81 → 42). ANGLE holds ~5 driver allocations of 27–36 MB while scrolling; software holds exactly 2 framebuffer-sized ones (23.5 MB). Part of this is structural in Avalonia: `ServerCompositionTarget` renders into a **full-window offscreen layer** unless the target sets `RetainsPreviousFrameContents`, which only the software `FramebufferRenderTarget` does — Caly cannot turn it off.
- **~60 MB once a document is drawn** — matches Skia's GPU cache (65 MB scroll, 83 MB zoom). On an iGPU, GPU memory is system RAM, so textures show up as process private memory. On the dGPU they move to dedicated VRAM (163–181 MB) and working set is close to software, though commit stays high.
- **Startup spike**: ANGLE private peaks ~1.5 s after launch (e.g. 500 / 434 MB), then settles to ~455 / 388. `MainWindow` opens at its XAML size (250×150), is resized in `JsonSettingsService._window_Opened`, then a *posted* call maximises it, so GPU surfaces are allocated at three sizes.
- Host-side `TileCache` is **not** a factor: it holds only on-screen pages' tiles (peak ~5 MB scroll, ~67 MB zoom) because pages leaving view are invalidated.

**What does not help** (measured, rejected):

- Lowering `GpuResourceBudgetBytes` to 28 MB: no memory saved (Skia still holds ~61 MB of locked, on-screen resources), mean draw 3–5× slower.
- `SKMipmapMode.None` for idle sampling: no change.
- Periodic `GRContext.PurgeUnusedResources(2000)`: ≤12 MB off the zoom peak, max draw op 3.5 ms → 9–13 ms.
- `Win32CompositionMode.DirectComposition` / `RedirectionSurface`: no memory change.

**What does help** (measured, not implemented):

- **Applying saved size / `WindowState.Maximized` before the window is first shown** (e.g. right after `ISettingsService.Load()`, which runs before the lifetime shows `MainWindow`): scroll peak WS −40 MB (506 → 465), no change to the zoom peak (zooming becomes the peak), no draw-time cost. The restored (un-maximised) size must still be preserved — that is why the current code posts the maximise.
- **Preferring WGL** (`RenderingMode = [Wgl, AngleEgl, Software]`): peak WS −40 to −57 MB, private −140 to −170 MB, draw ~35 % faster; rendering verified identical by screenshot. WGL surfaces land in *mapped* GPU memory instead of private, which is why WS falls less than private. Risks: native OpenGL driver quality varies, Windows on ARM has no native GL (falls back), only `RedirectionSurface` composition, tested on one machine only.

Memory numbers are iGPU-specific; do not generalise them to discrete GPUs. The earlier claim in `docs/superpowers/plans/2026-09-11-gpu-rendering.md` that the extra ~140 MB is "from GPU textures" is wrong — it is mostly window-sized buffers and the driver floor.

**Measuring memory: pitfalls.** Script the workload (same document, window state and key sequence) and alternate configs between runs. Sample from process launch, not from the first draw, or the startup spike is missed. Compare **private bytes** alongside working set, since WGL and dGPU move memory between private, mapped and VRAM. Screenshot each run to confirm real content is on screen.

### AOT Compatibility

The desktop project supports Native AOT. Key constraints enforced in `Caly.Desktop.csproj`:
- `JsonSerializerIsReflectionEnabledByDefault` = false — use source-generated JSON serialization
- `AvaloniaUseCompiledBindingsByDefault` = true in `Caly.Core` — avoid reflection-based bindings
- All assemblies are strong-name signed via `Caly.snk`

### Multi-targeting

All projects target `net10.0`. Mobile heads use `net10.0-android36.0` / `net10.0-ios`. There is no net9.0 target.
