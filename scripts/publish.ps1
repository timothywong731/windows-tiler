<# .SYNOPSIS
Publishes the framework-dependent x64 helper and native bridge source.
#>
[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\WindowsTiler'))
$ErrorActionPreference = 'Stop'
$tilerRoot = Split-Path -Parent $PSScriptRoot
$tilerOutput = [IO.Path]::GetFullPath($OutputDirectory)
& dotnet publish (Join-Path $tilerRoot 'src\WindowsTiler\WindowsTiler.csproj') -c Release --no-self-contained -o $tilerOutput --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $tilerRoot 'native\windows-tiler.wh.cpp') -Destination $tilerOutput
Copy-Item -LiteralPath (Join-Path $tilerRoot 'native\COPYING') -Destination $tilerOutput
Copy-Item -LiteralPath (Join-Path $tilerRoot 'THIRD-PARTY-NOTICES.md') -Destination $tilerOutput
Write-Output "Published helper and bridge source to $tilerOutput"
