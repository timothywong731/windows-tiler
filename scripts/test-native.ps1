<# .SYNOPSIS
Compiles against supplied Windhawk headers and tests menus outside Explorer.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Headers)
$ErrorActionPreference = 'Stop'
$tilerRoot = Split-Path -Parent $PSScriptRoot
$tilerHeaders = (Resolve-Path -LiteralPath $Headers).Path
New-Item -ItemType Directory -Path (Join-Path $tilerRoot '.local') -Force | Out-Null
Push-Location -LiteralPath $tilerRoot
try {
    & g++ -std=c++20 -c -DUNICODE -D_UNICODE -DWH_MOD '-DWH_MOD_ID=L"windows-tiler"' -I $tilerHeaders native/windows-tiler.wh.cpp -o .local/windows-tiler.o
    if ($LASTEXITCODE -ne 0) { throw 'Native bridge compilation failed.' }
    & windres -I tests/native tests/native/menu-tests.rc -o .local/menu-tests-resource.o
    if ($LASTEXITCODE -ne 0) { throw 'Native test manifest compilation failed.' }
    & g++ -std=c++20 -DUNICODE -D_UNICODE -I $tilerHeaders tests/native/menu-tests.cpp .local/menu-tests-resource.o -o .local/menu-tests.exe -lcomctl32 -static
    if ($LASTEXITCODE -ne 0) { throw 'Native menu test compilation failed.' }
    & .local/menu-tests.exe
    if ($LASTEXITCODE -ne 0) { throw "Native menu tests failed: $LASTEXITCODE" }
}
finally { Pop-Location }
