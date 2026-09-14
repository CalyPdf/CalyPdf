See https://github.com/peaceshi/Avalonia-NativeAOT-SingleFile

Download the required .lib files into this folder.

## Skia / HarfBuzz

https://github.com/2ndlab/SkiaSharp.Static/releases — or build from
https://github.com/CalyPdf/SkiaSharp.Static, which pins the SkiaSharp version we consume.

`skia.lib`, `SkiaSharp.lib`, `libHarfBuzzSharp.lib` (win-x64) and the `.a` equivalents (linux-x64).

## ANGLE

**In use** — GPU rendering is the default, and the `Portable` publish deletes `av_libglesv2.dll`
because it is statically linked instead. Without these libs a Portable build has no GPU backend.

Build from https://github.com/CalyPdf/ANGLE.Static, which pins **AvaloniaUI/angle**
`avalonia-fork-master` @ `1c89805903c1` — the same commit Avalonia itself ships, as declared in
`Avalonia.Angle.Windows.Natives`'s nuspec (`<repository ... commit="1c89805903c1..."/>`). Upstream
`google/angle` is a different lineage and will not match.

Required (win-x64 only; Linux uses the system EGL/GLX, not ANGLE):

- `libGLESv2_static.lib`
- `libEGL_static.lib` — required separately: the fork's libGLESv2 exports no `egl*` symbols
- `libANGLE_static.lib`

Re-pin whenever the Avalonia version changes: read the `FileVersion` of the shipped
`av_libglesv2.dll` (it carries the git hash) and update `ANGLE_Commit` in ANGLE.Static.