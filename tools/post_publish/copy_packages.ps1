<#
.SYNOPSIS
    Copies the packages in Caly.Desktop\bin\packages\<OS> to Caly.Desktop\bin\output, adding the OS to the file name.
    The originals are left in place.

.EXAMPLE
    bin\packages\Windows\Caly.x64.0.4.0.0.zip -> bin\output\caly.win.x64.0.4.0.0.zip
    bin\packages\Linux\Caly.x64.0.4.0.0.deb   -> bin\output\caly.linux.x64.0.4.0.0.deb

.NOTES
    Run with -WhatIf to preview without moving anything.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$PackagesDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\Caly.Desktop\bin\packages')),
    [string]$OutputDir = (Join-Path (Split-Path $PackagesDir -Parent) 'output')
)

$ErrorActionPreference = 'Stop'

# Package folder name -> OS tag used in the file name.
# Any folder not listed here falls back to its lower-cased name.
$osNames = @{
    'windows' = 'win'
    'linux'   = 'linux'
    'macos'   = 'macos'
}

if (-not (Test-Path -LiteralPath $PackagesDir)) {
    throw "Packages folder not found: $PackagesDir"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

foreach ($osDir in Get-ChildItem -LiteralPath $PackagesDir -Directory) {
    $key = $osDir.Name.ToLowerInvariant()
    $os = if ($osNames.ContainsKey($key)) { $osNames[$key] } else { $key }

    foreach ($file in Get-ChildItem -LiteralPath $osDir.FullName -File) {
        # Caly.x64.0.4.0.0.zip -> caly.win.x64.0.4.0.0.zip
        $dot = $file.Name.IndexOf('.')
        if ($dot -lt 0) {
            Write-Warning "Skipping '$($file.FullName)': unexpected name"
            continue
        }

        $newName = ($file.Name.Substring(0, $dot) + ".$os" + $file.Name.Substring($dot)).ToLowerInvariant()
        $target = Join-Path $OutputDir $newName

        if ($PSCmdlet.ShouldProcess($file.FullName, "Copy to $target")) {
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
            Write-Host "$($osDir.Name)\$($file.Name) -> output\$newName"
        }
    }
}
