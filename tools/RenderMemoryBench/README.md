# RenderMemoryBench

Scripted, repeatable in-app measurement of Caly's render memory and draw timings on Windows. It produced
the numbers in `CLAUDE.md` § *GPU memory cost*.

BenchmarkDotNet cannot measure this: it is headless, so it gets no GPU `GRContext`, and it would measure
raw Skia rather than Caly. This harness drives the real app instead.

## Files

| File | Purpose |
|---|---|
| `run.ps1` | One run: launch, workload, capture, close. Appends a row to `<OutDir>\results.csv`. |
| `matrix.ps1` | Configs × scenarios × runs, alternating config order, then `summarize.ps1`. |
| `summarize.ps1` | Startup / pre-input / input / whole-run peaks per run from the timelines → `summary.csv`. |
| `vmstat.cs` | Address-space breakdown of a live process (`dotnet run vmstat.cs -- <pid>`). |

## Requirements

- Windows, PowerShell 7, .NET 10 SDK.
- A Release build: `dotnet build Caly.Desktop -c Release`. Debug skews timings.
- **Caly not running** — it is single-instance, and a second launch would hand the file to the running one.
- **Leave the machine alone.** Each run takes keyboard focus for ~40–60 s and sends keys to the foreground
  window. `focus_lost` in `results.csv` counts keys that could not be delivered; discard runs where it is non-zero.

Your settings file (`%LOCALAPPDATA%\Caly\caly_settings`) is backed up and restored by every run, after Caly
exits. Each run writes a test settings file with `Debug.LogRenderTimings` on, so the in-app
`RenderTimings` dump is produced.

## Usage

```powershell
cd tools\RenderMemoryBench

# Single run
.\run.ps1 -Label gpu -Mode gpu -Scenario scroll -OutDir results\smoke

# Software vs GPU, 3 runs each of scroll and zoom
.\matrix.ps1 -OutDir results\baseline -Runs 3 -Scenarios scroll,zoom -Configs @(
    'sw|software|',
    'gpu|gpu|')

# Startup and fixed cost only, small window
.\matrix.ps1 -OutDir results\nodoc -Runs 2 -Scenarios nodoc -Window '"Width":800,"Height":600,"IsMaximised":false' -Configs @(
    'sw|software|',
    'gpu|gpu|')
```

A config is `label|mode|ENV=VALUE;ENV=VALUE`. `mode` is `gpu` or `software` (written as
`UseSoftwareRendering`). The environment variables are passed to Caly. Caly has no experiment knobs of its
own, so to test a change without rebuilding per variant, temporarily read an environment variable at the
point of interest (e.g. `SkiaOptions` or `Win32PlatformOptions` in `Caly.Desktop/Program.cs`). Rebuild
Release once, compare configs, then remove the knob.

### Scenarios

| Scenario | Workload (after a 14 s settle) |
|---|---|
| `scroll` | 45 × PageDown at 220 ms |
| `zoom` | 40 × PageDown to dense content, then 3 × (6 × Ctrl+= , 6 × Ctrl+-) at 700 ms — crosses tile levels 0–3 each way |
| `idle` | Document open, no input for 10 s |
| `nodoc` | No document, no input for 10 s — no `RenderTimings` dump, since nothing is drawn |

Zoom sends `^=` / `^-` through `SendKeys`, which assumes a layout where `=` and `-` are the OEM plus/minus keys.

## Outputs (per run, in `OutDir`)

| File | Content |
|---|---|
| `results.csv` | One row per run: draw ops, mean/max draw time, in-app peaks (working set, private, managed, tile cache, Skia GPU cache), max GPU dedicated/shared usage per adapter, `focus_lost`, `killed`, backend |
| `<tag>.timeline.csv` | Working set and private bytes every 200 ms **from launch**, sampled out of process |
| `<tag>.marks.csv` | Timeline marks: `input_start`, `input_end`, `vmstat`, `close` |
| `<tag>.txt` / `<tag>.csv` | The in-app `RenderTimings` summary and its 250 ms memory series |
| `<tag>.vmstat.txt` | Address-space snapshot 2 s after input ends (skip with `-SkipVmStat`) |
| `<tag>.png` | Half-size screenshot of the window at the end of input |

`<tag>` is `label-scenario-run`. `results/` is git-ignored.

## Reading the numbers

- **Check the screenshot.** A run that measured near-blank pages is invalid: `TileCache` records blank tiles as
  bare keys and draws nothing. Tiles per draw op should be ~5 for scroll and ~10–11 for zoom.
- **Compare private bytes as well as working set.** The GPU back-end decides where memory lands: ANGLE on an
  iGPU puts surfaces and textures in private memory, WGL in mapped GPU sections, and a dGPU in dedicated VRAM
  (see `gpu_dedicated_mb` / `gpu_shared_mb`).
- **Mind the startup spike.** The ANGLE peak can occur before any input, ~1.5–3 s after launch depending on
  how fast the launch is, so `startup_peak_*` (launch → input start) matters as much as `input_peak_*`.
- **Look at window size.** GPU memory scales with window pixels. Keep `-Window` identical across configs and
  note the display resolution and scaling.
- **Look at the spread.** Repeat runs agree to ±5 MB when the machine is quiet; a larger spread means
  background interference.
- **Which GPU?** On a hybrid laptop, Windows picks the adapter. To test the dGPU, set a per-app preference
  (`HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, value name = full path to `Caly.exe`, data
  `GpuPreference=2;`) and remove it afterwards.
