---
name: verifying-3d-changes
description: Use before declaring any 3D-artwork, Scene3D or Scene3DControl change done, committed, or checked on screen — running the corpus tests, Scene3DViewer self-test, clean and whole-solution builds, Native AOT publish, benchmarks, launching Caly on screen, or when a build works from the CLI but fails in Visual Studio.
---

# Verifying 3D changes

Evidence before claims. Read results before committing; report failures with their output.

## Gates, in order

1. **Whole solution, Debug** (the owner builds `Caly.slnx` in Visual Studio): `dotnet build Caly.slnx -c Debug`. Single-project builds hide missing solution entries (Phase 3 shipped with `Scene3D.Prc` missing from `Caly.slnx` and broke the owner's build).
2. **Clean** build too: `dotnet build Caly.slnx -c Release --no-incremental`; incremental builds hid an ILLink analyzer crash once.
3. **Scene3D tests**, only when Scene3D itself changed (Caly uses its NuGet packages, so these run in a checkout of `CalyPdf/Caly.Rendering.Scene3D`, branch `3d`; `<scene3d>` below is that checkout), with both corpora (real payloads; without the variables corpus tests pass vacuously):
   ```powershell
   $env:SCENE3D_PRC_CORPUS = 'C:\caly-work\3d\claude\survey\streams'
   $env:SCENE3D_PRC_CORPUS_EXTRA = 'C:\caly-work\3d\samples-prc-db\prcs'
   $env:SCENE3D_U3D_CORPUS = 'C:\caly-work\3d\claude\survey-simlab\streams'
   dotnet test <scene3d>/Caly.Rendering.Scene3D.Tests -c Release
   ```
   Deeper fuzz: `SCENE3D_FUZZ_MUTATIONS=4000`. Expect all pass (about 1,200 tests). `U3DLimitTests.ReadingAllocatesLittleBesidesTheScenesBuffers` is allocation-sensitive and varied in a Debug run: judge it in Release.
4. **PRC reader vs independent reader**: `python <scene3d>/tools/scripts/compare_trace.py --all` (env `PRC_DESCRIBE`, `SCENE3D_PRC_CORPUS`; on this machine prc-describe is `C:\caly-work\3d\claude\references\prc-rs-target\release\prc-describe.exe`) → 38 of 38 identical (needs prc-rs built; Rust is under `%USERPROFILE%\.cargo\bin`).
5. **Caly tests**: `$env:CALY_3D_SAMPLES='C:\caly-work\3d\samples'; dotnet test Caly.Tests --filter "FullyQualifiedName~Artwork3D"`, plus `dotnet test Caly.Scene3D.Avalonia.Tests -c Release`. With the NuGet packages, expect no failures. `dotnet test Caly.Printing/Caly.Printing.Tests` too when `IPdfDocumentService` changed (its `PrintTestDocument` implements it).
6. **GL code has no headless test**: run `dotnet run -c Release --project tools/Scene3DViewer -- --selftest` by hand on a GPU. Also `<file.pdf> --poster`, `--capture out.png`, `--scenario recycle`, `--software --exit-after 2`, `--benchmark 300`.
7. **Native AOT** when touching reflection/serialization/generics-heavy code: `dotnet publish Caly.Desktop -r win-x64 -c Portable -f net10.0 -o <folder>` (needs `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on PATH for vswhere). Viewer: `dotnet publish tools/Scene3DViewer -r win-x64 -c Release -p:ViewerAot=true` (never `-p:PublishAot=true`).
8. **Package versions agree**: one Scene3D version across `Caly.Pdf`, `Caly.Scene3D.Avalonia`, `Caly.Scene3D.Avalonia.Tests` and `tools/Scene3DViewer`, and the `PdfPig` version in `Caly.Pdf` contains the PdfPig change the work needs.
9. **Docs true**: Caly `CLAUDE.md` 3D section, Scene3D README, numbers re-measured not remembered; workspace `JOURNAL.md` entry.

Rendering/perf work: Release only. Benchmarks that show suspiciously fast renders may be drawing blank tiles; screenshot the measured window.

## Running Caly on screen (the owner may be using theirs)

- Check `Get-Process caly` first. The owner's instance locks `Caly.Desktop\bin\Release` and, being single-instance, swallows a second launch. **Never stop it.** Build elsewhere: `dotnet build Caly.Desktop -c Release -o C:\caly-work\3d\claude\build\caly-desktop`.
- Back up `%LOCALAPPDATA%\Caly\caly_settings` before a run and restore it after (Caly rewrites it on exit); `"UseSoftwareRendering": true` only for the software-renderer check.
- Driver with real input and screenshots: `C:\caly-work\3d\claude\plans\phase2c-logs\Caly.ps1` (`-Start`, `-Do 'click:x,y','wait:2.5','shot:name'`, `-Stop`); it acts only when Caly is the foreground window. Hold input while the owner is in the window.
- Render timings: `"Debug": {"LogRenderTimings": true}` → `%LOCALAPPDATA%\Caly\logs`.

## Environment gotchas

- Freshly built DLLs may be locked for minutes (antivirus): wait and retry; don't work around.
- Bash here-documents break on apostrophes: use the Write tool or PowerShell single-quoted here-strings.
- In PowerShell, relative paths in `[IO.File]` calls resolve against the process directory; use absolute paths. Assign `$null` to `$env:NAME` rather than deleting env vars.
- Headless Avalonia tests: two presses at one point are a double-click; a control added after `Show()` needs three forced render ticks before hit-testing; weak-reference checks need `Task.Yield()` first.
- The PdfPig and Scene3D repositories use CRLF; Edit/Write emit LF: check real changes with `git diff --ignore-all-space`.
