<#
.SYNOPSIS
    Summarises the memory timelines in a results folder: one row per run.

.DESCRIPTION
    startup_*   peak from launch until input starts (device creation, window sizing, document load).
                Not a fixed window: launch speed varies, e.g. the first launch after a rebuild is slower.
    pre_input_* peak in the 3 s before input starts (steady state with the document open)
    input_*     peak while the workload runs (plus 1.5 s)
    run_*       peak over the whole run
    os_peak_ws  Process.PeakWorkingSet64 from the in-app RenderTimings dump

    Also writes <Dir>\summary.csv.
#>
param([Parameter(Mandatory)] [string] $Dir)

$rows = foreach ($t in Get-ChildItem $Dir -Filter '*.timeline.csv') {
    $tag = $t.Name -replace '\.timeline\.csv$', ''
    $parts = $tag -split '-'
    $run = $parts[-1]; $scenario = $parts[-2]; $label = ($parts[0..($parts.Length - 3)] -join '-')

    $tl = Import-Csv $t.FullName | ForEach-Object { [pscustomobject]@{ ms = [int]$_.elapsed_ms; ws = [int]$_.working_set_mb; priv = [int]$_.private_mb } }

    $marks = @{}
    $marksFile = Join-Path $Dir "$tag.marks.csv"
    if (Test-Path $marksFile) { Get-Content $marksFile | ForEach-Object { $ms, $name = $_ -split ','; $marks[$name] = [int]$ms } }
    $start = $marks['input_start']; $end = $marks['input_end']

    $pre = $tl | Where-Object { $_.ms -lt $start -and $_.ms -gt ($start - 3000) }
    $during = $tl | Where-Object { $_.ms -ge $start -and $_.ms -le $end + 1500 }
    $startup = $tl | Where-Object { $_.ms -lt $start }

    $dump = Join-Path $Dir "$tag.txt"
    $osPeak = if (Test-Path $dump) { (Get-Content $dump | Where-Object { $_ -like 'Peak working set*' }) -replace '.*:\s*(\d+).*', '$1' } else { '' }

    [pscustomobject]@{
        label = $label; scenario = $scenario; run = $run
        startup_peak_ws = ($startup | Measure-Object ws -Maximum).Maximum
        startup_peak_priv = ($startup | Measure-Object priv -Maximum).Maximum
        pre_input_ws = ($pre | Measure-Object ws -Maximum).Maximum
        pre_input_priv = ($pre | Measure-Object priv -Maximum).Maximum
        input_peak_ws = ($during | Measure-Object ws -Maximum).Maximum
        input_peak_priv = ($during | Measure-Object priv -Maximum).Maximum
        run_peak_ws = ($tl | Measure-Object ws -Maximum).Maximum
        run_peak_priv = ($tl | Measure-Object priv -Maximum).Maximum
        os_peak_ws = $osPeak
    }
}

$rows = $rows | Sort-Object scenario, label, run
$rows | Export-Csv (Join-Path $Dir 'summary.csv') -NoTypeInformation
$rows | Format-Table -AutoSize | Out-String -Width 250
