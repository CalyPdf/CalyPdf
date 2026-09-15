<#
.SYNOPSIS
    One scripted Caly run: launch, drive a fixed workload, capture memory and render timings.

.DESCRIPTION
    Writes a test settings file (restored afterwards), launches Caly.exe on a document, samples process
    memory from launch and per-adapter GPU memory from outside the process, sends a fixed key sequence,
    takes an address-space snapshot and a screenshot, closes Caly gracefully and collects the
    RenderTimings dump. Appends one row to <OutDir>\results.csv.

    Takes keyboard focus for the duration of the run. See README.md.
#>
param(
    [Parameter(Mandatory)] [string] $Label,
    [ValidateSet('gpu', 'software')] [string] $Mode = 'gpu',
    [ValidateSet('scroll', 'zoom', 'idle', 'nodoc')] [string] $Scenario = 'scroll',
    # Extra environment variables for Caly, "NAME=VALUE;NAME=VALUE" - for temporary experiment knobs.
    [string] $EnvVars = '',
    [int] $Run = 1,
    # Window geometry written to the settings file, as JSON members.
    [string] $Window = '"Width":1571,"Height":711,"IsMaximised":true',
    [Parameter(Mandatory)] [string] $OutDir,
    [string] $Exe,
    [string] $Pdf,
    [switch] $SkipVmStat
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CalyBenchWin32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Exe) { $Exe = Join-Path $repo 'Caly.Desktop\bin\Release\net10.0\Caly.exe' }
if (-not $Pdf) { $Pdf = Join-Path $repo 'Caly.Tests\Documents\algo.pdf' }
if (-not (Test-Path $Exe)) { throw "Caly.exe not found at '$Exe'. Build first: dotnet build Caly.Desktop -c Release" }

$calyDir = Join-Path $env:LOCALAPPDATA 'Caly'
$logDir = Join-Path $calyDir 'logs'
$settingsPath = Join-Path $calyDir 'caly_settings'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

if (Get-Process Caly -ErrorAction SilentlyContinue) { throw 'Caly is already running - close it first (single-instance app).' }

$tag = "$Label-$Scenario-$Run"

# Back up the user's settings; restored in the outer finally, after Caly has exited (it rewrites the file on close).
$settingsBackup = $null
if (Test-Path $settingsPath) {
    $settingsBackup = Join-Path ([System.IO.Path]::GetTempPath()) "caly_settings.bench-$([guid]::NewGuid().ToString('N'))"
    Copy-Item $settingsPath $settingsBackup
}

try {
    $software = if ($Mode -eq 'software') { 'true' } else { 'false' }
    $settings = '{' + $Window + ',"PaneSize":323,"UseSoftwareRendering":' + $software + ',"Debug":{"LogRenderTimings":true}}'
    New-Item -ItemType Directory -Force $calyDir | Out-Null
    [System.IO.File]::WriteAllText($settingsPath, $settings)

    # Process memory from launch, sampled out of process: catches the startup spike before the first draw.
    $timeline = Join-Path $OutDir "$tag.timeline.csv"
    $memJob = Start-Job -ArgumentList $timeline -ScriptBlock {
        param($path)
        $deadline = (Get-Date).AddSeconds(30)
        while (-not ($p = Get-Process Caly -ErrorAction SilentlyContinue | Select-Object -First 1)) {
            if ((Get-Date) -gt $deadline) { return }
            Start-Sleep -Milliseconds 20
        }
        $lines = [System.Collections.Generic.List[string]]::new()
        $lines.Add('elapsed_ms,working_set_mb,private_mb')
        while (-not $p.HasExited) {
            try {
                $p.Refresh()
                $ms = [int]((Get-Date) - $p.StartTime).TotalMilliseconds
                $lines.Add("$ms,$([int]($p.WorkingSet64/1MB)),$([int]($p.PrivateMemorySize64/1MB))")
            } catch { }
            Start-Sleep -Milliseconds 200
        }
        [System.IO.File]::WriteAllLines($path, $lines)
    }
    Start-Sleep -Seconds 2   # let the job spin up before launching

    $argLine = if ($Scenario -eq 'nodoc') { '' } else { "`"$Pdf`"" }
    $psi = [System.Diagnostics.ProcessStartInfo]::new($Exe, $argLine)
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = Split-Path $Exe
    foreach ($kv in ($EnvVars -split ';' | Where-Object { $_ })) {
        $k, $v = $kv -split '=', 2
        $psi.Environment[$k] = $v
    }

    $startTime = Get-Date
    $proc = [System.Diagnostics.Process]::Start($psi)
    $procId = $proc.Id

    # GPU memory per adapter (dedicated vs shared), sampled out of process so the app is not perturbed.
    $gpuJob = Start-Job -ArgumentList $procId -ScriptBlock {
        param($procId)
        $max = @{}
        while (Get-Process -Id $procId -ErrorAction SilentlyContinue) {
            try {
                $samples = (Get-Counter "\GPU Process Memory(pid_$($procId)_*)\*" -ErrorAction Stop).CounterSamples
                foreach ($s in $samples) {
                    $inst = ($s.InstanceName -replace '^pid_\d+_', '') + '|' + ($s.Path -split '\\')[-1]
                    if (-not $max.ContainsKey($inst) -or $s.CookedValue -gt $max[$inst]) { $max[$inst] = $s.CookedValue }
                }
            } catch { }
            Start-Sleep -Milliseconds 500
        }
        $max
    }

    function Focus-Caly {
        $proc.Refresh()
        $h = $proc.MainWindowHandle
        if ($h -eq [IntPtr]::Zero) { return $false }
        if ([CalyBenchWin32]::GetForegroundWindow() -eq $h) { return $true }
        [CalyBenchWin32]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)   # Alt down: lifts the foreground lock
        [CalyBenchWin32]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)   # Alt up
        [CalyBenchWin32]::SetForegroundWindow($h) | Out-Null
        Start-Sleep -Milliseconds 150
        return ([CalyBenchWin32]::GetForegroundWindow() -eq $h)
    }

    $focusLost = 0
    function Send([string] $keys, [int] $delayMs) {
        if (-not (Focus-Caly)) { $script:focusLost++; return }
        [System.Windows.Forms.SendKeys]::SendWait($keys)
        Start-Sleep -Milliseconds $delayMs
    }

    function Screenshot([string] $path) {
        $proc.Refresh()
        $r = New-Object CalyBenchWin32+RECT
        [CalyBenchWin32]::GetWindowRect($proc.MainWindowHandle, [ref]$r) | Out-Null
        $w = [Math]::Max(1, $r.Right - $r.Left); $h = [Math]::Max(1, $r.Bottom - $r.Top)
        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
        $small = New-Object System.Drawing.Bitmap $bmp, ([int]($w / 2)), ([int]($h / 2))
        $small.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose(); $small.Dispose()
    }

    $marks = [System.Collections.Generic.List[string]]::new()
    function Mark([string] $name) { $marks.Add("$([int]((Get-Date) - $proc.StartTime).TotalMilliseconds),$name") }

    $killed = $false
    try {
        Start-Sleep -Seconds 14
        Focus-Caly | Out-Null
        Mark 'input_start'

        switch ($Scenario) {
            'scroll' {
                1..45 | ForEach-Object { Send '{PGDN}' 220 }
            }
            'zoom' {
                # Move to dense content first: near-blank front matter records blank tiles and draws nothing.
                1..40 | ForEach-Object { Send '{PGDN}' 150 }
                Start-Sleep -Seconds 2
                # Zoom ladder 1 -> 6 -> 1, crossing tile levels 0..3 each way.
                1..3 | ForEach-Object {
                    1..6 | ForEach-Object { Send '^=' 700 }
                    1..6 | ForEach-Object { Send '^-' 700 }
                }
            }
            { $_ -in 'idle', 'nodoc' } {
                Start-Sleep -Seconds 10
            }
        }

        Mark 'input_end'
        Start-Sleep -Seconds 2
        if (-not $SkipVmStat) {
            & dotnet run (Join-Path $PSScriptRoot 'vmstat.cs') -- $procId 2>$null | Out-File (Join-Path $OutDir "$tag.vmstat.txt")
            Mark 'vmstat'
        }
        Screenshot (Join-Path $OutDir "$tag.png")
        Start-Sleep -Seconds 1
        Mark 'close'
    }
    finally {
        $proc.Refresh()
        if (-not $proc.HasExited) {
            $proc.CloseMainWindow() | Out-Null
            if (-not $proc.WaitForExit(20000)) { $proc.Kill(); $killed = $true }
        }
    }

    $gpu = Receive-Job $gpuJob -Wait -AutoRemoveJob
    Receive-Job $memJob -Wait -AutoRemoveJob | Out-Null
    [System.IO.File]::WriteAllLines((Join-Path $OutDir "$tag.marks.csv"), $marks)
    Start-Sleep -Milliseconds 500

    # RenderTimings writes these at ProcessExit; there is no dump if nothing was drawn (nodoc).
    $txt = Get-ChildItem $logDir -Filter 'render_timings_*.txt' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $startTime } | Sort-Object LastWriteTime | Select-Object -Last 1
    $csv = Get-ChildItem $logDir -Filter 'render_memory_*.csv' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $startTime } | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($txt) { Copy-Item $txt.FullName (Join-Path $OutDir "$tag.txt") }
    if ($csv) { Copy-Item $csv.FullName (Join-Path $OutDir "$tag.csv") }

    function Field([string] $name) {
        if (-not $txt) { return '' }
        $line = Get-Content $txt.FullName | Where-Object { $_ -like "$name*" } | Select-Object -First 1
        if ($line -match ':\s*([\d.]+)') { return $Matches[1] }
        return ''
    }

    # Largest dedicated/shared GPU usage over all adapters, in MB.
    $ded = 0; $shared = 0; $gpuDetail = @()
    if ($gpu) {
        foreach ($k in $gpu.Keys) {
            $mb = [Math]::Round($gpu[$k] / 1MB)
            if ($mb -gt 0) { $gpuDetail += "$k=$mb" }
            if ($k -like '*|dedicated usage' -and $mb -gt $ded) { $ded = $mb }
            if ($k -like '*|shared usage' -and $mb -gt $shared) { $shared = $mb }
        }
    }

    $row = [pscustomobject]@{
        label = $Label; scenario = $Scenario; run = $Run
        draw_ops = Field 'Draw ops'; mean_ms = Field 'Mean draw time'; max_ms = Field 'Max draw time'
        peak_ws_mb = Field 'Peak working set'; peak_private_mb = Field 'Peak private bytes'
        peak_managed_mb = Field 'Peak managed heap'; peak_tiles_mb = Field 'Peak tile cache'; peak_gpu_cache_mb = Field 'Peak GPU cache'
        gpu_dedicated_mb = $ded; gpu_shared_mb = $shared
        focus_lost = $focusLost; killed = $killed
        backend = if ($txt) { ((Get-Content $txt.FullName | Where-Object { $_ -like 'Backend*' }) -split ':\s*', 2)[1] } else { 'NO DUMP' }
        gpu_detail = ($gpuDetail -join ' ')
    }
    $row | Export-Csv (Join-Path $OutDir 'results.csv') -Append -NoTypeInformation
    $row
}
finally {
    if ($settingsBackup) {
        Copy-Item $settingsBackup $settingsPath -Force
        Remove-Item $settingsBackup
    }
    elseif (Test-Path $settingsPath) {
        Remove-Item $settingsPath   # there was none before the run
    }
}
