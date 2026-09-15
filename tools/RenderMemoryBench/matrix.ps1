<#
.SYNOPSIS
    Runs run.ps1 over configs x scenarios x runs, alternating config order between runs.

.EXAMPLE
    .\matrix.ps1 -OutDir results\baseline -Runs 3 -Scenarios scroll,zoom -Configs @(
        'sw|software|',
        'gpu|gpu|',
        'gpu-wgl|gpu|CALY_EXP_RENDER=Wgl')

    Each config is "label|mode|ENV=VALUE;ENV=VALUE" - mode is gpu or software; the environment
    variables are passed to Caly for temporary experiment knobs.
#>
param(
    [Parameter(Mandatory)] [string] $OutDir,
    [Parameter(Mandatory)] [string[]] $Configs,
    [string[]] $Scenarios = @('scroll', 'zoom'),
    [int] $Runs = 3,
    [string] $Window = '"Width":1571,"Height":711,"IsMaximised":true'
)

$ErrorActionPreference = 'Stop'

foreach ($run in 1..$Runs) {
    foreach ($scenario in $Scenarios) {
        # Alternate config order per run to spread thermal and background drift across configs.
        $ordered = if ($run % 2 -eq 0) { $Configs[($Configs.Length - 1)..0] } else { $Configs }
        foreach ($cfg in $ordered) {
            $label, $mode, $envVars = $cfg -split '\|', 3
            Write-Host "=== run $run  $scenario  $label"
            & (Join-Path $PSScriptRoot 'run.ps1') -Label $label -Mode $mode -Scenario $scenario -EnvVars $envVars `
                -Run $run -Window $Window -OutDir $OutDir | Out-Null
            Start-Sleep -Seconds 3
        }
    }
}

& (Join-Path $PSScriptRoot 'summarize.ps1') -Dir $OutDir
