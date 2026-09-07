<# .SYNOPSIS
Copies a published helper per user; does not install Windhawk or enable Explorer hooks.
#>
[CmdletBinding()]
param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'WindowsTiler'))
$ErrorActionPreference = 'Stop'
$tilerRoot = Split-Path -Parent $PSScriptRoot
$tilerSource = Join-Path $tilerRoot 'artifacts\WindowsTiler'
if (-not (Test-Path -LiteralPath (Join-Path $tilerSource 'WindowsTiler.exe'))) {
    throw 'Run scripts\publish.ps1 before installing the helper.'
}
$tilerDestination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $tilerDestination -Force | Out-Null
Get-ChildItem -LiteralPath $tilerSource -File | Copy-Item -Destination $tilerDestination -Force
Write-Output "Helper installed: $(Join-Path $tilerDestination 'WindowsTiler.exe')"
Write-Output 'Enable native\windows-tiler.wh.cpp in Windhawk and set helperPath to this executable.'
