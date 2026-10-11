# Caly.Avalonia.Scene3D

An Avalonia control that draws a [Scene3D](../external/Scene3D) scene with OpenGL: the 3D artwork of a PDF
(U3D today, PRC later), in the views the document authored, turned and zoomed with the pointer.

Depends on Avalonia, SkiaSharp and Scene3D. It knows nothing of PDF, of Caly's view models or of file
formats: a host gives it a `Scene` and a `SceneView`.

```csharp
var view3D = new Scene3DControl
{
    Scene = U3DReader.Read(payload),                    // the host owns and disposes the scene
    View = Artwork3DViewConverter.Convert(pdfView, w, h) // or null: a default view of the whole scene
};
```

## What it does

| | |
|---|---|
| Geometry | Every mesh instance of the scene, hidden nodes left out; meshes without normals shaded flat |
| Views | Camera (a pose or a camera node of the scene), perspective or orthographic projection, background colour, lighting scheme, per-node visibility, opacity and transform |
| Lighting | The schemes of ISO 32000-2 Table 321 with its colours and directions, or the scene's own lights; two-sided |
| Materials | Diffuse, specular, emissive, opacity, alpha test, vertex colours; transparent surfaces drawn last, back to front |
| Textures | The base-colour texture of each material (JPEG or PNG, with a separate alpha image if any), decoded off the UI thread after the first frame |
| Pointer | Left-drag rotates about the view's orbit point; right-drag or the wheel (while focused) zooms; Ctrl + left-drag or middle-drag pans; double-click or Home returns to the view |
| Anti-aliasing | 4 samples per pixel in the control's own framebuffer, fewer when that would exceed 96 MB |

It draws only when something changed: an idle view costs no GPU time.

Not drawn: render modes other than solid, cross sections, further texture layers, generated texture
coordinates, lines and points.

## Things a host must know

- **GPU only.** Under Avalonia's software renderer OpenGL never initialises. The control then stays blank
  and, half a second after it is attached, reports `Status == Unavailable`.
- **The scene's geometry moves to the GPU.** After uploading, the control calls `Scene.ReleaseGeometry()`
  (switch: `ReleaseGeometryAfterUpload`). When the control leaves the visual tree its GPU objects are
  released; back in the tree it reports `NeedsScene`, and the host must give it a scene that holds its
  geometry again (decode again). Do not dispose a scene while it is the control's `Scene`.
- **Zoomed pages: use `VisibleRegion`.** Avalonia sizes the control's framebuffer from its bounds, so a
  control inside a zoom transform is blurred, and one enlarged to compensate needs a framebuffer that
  grows with the zoom squared. Instead, place the control over the *visible* part of the 3D view at
  device resolution and set `VisibleRegion` to that part (in fractions of the whole view). The framebuffer
  then never exceeds the window. `tools/Scene3DViewer/ZoomedViewHost.cs` shows how.
- **`Statistics`** gives the GPU memory held (vertices, indices, textures, framebuffer), the last frame's
  draws, triangles and time, and `TexturesPending`: the model is complete when it reaches 0.
- **The payload must stay alive.** A scene's texture images may be slices of the payload it was read
  from; they are decoded in the background after the scene is set.
- Naming: inside any namespace that starts with `Caly.`, the simple name `Avalonia` now means
  `Caly.Avalonia`. Write `global::Avalonia.X` (or use a `using`) where code names Avalonia types in full.

## Layout of the code

| No GPU needed, unit-tested | Needs a GL context |
|---|---|
| `Internal/ViewCamera` — which camera a view means; view and projection matrices; clip planes | `Internal/Gl/GlApi` — the GL functions used, as function pointers |
| `Internal/OrbitController` — rotate, pan, zoom | `Internal/Gl/SceneGpuResources` — buffers and vertex arrays |
| `Internal/DrawList` — what to draw, in which pass and order | `Internal/Gl/RenderTargets` — the multisampled framebuffer and its resolve |
| `Internal/LightRigs` — the lights of each scheme | `Internal/Gl/SceneRenderer`, `Shaders` — the frame |
| `Internal/TextureDecoder`, `TextureStreamer` — images to pixels, off-thread | `Scene3DControl` — properties, input, lifetime |

## Checking it

```
dotnet test Caly.Avalonia.Scene3D.Tests                        # everything that needs no GPU
dotnet run -c Release --project tools/Scene3DViewer -- --selftest   # the GPU code, on a real GPU
dotnet run -c Release --project tools/Scene3DViewer -- <file.pdf | file.u3d> [--poster] [--benchmark 300]
```

`dotnet test` cannot reach the GL code (there is no headless OpenGL): the viewer's self-test draws
hand-made scenes, reads the framebuffer back and checks colours, coverage, texture orientation, blending,
lighting and the visible region, then that no GPU object survives the control leaving the window.

Verified on Windows (ANGLE on Direct3D 11) only. The shaders carry a desktop-GL variant for Linux and
macOS that has not been run.

## Measured

Intel UHD (integrated), 1800 × 1350 pixels, 4 × anti-aliased, a scripted orbit of 300 frames, frame time
including the GPU's work:

| Model | Draws | Triangles | Frame, 95th percentile | GPU memory |
|---|---:|---:|---:|---:|
| Engine | 391 | 210 k | 8.4 ms | 81 MB |
| Phone | 1,577 | 127 k | 8.8 ms | 93 MB |
| Desks, 631 textures | 647 | 23 k | 8.8 ms | 154 MB |
| House walkthrough | 358 | 77 k | 10.7 ms | 86 MB |

No managed allocation per frame while orbiting. Textures are all on the GPU 55 – 450 ms after the first
frame. About 78 MB of each figure is the anti-aliased framebuffer at that size.
