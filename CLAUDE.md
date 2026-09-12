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

Rendering mode is **GPU** on desktop — Avalonia's platform default, ANGLE/D3D on Windows (`Caly.Desktop/Program.cs`). Set `CALY_RENDER_MODE=software` to force the software renderer; that is the escape hatch for broken drivers. Android is still software; iOS uses the platform default.

Tile *rasterisation* is CPU-side regardless and must stay that way — a GPU surface needs a `GRContext`, which is render-thread-owned and not thread-safe. Only the per-frame tile composition is GPU-accelerated.

`SkiaOptions.MaxGpuResourceSizeBytes` is raised from Avalonia's 28 MiB default via `GpuResourceBudgetBytes` in `Caly.Desktop/Program.cs`. That budget is **process-wide** (Avalonia creates one `Compositor`, hence one `GRContext`, per process regardless of window count), so it is sized against the tiles visible at once across all visible windows — deliberately independent of `TileCache`'s per-document budget, which they must not be tied to.

Set `CALY_RENDER_TIMING=1` to dump render-path timings (back-end actually in use, draw-op count, mean/max draw time) to `%LOCALAPPDATA%\Caly\logs` at exit — see `Caly.Core/Services/Rendering/RenderTimings.cs`.

### AOT Compatibility

The desktop project supports Native AOT. Key constraints enforced in `Caly.Desktop.csproj`:
- `JsonSerializerIsReflectionEnabledByDefault` = false — use source-generated JSON serialization
- `AvaloniaUseCompiledBindingsByDefault` = true in `Caly.Core` — avoid reflection-based bindings
- All assemblies are strong-name signed via `Caly.snk`

### Multi-targeting

All projects target `net10.0`. Mobile heads use `net10.0-android36.0` / `net10.0-ios`. There is no net9.0 target.
