<#
.SYNOPSIS
    Runs Caly through a fixed session (open, scroll, zoom, search, open more tabs, close all, repeat
    open/close) and records its memory at named steps. Windows driver; run-memory-scenario.sh is the
    Linux twin and writes the same results folder.

.DESCRIPTION
    Keyboard and command line only: no mouse coordinates, so the run survives layout changes. Caly
    must be built with the memory probe (Debug.LogMemory). The script switches the probe and render
    timings on in caly_settings, restores that file afterwards, and refuses to run while any Caly
    process exists (Caly is single-instance: a second launch would hand its file to the owner's
    running copy).

    Results folder <OutDir>\<yyyyMMdd_HHmmss>-<machine>-<gpu|software>\ holds:
      steps.csv           utc_ms,label - one row per step, A-baseline ... D-closed-<n>
      memory.csv          the probe's samples for the whole run (copied from the logs folder)
      render_timings.txt  Caly's render timings (tiles per draw op shows whether content was on screen)
      run.json            machine, os, renderer, build, commit, pdfs
      *.dmw               with -DotMemory: one workspace file holding a snapshot per step, named after it

.PARAMETER CalyExe
    The Caly.exe to run. Never the owner's build: a path under Caly.Desktop\bin\Release is refused.
    Build to a separate folder: dotnet build Caly.Desktop -c Release -o <dir>

.PARAMETER DotMemory
    Path to dotMemory.exe of the command-line profiler (NuGet package JetBrains.dotMemory.Console.windows-x64;
    the dotMemory.exe inside the IDE/standalone install is the GUI and will not work). Caly is then
    started through it and a snapshot is taken at every step.

.PARAMETER ExtraPdfDir
    A folder of heavy PDFs. Its first six *.pdf files (by name) are opened as extra tabs in step 3.

.PARAMETER Cycles
    Number of open/close cycles after step 3; D-closed-1 is step 4, the rest repeat algo.pdf.

.PARAMETER ExtraSettings
    Top-level caly_settings keys to set for this run, for example @{ UseCompactTileFormat = $true }.
    They are merged over the driver's own settings; the original file is restored afterwards.

.PARAMETER OnMark
    A script block called after each step is recorded, as & $OnMark $label $calyPid. For extra
    captures at the named steps, for example dotnet-gcdump collect -p $calyPid. Runs after the
    step's timestamp is written, so its duration does not move the memory samples.

.EXAMPLE
    pwsh .claude/skills/measuring-caly-memory/scripts/run-memory-scenario.ps1 -CalyExe C:\caly-work\memory\bin\Caly.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $CalyExe,
    [string] $OutDir = (Join-Path $env:TEMP 'caly-memory'),
    [switch] $Software,
    [string] $DotMemory,
    [string] $ExtraPdfDir,
    [int] $Cycles = 3,
    [scriptblock] $OnMark,
    [hashtable] $ExtraSettings
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

if (-not ([System.Management.Automation.PSTypeName]'CalyMemWin32').Type) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CalyMemWin32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    public static uint ForegroundPid() { uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return pid; }
}
'@
}

# ---------------------------------------------------------------- inputs and guards
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$docsDir = Join-Path $repo 'Caly.Tests\Documents'

if (-not (Test-Path $CalyExe)) { throw "Caly.exe not found: $CalyExe" }
$CalyExe = (Resolve-Path $CalyExe).Path
if ($CalyExe -match '\\Caly\.Desktop\\bin\\Release\\') {
    throw "Refusing to run '$CalyExe': that folder is the owner's Release build. Build to another folder with -o."
}
if ($DotMemory -and -not (Test-Path $DotMemory)) { throw "dotMemory.exe not found: $DotMemory" }
if (Get-Process caly -ErrorAction SilentlyContinue) {
    throw 'Caly is already running - close it first (single-instance app; a second launch would use that copy).'
}

$mainPdf = Join-Path $docsDir 'algo.pdf'
$otherPdfs = 'TIKA-584-0.pdf', '0000192.pdf', 'fseprd1102849.pdf', 'Brotli-Prototype-FileA.pdf' | ForEach-Object { Join-Path $docsDir $_ }
$extraPdfs = @()
if ($ExtraPdfDir) {
    $extraPdfs = @(Get-ChildItem $ExtraPdfDir -Filter *.pdf | Sort-Object Name | Select-Object -First 6 -ExpandProperty FullName)
}
foreach ($p in @($mainPdf) + $otherPdfs) { if (-not (Test-Path $p)) { throw "PDF not found: $p" } }
$tabPdfs = @($otherPdfs) + $extraPdfs

$renderer = if ($Software) { 'software' } else { 'gpu' }
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$results = Join-Path $OutDir "$stamp-$($env:COMPUTERNAME)-$renderer"
New-Item -ItemType Directory -Force $results | Out-Null
$stepsFile = Join-Path $results 'steps.csv'
Set-Content $stepsFile 'utc_ms,label'

$calyDir = Join-Path $env:LOCALAPPDATA 'Caly'
$settingsPath = Join-Path $calyDir 'caly_settings'
$logsDir = Join-Path $calyDir 'logs'

# ---------------------------------------------------------------- helpers
$script:calyPid = 0
$script:dm = $null          # dotMemory process
$script:dmLines = $null     # its stdout lines

function Focus {
    $h = (Get-Process -Id $script:calyPid).MainWindowHandle
    if ($h -eq [IntPtr]::Zero) { throw 'Caly window not found.' }
    # An Alt tap lifts Windows' foreground lock, which otherwise ignores SetForegroundWindow from a background process.
    [CalyMemWin32]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [CalyMemWin32]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    [CalyMemWin32]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 200
}

function Keys([string] $keys, [int] $pauseMs = 300) {
    # Keystrokes (Ctrl+F4 closes documents in many apps) must never reach another window: if the user
    # has switched away, bring Caly back once, and stop the run rather than type into their work.
    if ([CalyMemWin32]::ForegroundPid() -ne $script:calyPid) {
        Focus
        if ([CalyMemWin32]::ForegroundPid() -ne $script:calyPid) {
            throw 'Caly lost the keyboard focus and could not take it back; stopping so no keys reach another window.'
        }
    }
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds $pauseMs
}

function Repeat([string] $keys, [int] $count, [int] $pauseMs) {
    for ($i = 0; $i -lt $count; $i++) { Keys $keys $pauseMs }
}

function Open-Pdf([string] $path) {
    # A new Caly.exe hands its file to the running single instance over a named pipe, which opens it as a tab.
    Write-Host "  open $(Split-Path $path -Leaf)"
    Start-Process $CalyExe -ArgumentList "`"$path`"" | Out-Null
    Start-Sleep -Seconds 5
    Focus
}

function Wait-DotMemoryLine([string] $pattern, [int] $fromIndex, [int] $timeoutSec) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $lines = @($script:dmLines.ToArray())
        for ($i = $fromIndex; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match $pattern) { return $lines[$i] }
        }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Take-Snapshot([string] $label) {
    $from = $script:dmLines.Count
    $script:dm.StandardInput.WriteLine("##dotMemory[`"get-snapshot`", {pid:$($script:calyPid), name:`"$label`"}]")
    $script:dm.StandardInput.Flush()
    # 'snapshot-saved' per snapshot; the single .dmw workspace holding them all is written when Caly exits.
    if (-not (Wait-DotMemoryLine 'snapshot-saved' $from 180)) {
        Write-Warning "dotMemory did not report a saved snapshot for '$label' within 180 s."
    }
}

function Mark([string] $label) {
    Write-Host "mark $label"
    Start-Sleep -Seconds 3   # settle: let renders and the close-time collection finish before sampling
    Add-Content $stepsFile "$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()),$label"
    if ($script:dm) { Take-Snapshot $label }
    if ($OnMark) { & $OnMark $label $script:calyPid }
}

function Start-Caly {
    if (-not $DotMemory) {
        $p = Start-Process $CalyExe -PassThru
        $script:calyPid = $p.Id
        return
    }
    $psi = [System.Diagnostics.ProcessStartInfo]::new($DotMemory)
    foreach ($a in 'start', "--save-to-dir=$results", '--service-output', '--service-input=stdin', $CalyExe) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $script:dmLines = [System.Collections.ArrayList]::Synchronized((New-Object System.Collections.ArrayList))
    $script:dm = [System.Diagnostics.Process]::new()
    $script:dm.StartInfo = $psi
    Register-ObjectEvent -InputObject $script:dm -EventName OutputDataReceived -MessageData $script:dmLines -Action {
        if ($null -ne $EventArgs.Data) { $Event.MessageData.Add($EventArgs.Data) | Out-Null }
    } | Out-Null
    $script:dm.Start() | Out-Null
    $script:dm.StandardInput.NewLine = "`r`n"
    $script:dm.BeginOutputReadLine()

    $connected = Wait-DotMemoryLine 'connected' 0 90
    if (-not $connected -or $connected -notmatch 'pid:\s*(\d+)') { throw "dotMemory did not report the profiled process. Output: $($script:dmLines -join ' | ')" }
    $script:calyPid = [int]$Matches[1]
}

function Collect-Logs {
    # Newest probe and timings files written since this run began.
    $since = $script:startedAt
    foreach ($spec in @(@('memory_*.csv', 'memory.csv'), @('render_timings_*.txt', 'render_timings.txt'))) {
        $f = Get-ChildItem $logsDir -Filter $spec[0] -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -ge $since } | Sort-Object LastWriteTime | Select-Object -Last 1
        if ($f) { Copy-Item $f.FullName (Join-Path $results $spec[1]) -Force }
    }
}

# ---------------------------------------------------------------- run
$backup = $null
if (Test-Path $settingsPath) { $backup = [System.IO.File]::ReadAllBytes($settingsPath) }
New-Item -ItemType Directory -Force $calyDir | Out-Null
$script:startedAt = Get-Date

try {
    $settingsTable = [ordered]@{
        Width = 1600; Height = 1000; IsMaximised = $false; PaneSize = 323
        UseSoftwareRendering = [bool]$Software
        Debug = [ordered]@{ LogMemory = $true; LogRenderTimings = $true }
    }
    if ($ExtraSettings) { foreach ($k in $ExtraSettings.Keys) { $settingsTable[$k] = $ExtraSettings[$k] } }
    $settings = $settingsTable | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText($settingsPath, $settings)

    # 1. Baseline: Caly up, no document.
    Write-Host '1. baseline'
    Start-Caly
    Start-Sleep -Seconds 8
    Focus
    Mark 'A-baseline'

    # 2. One heavy document: scroll, zoom out and in, search.
    Write-Host '2. one document'
    Open-Pdf $mainPdf
    Repeat '{PGDN}' 40 150
    Repeat '^=' 4 400
    Repeat '^-' 4 400
    Keys '^f' 800
    Keys 'the' 500
    Keys '{ENTER}' 5000   # lets the search index build
    Keys '{ESC}' 500
    Mark 'B-one-doc'

    # 3. More tabs, a short scroll on each, then a walk through them all.
    Write-Host '3. more tabs'
    foreach ($pdf in $tabPdfs) {
        Open-Pdf $pdf
        Repeat '{PGDN}' 10 150
    }
    $tabCount = 1 + $tabPdfs.Count
    Repeat '^{PGDN}' $tabCount 1500
    Mark 'C-peak'

    # 4. Close everything.
    Write-Host '4. close all'
    Repeat '^{F4}' $tabCount 700
    Start-Sleep -Seconds 6   # covers the debounce before RequestGcCollect fires
    if (-not (Get-Process -Id $script:calyPid -ErrorAction SilentlyContinue)) {
        throw 'Caly exited when its last document closed; the scenario expects the window to stay open.'
    }
    Mark 'D-closed-1'

    # 5. Repeat open/close: growth across D-closed-* is the leak signal.
    for ($n = 2; $n -le $Cycles; $n++) {
        Write-Host "5. cycle $n"
        Open-Pdf $mainPdf
        Repeat '{PGDN}' 40 150
        Keys '^{F4}' 700
        Start-Sleep -Seconds 6
        Mark "D-closed-$n"
    }

    # 6. Quit Caly.
    Write-Host '6. quit'
    $p = Get-Process -Id $script:calyPid -ErrorAction SilentlyContinue
    if ($p) {
        $p.CloseMainWindow() | Out-Null
        if (-not $p.WaitForExit(15000)) { $p.Kill() }
    }
    Start-Sleep -Seconds 2
    if ($script:dm -and -not (Wait-DotMemoryLine 'workspace-saved' 0 120)) {
        Write-Warning 'dotMemory did not report the saved workspace (.dmw) within 120 s.'
    }
}
finally {
    if ($script:dm -and -not $script:dm.HasExited) {
        try { $script:dm.StandardInput.WriteLine('##dotMemory["disconnect"]'); $script:dm.WaitForExit(10000) | Out-Null } catch { }
    }
    $left = Get-Process -Id $script:calyPid -ErrorAction SilentlyContinue
    if ($left) { $left.Kill() }
    if ($script:dm -and -not $script:dm.HasExited) { $script:dm.Kill() }
    Get-EventSubscriber -ErrorAction SilentlyContinue | Unregister-Event -ErrorAction SilentlyContinue

    if ($backup) { [System.IO.File]::WriteAllBytes($settingsPath, $backup) } else { Remove-Item $settingsPath -ErrorAction SilentlyContinue }

    Collect-Logs

    $commit = try { (git -C $repo rev-parse --short HEAD 2>$null) } catch { '' }
    [ordered]@{
        machine  = $env:COMPUTERNAME
        os       = [System.Environment]::OSVersion.VersionString
        renderer = $renderer
        build    = (Get-Item $CalyExe).VersionInfo.ProductVersion
        commit   = "$commit"
        pdfs     = @($mainPdf) + $tabPdfs | ForEach-Object { Split-Path $_ -Leaf }
    } | ConvertTo-Json | Set-Content (Join-Path $results 'run.json')
}

Write-Host "Results: $results"
