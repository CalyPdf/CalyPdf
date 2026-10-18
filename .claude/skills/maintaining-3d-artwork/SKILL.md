---
name: maintaining-3d-artwork
description: Use when touching anything about 3D artwork in PDFs in Caly — U3D, PRC, Scene3D, Scene3DControl, Artwork3D, Artwork3DViewModel, Scene3DViewer, PdfPig.Artwork, "3D badge", 3D annotation — or when resuming the 3D work after a break, before reading code or planning.
---

# Maintaining Caly's 3D artwork feature

Entry point. Read this first, then the skill that matches the task (table at the end).

## Where everything is

| What | Where |
|---|---|
| How it works (architecture, input, placement, failures) | "3D Artwork" section of Caly's `CLAUDE.md` — authoritative, keep it current |
| Decoders: scene model, U3D, PRC, GLB, `scene3d-tool` | NuGet packages `Caly.Rendering.Scene3D`, `Caly.Rendering.Scene3D.U3D` and `Caly.Rendering.Scene3D.Prc`, source `CalyPdf/Caly.Rendering.Scene3D` (branch `3d`); its `README.md` is the best description of the library as built |
| PDF recognition | `PdfPig` NuGet package (nightly `0.1.17-alpha-202610180035-9c18f` or later) → `UglyToad.PdfPig.Artwork` (`Artwork3D.TryCreate`) |
| PDF → scene glue | `Caly.Pdf/Artwork/` (`Artwork3DContentReader`, `Artwork3DSceneReader`, `Artwork3DViewConverter`) |
| GL control | `Caly.Scene3D.Avalonia` (+ `.Tests`, GPU-free parts only) |
| Page integration | `Caly.Core`: `Artwork3DViewModel`, `Artwork3DView`, `Artwork3DPlacementPanel`, `PageInteractiveLayerControl`, `TextSelectionInputHandler.TryActivateArtwork3D` |
| Host/benchmark/self-test | `tools/Scene3DViewer` |
| History, rulings, probes, surveys, specs | **Workspace `C:\caly-work\3d\claude`**: start at `README.md`, then the last entry of `JOURNAL.md`; `plans/*.progress.md` are ledgers of every ruling; `OPEN-QUESTIONS.md` is the owner's decisions log |
| Samples | `C:\caly-work\3d\samples` (+ `samples-u3d-db`, `samples-prc-db`, owner's problem files `samples_issues`) |

The workspace is outside git. If it is missing, the rulings are summarised in `CLAUDE.md`, the Scene3D README and the sibling skills; do not recreate it from guesses.

## Standing rules (owner's decisions)

- **Commits stay local** in Caly and in the PdfPig and Scene3D repositories (Scene3D's branch is `3d`). The owner pushes. Never push or open PRs unprompted. Stage explicit paths (the tree carries unrelated changes).
- **Dependencies are NuGet packages** (this branch is based on master; no submodules): a PdfPig or Scene3D change is made in its own repository and reaches Caly only through a new package version. Bump it in every referencing project together (Scene3D: `Caly.Pdf`, `Caly.Scene3D.Avalonia`, `Caly.Scene3D.Avalonia.Tests` and `tools/Scene3DViewer`; PdfPig: `Caly.Pdf`). A change Caly needs that is not in a published package yet: say so, do not switch to project references.
- **Every project Caly references by path must be in `Caly.slnx`** (owner builds in Visual Studio). See `verifying-3d-changes`.
- Libraries are performance-first, `net8.0`+ (spans, pooling, no hot-path allocations, BenchmarkDotNet in Release). Code inside PdfPig follows PdfPig's targets.
- Anything with PDF threading: PdfPig's scanner is not thread-safe; lazy PdfPig 3D members (`Views`, `DefaultView`, `GetData`) are touched only under the document lock, never on the UI thread.
- Scripts in 3D payloads are never executed. If that ever changes, the engine is Jint, and **ask the owner first**.
- New files in `Caly.Core` are CRLF with BOM and carry the MIT header (`C:\caly-work\3d\claude\tools\calyfile.py` makes that form).
- The control library is `Caly.Scene3D.Avalonia`; never name a namespace `Caly.Avalonia.*` (it would shadow `Avalonia` in every `Caly.*` namespace).

## Known state worth checking first

- Two Caly tests fail on untouched `develop`, the submodule layout (`TextLayerActualTextTests.AnUnclosedSequenceInAFormCoversTheRestOfThePage`, `.AStrayEndInAFormEndsTheEnclosingSequence`): not 3D. With the NuGet packages of this branch they pass.
- Open gaps: U3D vendor extensions other than `RHAdobeMeshResource` (which is decoded), PRC exact geometry: faces on Blend/Revolution/Extrusion/Offset surfaces (counted, not drawn), about 0.2% of compressed and 0.6% of uncompressed faces whose loops cannot be mapped or triangulated, mostly spline faces whose edges lie well off their surface), PDF's own 3D animation settings, bone links/IK.
- The workspace's last JOURNAL entry says what the owner wanted next.

## Which skill next

| Task | Skill |
|---|---|
| Reading a spec, reference code, issue, patent, library; adding a script engine | `clean-room-3d-sources` (**before** opening anything) |
| Touching `PrcCompressedDecoder`, `PrcBasis`, compressed meshes wrong/shards | `prc-compressed-tessellation` |
| Adding or fixing a U3D/PRC reader feature, scene model change, limits | `extending-scene3d-readers` |
| Camera, projection, visibility, node names, view conversion | `pdf-3d-view-conventions` |
| Declaring work done, running on screen, AOT, benchmarks | `verifying-3d-changes` |

At the end of a session of real work, append an entry to the workspace `JOURNAL.md` (what was done, learned, next) and update `CLAUDE.md`/the Scene3D README if behaviour changed.
