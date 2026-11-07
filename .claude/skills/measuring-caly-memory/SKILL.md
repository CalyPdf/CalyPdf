---
name: measuring-caly-memory
description: Use when changing or investigating Caly's memory — TileCache, the page picture and text-layer caches, Skia or GPU caches, document open/close lifetime, disposing Skia objects, GC settings — or when anyone says memory, RAM, working set, leak, or "closing a document does not give the memory back". Runs a fixed scripted session on Windows and Linux, records memory at named steps, and compares runs before and after a change.
---

# Measuring Caly's memory

Measure before and after every memory change; a fix is kept only if its numbers improve and scrolling, zooming and page turns look unchanged. Never claim a saving from reading code.

## What it measures, and what it cannot

- **In-process probe** (`Caly.Core/Services/Rendering/MemoryProbe.cs`): one CSV row every 500 ms, plus a row labelled `close-gc` right after the collection that follows a document close. Off by default. Switch it on with `"Debug": {"LogMemory": true}` in `caly_settings` (or Settings → Debug → Log memory; applies from the next launch). Logs go to `%LOCALAPPDATA%\Caly\logs` (Linux: `~/.local/share/Caly/logs`) as `memory_*.csv`. Columns, all bytes: `utc_ms,label,working_set,private_bytes,gc_heap,gc_fragmented,gc_committed,gc_total_allocated,skia_resource_cache,skia_font_cache,gpu_cache,tile_cache`.
- **dotMemory** (command-line profiler, then the dotMemory app on the `.dmw`): the .NET heap, who keeps a closed document alive, duplicate arrays and strings, allocation sources. It sees Skia memory only as one "unmanaged" total.
- **Skia native memory** comes from the probe's `skia_*` and `gpu_cache` columns. `skia_resource_cache` stays near 0 under the GPU renderer (tiles are rasterised by a CPU surface into `SKImage`s the tile cache owns; the GPU cache is `gpu_cache`).
- **Not profilable:** the Native AOT build (`-c Portable`) cannot be attached by dotMemory. Check it with the probe's process numbers only.
- `gc_heap` is 0 until the first collection (the probe reads the last GC's info). On Linux `private_bytes` is address-space-like (about 750 MB at idle); read `working_set` as the headline figure there.

## The scenario

Keyboard and command line only, so it survives layout changes. Document set from `Caly.Tests/Documents`: `algo.pdf` (24.8 MB, long text) plus `TIKA-584-0.pdf`, `0000192.pdf`, `fseprd1102849.pdf`, `Brotli-Prototype-FileA.pdf`. Heavy extras (scans, JPEG 2000, Type 3, colour) come from a folder passed to `-ExtraPdfDir` / `--extra-pdf-dir`; its first six `*.pdf` (by name) become more tabs. The owner's corpus is at `%USERPROFILE%\Documents\Pdf` (for example `0012\0012113.pdf` for Type 3, `ColorIssue.pdf`, `_very large pdfs`); it is not in the repo.

| Step | What happens | Mark |
|---|---|---|
| 1 | Caly starts with no document, 8 s | `A-baseline` |
| 2 | open `algo.pdf`, Page Down ×40, zoom in ×4 and out ×4, search `the`, wait 5 s | `B-one-doc` |
| 3 | open the other PDFs as tabs (Page Down ×10 each), walk through every tab | `C-peak` |
| 4 | close every document, wait 6 s | `D-closed-1` |
| 5 | open `algo.pdf`, Page Down ×40, close, wait 6 s — repeated until `-Cycles` (default 3) | `D-closed-2`, `D-closed-3` |
| 6 | quit Caly | |

Each mark waits 3 s first, writes `utc_ms,label` to `steps.csv`, and (with dotMemory) takes a named snapshot. Growth across the `D-closed-*` marks is the leak signal.

The driver never sends keys unless Caly is the target window (Ctrl+F4 closes documents in many apps) and stops the run if it is not. Do not use the machine's keyboard while it runs.

## Build

Never use the owner's `Caly.Desktop\bin\Release` (their running Caly locks it, and Caly is single-instance: a second launch hands its file to that copy). Build elsewhere:

```powershell
dotnet build Caly.Desktop -c Release -o C:\caly-work\memory\bin                          # Windows
dotnet publish Caly.Desktop -r linux-x64 -c Release -f net10.0 -o <dir>                  # Linux executable is <dir>/caly
```

Release always: Debug skews memory and timing. Any build of this branch (or later) contains the probe; the run is only valid if `memory.csv` ends up non-empty in the results folder. The Windows executable is `caly.exe` (Windows ignores the case). Use a fresh `-o` folder per build you want to keep; the `Caly.Desktop\bin\Release` guard only refuses that one folder. The build needs the `external/` checkouts to be present, and reuses `obj\` (run `dotnet clean` or delete `obj\` for a clean-room build).

## Run

Paths below are relative to the repo root; the scripts work from any current directory. A run needs an interactive desktop session and takes about 3 minutes (about 5 with dotMemory), with Caly windows visible and the keyboard in use.

Windows (needs PowerShell 7):

```powershell
pwsh .claude/skills/measuring-caly-memory/scripts/run-memory-scenario.ps1 -CalyExe <Caly.exe> [-OutDir <dir>] [-Software] [-DotMemory <dotMemory.exe>] [-ExtraPdfDir <dir>] [-Cycles 3]
```

Linux (X11 or WSLg):

```bash
.claude/skills/measuring-caly-memory/scripts/run-memory-scenario.sh --caly <dir>/caly [--out <dir>] [--software] [--dotmemory <dotMemory.sh>] [--extra-pdf-dir <dir>] [--cycles 3] [--xdotool <path>]
```

Windows extras: `-ExtraSettings @{ UseCompactTileFormat = $true }` sets other `caly_settings` keys for the run (restored afterwards); `-OnMark { param($label, $calyPid) ... }` runs a script block after each step, for extra captures (see "Without the dotMemory app").

Both refuse to run while any Caly is running, back up and restore `caly_settings` (also on Ctrl+C), and write `<out>/<yyyyMMdd_HHmmss>-<machine>-<gpu|software>/` with `steps.csv`, `memory.csv`, `render_timings.txt`, `run.json` and, with dotMemory, one `.dmw` holding a named snapshot per step. Default out dir: `%TEMP%\caly-memory` / `/tmp/caly-memory`. The last line printed is `Results: <path>`: that sub-folder (not `-OutDir`) is what the compare tool takes. The folder name carries the *requested* renderer; the real back end is on the `Backend` line of `render_timings.txt`. `run.json` records the commit, to confirm which code a run measured.

Linux setup:
- `xdotool`: `sudo apt install xdotool`; without root, `apt-get download xdotool libxdo3`, `dpkg -x` both into a folder, add its `usr/lib/x86_64-linux-gnu` to `LD_LIBRARY_PATH`, and pass `--xdotool <folder>/usr/bin/xdotool`.
- A .NET 10 SDK to publish (or publish on Windows with `-r linux-x64` and copy the folder; copy it off `/mnt/c` before running).
- Under WSLg there is no window manager: X focus follows the pointer, so the driver parks the pointer over Caly (no clicks). WSLg has no GPU path for Skia: Caly renders in **software** there, so WSL2 numbers are CPU-renderer numbers. GPU figures need the native Linux machine.
- `run.json` `commit` is empty when the repo is a Windows worktree seen from WSL (its `.git` points at a Windows path).

## dotMemory

- Use the **command-line profiler**, not the GUI. NuGet packages `JetBrains.dotMemory.Console.windows-x64` and `JetBrains.dotMemory.Console.linux-x64` (free; version 2026.2.4.1 was used): unzip the `.nupkg`, the tool is `tools/dotMemory.exe` / `tools/dotMemory.sh`. The `dotMemory.exe` inside the JetBrains install folder is the GUI and prints nothing useful. On Linux use `dotMemory.sh` (documented protocol); the newer `dotmemory` launcher next to it speaks a different one.
- The driver starts Caly as `dotMemory start --save-to-dir=<results> --service-output --service-input=stdin <Caly>` and writes `##dotMemory["get-snapshot", {pid:N, name:"<step>"}]` to its stdin; it waits for `snapshot-saved`. The single `.dmw` is written when Caly exits. Profiling slows Caly: use dotMemory runs to find *what holds memory*, plain runs for *numbers*.
- Analyse in the dotMemory app (Rider/dotUltimate): open the `.dmw` (from WSL: `\\wsl.localhost\<distro>\home\...`). Compare `A-baseline` with `D-closed-1`: *Survived objects* in namespaces `Caly.*`, `UglyToad.PdfPig.*`, `SkiaSharp` (`SKPicture`, `SKImage`), type `TileImage`; open *Key retention paths* on a survivor (`DocumentViewModel`, `PageViewModel`, `PdfDocument`). Look at *Memory allocation* between `A-baseline` and `B-one-doc` for allocation sources and the large object heap.

## Without the dotMemory app (scriptable heap checks)

For "does anything of a closed document survive?" without opening the GUI: `dotnet tool install dotnet-dump --tool-path <dir>` (and `dotnet-gcdump`), then pass an `-OnMark` hook that runs `dotnet-dump collect -p $calyPid --type Heap -o <file>` at `D-closed-1`, and read it with `dotnet-dump analyze <file> -c "dumpheap -stat" -c "dumpheap -stat -live" -c "dumpheap -stat -dead" -c exit`. Lessons from using it: `dotnet-gcdump` warns it may be incomplete on large heaps for .NET 10 targets and under-reported here, prefer the heap dump; `gcroot` found no roots for arrays the runtime still counted live (a heap dump has no thread stacks), so judge liveness from the probe's post-GC `gc_heap` at the `close-gc` rows, not from `-dead`. A closed document that is retained shows up as surviving `DocumentViewModel`, `PageViewModel`, `PdfDocument`, `SKPicture` or `TileImage` instances.

## Reading the results

```powershell
dotnet run .claude/skills/measuring-caly-memory/scripts/compare-memory.cs -- <results folder> [<results folder>]
dotnet run .claude/skills/measuring-caly-memory/scripts/compare-memory.cs -- --self-test
```

`r1` is the first folder given, `r2` the second; with two folders (before, after) a `diff` group is added. Read, in this order:
1. `tiles per draw op` — about 4–5 for plain scrolling, ~6 across this scenario. Below 3 prints a WARNING: the run measured blank page area and its timings and tile numbers mean nothing.
2. `after-close vs baseline` — working set at `D-closed-1` against `A-baseline`; goal: within 10 %. Look at the later `D-closed-*` rows too: they can tell a different story.
3. A `LEAK?` line: working set after the last close cycle is more than 10 % above `D-closed-1`. It is printed only when true; no line means no such growth.
4. The `peak` row and `C-peak`: the headline for "lower peak memory".

Compare like with like: same OS, same renderer, same machine, same PDFs and cycles, ideally the same quiet machine state. A difference under a few percent is noise; repeat before believing it.

## Pitfalls

- Blank page area produces fast, clean, meaningless numbers (`TileCache` stores blank tiles as bare keys). Always look at tiles per draw op.
- The owner's running Caly: never stop it, never build into its folder, never run while it is open (the driver refuses).
- Caly rewrites `caly_settings` on exit; the drivers restore it. After a crashed run, check it.
- PowerToys ZoomIt owns Ctrl+1..7 on the owner's machine; the scenario avoids Ctrl+digit. Caly's own zoom keys are Alt+0..4.
- `.NET` does not raise `ProcessExit` on SIGTERM for plain apps; `RenderTimings` registers for SIGTERM so the Linux driver can end Caly that way and still get `render_timings.txt`.
- Memory after close is *not* expected to equal baseline in every column: JIT-compiled code, loaded libraries and Skia/ANGLE driver state stay. Judge by the trend across close cycles and by retained objects in dotMemory.

## Reference numbers

Point-in-time; re-run before trusting them on a different build or machine.

**Baseline, before any memory fix** — 2026-11-07, commit `0c1b6f44` (probe added, nothing else changed), Release build, default `TileCacheSizeMB` (128), the five-document scenario above with 3 cycles, laptop `LAPTOP-DB6HS41I` (Windows 11, integrated GPU) and its WSL2 Ubuntu 24.04. Working set in MiB:

| Step | Windows GPU | Windows software | WSL2 (software) |
|---|---|---|---|
| A-baseline | 244 | 130 | 123 |
| B-one-doc | 478 | 282 | 358 |
| C-peak | 620 | 343 | 664 |
| D-closed-1 | 514 | 293 | 665 |
| D-closed-2 | 450 | 297 | 702 |
| D-closed-3 | 440 | 202 | 639 |
| peak over the run | 825 | 566 | 891 |
| after-close vs baseline | +111 % | +125 % | +439 % |
| tiles per draw op | 6.4 | 7.0 | 3.9 |

What the baseline already shows (to confirm in dotMemory, not yet explained): memory after closing everything is far above the starting point on every back end; on Windows GPU `gpu_cache` stays at 79.6 MiB after the last document closes; the tile caches peak at about 48 MiB (20 MiB on WSL2) yet empty on close.

Noise: two runs of the same build differed by about 10 % in working set at single steps (Windows GPU `A-baseline` 224 and 244, `D-closed-1` 526 and 514). Treat smaller differences as noise. Native Linux (GPU and software) baselines are not recorded yet: run both drivers there and add the column.

## Rules this work established

(filled in at the end of the project)
