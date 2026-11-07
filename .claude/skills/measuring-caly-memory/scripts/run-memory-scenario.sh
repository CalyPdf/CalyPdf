#!/usr/bin/env bash
# Runs Caly through the same fixed session as run-memory-scenario.ps1 and writes the same results
# folder (steps.csv, memory.csv, render_timings.txt, run.json, and a .dmw with -dotmemory).
# Keyboard and command line only: keys go to Caly through xdotool (X11 or XWayland).
#
# Usage:
#   run-memory-scenario.sh --caly <path to the Caly executable> [--out <dir>] [--software]
#       [--dotmemory <dotMemory.sh>] [--extra-pdf-dir <dir>] [--cycles <n>] [--xdotool <path>]
#
# Build the executable first (from the repo root):
#   dotnet publish Caly.Desktop -r linux-x64 -c Release -f net10.0 -o <dir>
set -uo pipefail

caly=""; out="${TMPDIR:-/tmp}/caly-memory"; software=0; dotmemory=""; extra_dir=""; cycles=3; xdotool_bin="xdotool"
while [ $# -gt 0 ]; do
  case "$1" in
    --caly) caly=$2; shift 2 ;;
    --out) out=$2; shift 2 ;;
    --software) software=1; shift ;;
    --dotmemory) dotmemory=$2; shift 2 ;;
    --extra-pdf-dir) extra_dir=$2; shift 2 ;;
    --cycles) cycles=$2; shift 2 ;;
    --xdotool) xdotool_bin=$2; shift 2 ;;
    -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

die() { echo "ERROR: $*" >&2; exit 1; }

repo=$(cd "$(dirname "$0")/../../../.." && pwd)
docs="$repo/Caly.Tests/Documents"

[ -n "$caly" ] || die "--caly is required"
[ -x "$caly" ] || die "Caly executable not found or not executable: $caly"
caly=$(readlink -f "$caly")
case "$caly" in */Caly.Desktop/bin/Release/*) die "refusing the owner's Release build folder: $caly" ;; esac
[ -z "$dotmemory" ] || [ -x "$dotmemory" ] || die "dotMemory.sh not found or not executable: $dotmemory"
command -v "$xdotool_bin" >/dev/null 2>&1 || [ -x "$xdotool_bin" ] || die "xdotool not found (sudo apt install xdotool, or pass --xdotool <path>)"
[ -n "${DISPLAY:-}" ] || die "DISPLAY is not set: Caly needs an X11 or XWayland display"
if pgrep -x -i caly >/dev/null 2>&1; then die "Caly is already running - close it first (single-instance app)"; fi

main_pdf="$docs/algo.pdf"
tab_pdfs=("$docs/TIKA-584-0.pdf" "$docs/0000192.pdf" "$docs/fseprd1102849.pdf" "$docs/Brotli-Prototype-FileA.pdf")
if [ -n "$extra_dir" ]; then
  while IFS= read -r f; do tab_pdfs+=("$f"); done < <(find "$extra_dir" -maxdepth 1 -iname '*.pdf' | sort | head -n 6)
fi
for p in "$main_pdf" "${tab_pdfs[@]}"; do [ -f "$p" ] || die "PDF not found: $p"; done

if [ "$software" -eq 1 ]; then renderer=software; else renderer=gpu; fi
if grep -qi microsoft /proc/version 2>/dev/null; then machine=wsl2; else machine=$(hostname); fi
results="$out/$(date +%Y%m%d_%H%M%S)-$machine-$renderer"
mkdir -p "$results"
steps="$results/steps.csv"
echo "utc_ms,label" > "$steps"

# Settings and logs live under LocalApplicationData, which .NET maps to $XDG_DATA_HOME or ~/.local/share.
data_dir="${XDG_DATA_HOME:-$HOME/.local/share}/Caly"
settings="$data_dir/caly_settings"
logs="$data_dir/logs"
mkdir -p "$data_dir"
settings_backup="$results/.caly_settings.backup"
had_settings=0
if [ -f "$settings" ]; then cp "$settings" "$settings_backup"; had_settings=1; fi
started_marker="$results/.started"
touch "$started_marker"

calypid=0; dm_pid=0; dm_out=""; dm_fifo=""

# ---------------------------------------------------------------- cleanup (also on Ctrl+C)
cleanup() {
  local rc=$?
  trap - EXIT INT TERM
  if [ "$dm_pid" -ne 0 ] && kill -0 "$dm_pid" 2>/dev/null; then
    printf '##dotMemory["disconnect"]\r\n' >&9 2>/dev/null
    sleep 2
  fi
  if [ "$calypid" -ne 0 ] && kill -0 "$calypid" 2>/dev/null; then kill -KILL "$calypid" 2>/dev/null; fi
  if [ "$dm_pid" -ne 0 ] && kill -0 "$dm_pid" 2>/dev/null; then kill -KILL "$dm_pid" 2>/dev/null; fi
  exec 9>&- 2>/dev/null

  if [ "$had_settings" -eq 1 ]; then cp "$settings_backup" "$settings"; else rm -f "$settings"; fi

  local f
  f=$(find "$logs" -name 'memory_*.csv' -newer "$started_marker" 2>/dev/null | sort | tail -n 1)
  [ -z "$f" ] || cp "$f" "$results/memory.csv"
  f=$(find "$logs" -name 'render_timings_*.txt' -newer "$started_marker" 2>/dev/null | sort | tail -n 1)
  [ -z "$f" ] || cp "$f" "$results/render_timings.txt"

  local commit pdfs="" p os
  commit=$(git -C "$repo" rev-parse --short HEAD 2>/dev/null || true)
  os=$(. /etc/os-release 2>/dev/null && echo "$PRETTY_NAME"; true)
  for p in "$main_pdf" "${tab_pdfs[@]}"; do pdfs="$pdfs${pdfs:+, }\"$(basename "$p")\""; done
  printf '{\n  "machine": "%s",\n  "os": "%s (%s)",\n  "renderer": "%s",\n  "build": "%s",\n  "commit": "%s",\n  "pdfs": [%s]\n}\n' \
    "$machine" "$os" "$(uname -sr)" "$renderer" "$(stat -c %y "$caly" | cut -d. -f1)" "$commit" "$pdfs" > "$results/run.json"

  echo "Results: $results"
  exit "$rc"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# ---------------------------------------------------------------- helpers
xd() { "$xdotool_bin" "$@"; }

caly_window() { xd search --pid "$calypid" --onlyvisible 2>/dev/null | tail -n 1; }

focus() {
  local w
  w=$(caly_window)
  [ -n "$w" ] || die "Caly window not found"
  xd windowactivate --sync "$w" 2>/dev/null || xd windowfocus "$w" 2>/dev/null || true
  sleep 0.3
}

focused_pid() { xd getwindowfocus getwindowpid 2>/dev/null || true; }

# Keystrokes (Ctrl+F4 closes documents in many apps) must never reach another window: bring Caly back
# once, and stop the run rather than type into the user's work.
keys() {
  local pause=$1; shift
  if [ "$(focused_pid)" != "$calypid" ]; then
    focus
    [ "$(focused_pid)" = "$calypid" ] || die "Caly lost the keyboard focus and could not take it back; stopping so no keys reach another window."
  fi
  xd key --delay 25 "$@"
  sleep "$pause"
}

repeat_keys() { local n=$1 pause=$2; shift 2; local i; for ((i = 0; i < n; i++)); do keys "$pause" "$@"; done; }

open_pdf() {
  echo "  open $(basename "$1")"
  "$caly" "$1" >/dev/null 2>&1 &   # the new process hands its file to the running instance, then exits
  sleep 5
  focus
}

dm_lines() { [ -n "$dm_out" ] && wc -l < "$dm_out" || echo 0; }

# wait_dm <regex> <from line> <timeout s>: prints the first matching line after <from line>
wait_dm() {
  local pattern=$1 from=$2 timeout=$3 end=$((SECONDS + $3)) hit
  while [ "$SECONDS" -lt "$end" ]; do
    hit=$(tail -n +"$((from + 1))" "$dm_out" 2>/dev/null | grep -m 1 -E "$pattern" || true)
    if [ -n "$hit" ]; then echo "$hit"; return 0; fi
    sleep 0.3
  done
  return 1
}

take_snapshot() {
  local from; from=$(dm_lines)
  printf '##dotMemory["get-snapshot", {pid:%s, name:"%s"}]\r\n' "$calypid" "$1" >&9
  # 'snapshot-saved' per snapshot; the single .dmw workspace is written when Caly exits.
  wait_dm 'snapshot-saved' "$from" 180 >/dev/null || echo "WARNING: dotMemory did not report a saved snapshot for '$1' within 180 s" >&2
}

mark() {
  echo "mark $1"
  sleep 3   # settle: let renders and the close-time collection finish before sampling
  echo "$(date +%s%3N),$1" >> "$steps"
  if [ "$dm_pid" -ne 0 ]; then take_snapshot "$1"; fi
}

start_caly() {
  if [ -z "$dotmemory" ]; then
    "$caly" >/dev/null 2>&1 &
    calypid=$!
    return
  fi
  dm_out="$results/dotmemory.out"; : > "$dm_out"
  dm_fifo="$results/.dotmemory.in"; mkfifo "$dm_fifo"
  exec 9<>"$dm_fifo"   # read-write open: never blocks, and keeps the pipe open for our writes
  "$dotmemory" start --save-to-dir="$results" --service-output --service-input=stdin "$caly" <"$dm_fifo" >"$dm_out" 2>&1 &
  dm_pid=$!
  local line
  line=$(wait_dm 'connected' 0 90) || die "dotMemory did not report the profiled process. Output: $(tr '\n' '|' < "$dm_out")"
  calypid=$(printf '%s' "$line" | sed -n 's/.*pid: *\([0-9][0-9]*\).*/\1/p')
  [ -n "$calypid" ] || die "could not read the profiled pid from: $line"
}

# ---------------------------------------------------------------- run
if [ "$software" -eq 1 ]; then sw=true; else sw=false; fi
printf '{"Width":1600,"Height":1000,"IsMaximised":false,"PaneSize":323,"UseSoftwareRendering":%s,"Debug":{"LogMemory":true,"LogRenderTimings":true}}' "$sw" > "$settings"

echo "1. baseline"
start_caly
sleep 8
focus
mark A-baseline

echo "2. one document"
open_pdf "$main_pdf"
repeat_keys 40 0.15 Next
repeat_keys 4 0.4 ctrl+equal
repeat_keys 4 0.4 ctrl+minus
keys 0.8 ctrl+f
xd type --delay 80 the; sleep 0.5
keys 5 Return          # lets the search index build
keys 0.5 Escape
mark B-one-doc

echo "3. more tabs"
for pdf in "${tab_pdfs[@]}"; do
  open_pdf "$pdf"
  repeat_keys 10 0.15 Next
done
tab_count=$((1 + ${#tab_pdfs[@]}))
repeat_keys "$tab_count" 1.5 ctrl+Next
mark C-peak

echo "4. close all"
repeat_keys "$tab_count" 0.7 ctrl+F4
sleep 6   # covers the debounce before RequestGcCollect fires
kill -0 "$calypid" 2>/dev/null || die "Caly exited when its last document closed; the scenario expects the window to stay open."
mark D-closed-1

for ((n = 2; n <= cycles; n++)); do
  echo "5. cycle $n"
  open_pdf "$main_pdf"
  repeat_keys 40 0.15 Next
  keys 0.7 ctrl+F4
  sleep 6
  mark "D-closed-$n"
done

echo "6. quit"
kill -TERM "$calypid" 2>/dev/null
for _ in $(seq 1 30); do kill -0 "$calypid" 2>/dev/null || break; sleep 0.5; done
sleep 2
if [ "$dm_pid" -ne 0 ]; then
  wait_dm 'workspace-saved' 0 120 >/dev/null || echo "WARNING: dotMemory did not report the saved workspace (.dmw) within 120 s" >&2
fi
