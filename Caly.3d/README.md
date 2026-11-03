# Caly.3d

3D artwork for Caly: a click on a PDF's U3D or PRC artwork shows the model in place on its page, in the views the document authored, with orbit, pan, zoom, render modes, lighting and U3D animations.

| Project | Purpose |
|---|---|
| `Caly.Scene3D.Avalonia` | `Scene3DControl`: an Avalonia OpenGL control that draws a Scene3D scene for a view. It knows nothing of PDF or of Caly's view models; see its [README](Caly.Scene3D.Avalonia/README.md) |
| `Caly.Scene3D.Avalonia.Tests` | Tests of the control's parts that need no GPU |
| `Scene3DViewer` | A host for the control: opens a `.u3d`, a `.prc` or a PDF's 3D artwork, and carries the control's GPU self-test, benchmark and capture modes |

The rest of the feature lives outside this folder:

- **Decoding:** the NuGet packages `Caly.Rendering.Scene3D` (the scene model), `Caly.Rendering.Scene3D.U3D` and `Caly.Rendering.Scene3D.Prc`. `Caly.Pdf`, `Caly.Scene3D.Avalonia`, `Caly.Scene3D.Avalonia.Tests` and `Scene3DViewer` must all reference the same version.
- **Finding the artwork in a PDF:** PdfPig's `UglyToad.PdfPig.Artwork` (`Artwork3D.TryCreate`).
- **PDF to scene:** `Caly.Pdf/Artwork` (`Artwork3DContentReader`, `Artwork3DSceneReader`, `Artwork3DViewConverter`).
- **The page:** `Caly.Core` (`Artwork3DViewModel`, `Artwork3DView`, `PageInteractiveLayerControl`).

The "3D Artwork" section of the repository's `CLAUDE.md` describes how these fit together.

The control needs the GPU renderer. Under the software renderer, and on Android, it reports `Unavailable` and Caly keeps showing the page's poster.

## Tests

```bash
dotnet test Caly.3d/Caly.Scene3D.Avalonia.Tests
```

There is no headless OpenGL, so `dotnet test` cannot reach the GL code. The viewer's self-test checks it on a real GPU:

```bash
dotnet run -c Release --project Caly.3d/Scene3DViewer -- --selftest
dotnet run -c Release --project Caly.3d/Scene3DViewer -- <file.pdf | file.u3d | file.prc> [--poster] [--benchmark 300]
```

Some of the `Artwork3D` tests in `Caly.Tests` read sample documents. They run when `CALY_3D_SAMPLES` names the samples folder and skip otherwise. `Artwork3DPosterSilhouetteTests` also needs `CALY_3D_VIEWER`, the path of the built `Scene3DViewer` executable.

## Native AOT

```bash
dotnet publish Caly.3d/Scene3DViewer -r win-x64 -c Release -p:ViewerAot=true
```

Use `-p:ViewerAot=true`, not `-p:PublishAot=true`: given on the command line, `PublishAot` reaches the netstandard2.0 projects the viewer references, which cannot be compiled ahead of time.
