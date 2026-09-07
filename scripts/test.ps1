<# .SYNOPSIS
Builds and runs core tests, with optional test-owned desktop windows.
#>
[CmdletBinding()]
param([switch]$Windows)
$ErrorActionPreference = 'Stop'
$tilerRoot = Split-Path -Parent $PSScriptRoot

# Native process failures need explicit handling in PowerShell.
function Invoke-TilerDotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

Push-Location -LiteralPath $tilerRoot
try {
    Invoke-TilerDotnet -Arguments @('build', 'WindowsTiler.sln', '-c', 'Release', '--nologo')
    Invoke-TilerDotnet -Arguments @('run', '--project', 'tests/WindowsTiler.Tests', '-c', 'Release', '--no-build')
    if ($Windows) {
        Invoke-TilerDotnet -Arguments @('run', '--project', 'tests/WindowsTiler.WindowsTests', '-c', 'Release', '--no-build')
    }
}
finally { Pop-Location }
