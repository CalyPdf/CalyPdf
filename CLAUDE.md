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

# Publish desktop (Native AOT; the project's AOT settings are keyed on the Portable configuration)
dotnet publish Caly.Desktop -r win-x64 -c Portable -f net10.0
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
| `Caly.3d/Caly.Scene3D.Avalonia` | `Scene3DControl`: draws a `Scene3D` scene (3D artwork) with OpenGL |
| `Caly.3d/Caly.Scene3D.Avalonia.Tests` | xUnit tests of the control's GPU-free parts |
| `Caly.3d/Scene3DViewer` | Host for the 3D control: viewer, benchmark, GPU self-test |
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

Pages are tiled: PdfPig renders a page to a cached `SKPicture` (`PdfPageService`), `TileRenderService` rasterises it into 512 px tiles on background threads, `TileCache` (one per document, LRU, 128 MB soft budget) holds them, and `TiledPdfPageControl` composes them each frame. Desktop renders on the GPU; `"UseSoftwareRendering": true` in `%LOCALAPPDATA%\Caly\caly_settings` forces software.

- `PdfPageService` takes one request at a time, in priority order (PdfPig reads one page at a time per document). Do not run requests in parallel again.
- Tile rasterisation stays on the CPU: a GPU surface needs the render-thread-owned `GRContext`.
- `GpuResourceBudgetBytes` (`Caly.Desktop/Program.cs`) applies to the whole process. Never tie it to `TileCache`'s per-document budget.

Details (cache sizing, single-page view, prefetch, GPU memory, render timings): `architecture/rendering.md`.

### Zoom and fit modes

`DocumentViewModel.ZoomMode` holds the mode. `PageItemsControl.ZoomFit.cs` applies it, because only the control knows the viewer's size; the maths is in pure `ZoomFit`. A fit mode stays on until the user zooms by hand. Fits run from `LayoutUpdated`, never from a `Loaded` job, so the new zoom shows in the same frame. Details (margins, triggers, timing, landing, content box): `architecture/zoom-and-fit.md`.

### 3D Artwork (U3D / PRC)

Work in progress: a click on U3D or PRC artwork shows the model in place on its page. Before any 3D work, use the `maintaining-3d-artwork` skill. How it works: `architecture/3d-artwork.md`. Workspace outside the repo: `C:\caly-work\3d\claude`.

- Dependencies are NuGet packages. A change to PdfPig or Scene3D is made in its own repository and reaches Caly by bumping the version in every project that references it. All four Scene3D referencers (`Caly.Pdf`, `Caly.Scene3D.Avalonia`, `Caly.Scene3D.Avalonia.Tests`, `Caly.3d/Scene3DViewer`) stay on the same version.
- The control library is `Caly.Scene3D.Avalonia`, never `Caly.Avalonia.*`: a `Caly.Avalonia` namespace makes the simple name `Avalonia` mean it inside every `Caly.*` namespace.

Decisions that constrain future work: PRC's compressed tessellation (`PRC_TYPE_TESS_3D_Compressed`) comes after plain tessellation, as a phase of its own, and the text of US patent 8,207,965 is **never** used as a source for it (nor passages elsewhere that argue from it); nanoPRC (AGPL) is never to be opened; the PDF Association's PRC issues are preferred over the 2009 draft where they differ, and their decoding pseudo-code (issue #727) may be read and implemented from, but lines naming nanoPRC or the patent stay filtered out (`research/read-issue.py`) and links to either are never followed; scripts are not executed (Jint is the designated engine if that ever changes, ask first); new 3D libraries target .NET 8+ and are written for speed and low allocation; commits stay local unless told otherwise.

### AOT Compatibility

The desktop project supports Native AOT. Key constraints enforced in `Caly.Desktop.csproj`:
- `JsonSerializerIsReflectionEnabledByDefault` = false — use source-generated JSON serialization
- `AvaloniaUseCompiledBindingsByDefault` = true in `Caly.Core` — avoid reflection-based bindings
- All assemblies are strong-name signed via `Caly.snk`

### Multi-targeting

All projects target `net10.0`. Mobile heads use `net10.0-android36.0` / `net10.0-ios`. There is no net9.0 target.
