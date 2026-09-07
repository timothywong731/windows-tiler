<# .SYNOPSIS
Publishes a self-contained helper and builds a versioned x64 MSI using a pinned WiX SDK.
#>
[CmdletBinding()]
param([string]$Version, [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\installer'))
$ErrorActionPreference = 'Stop'
$tilerRoot = Split-Path -Parent $PSScriptRoot
[xml]$tilerVersions = Get-Content -LiteralPath (Join-Path $tilerRoot 'installer\Version.props') -Raw
if (-not $Version) { $Version = $tilerVersions.Project.PropertyGroup.InstallerVersion.InnerText }
if ($Version -notmatch '^\d{1,3}\.\d{1,3}\.\d{1,5}$') { throw 'Use an MSI version such as 1.2.3 (three numeric fields).' }
$tilerParts = $Version.Split('.') | ForEach-Object { [int]$_ }
if ($tilerParts[0] -gt 255 -or $tilerParts[1] -gt 255 -or $tilerParts[2] -gt 65535) { throw 'MSI version limits are 255.255.65535.' }
$Version = $tilerParts -join '.'
$tilerRuntime = [string]$tilerVersions.Project.PropertyGroup.BundledRuntimeVersion
$tilerOutput = [IO.Path]::GetFullPath($OutputDirectory)
# Each build gets a fresh payload directory, so old publish files cannot leak into a release.
$tilerStageRoot = [IO.Path]::GetFullPath((Join-Path $tilerRoot '.local\msi-build'))
$tilerStage = Join-Path $tilerStageRoot ([guid]::NewGuid().ToString('N'))
$tilerPayload = Join-Path $tilerStage 'payload'
New-Item -ItemType Directory -Path $tilerPayload,$tilerOutput -Force | Out-Null

# Invoke dotnet with argument arrays and fail immediately on native command errors.
function Invoke-TilerBuild {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

try {
    Invoke-TilerBuild -Arguments @('publish', (Join-Path $tilerRoot 'src\WindowsTiler\WindowsTiler.csproj'),
        '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $tilerPayload, '--nologo',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=None', '-p:DebugSymbols=false', "-p:RuntimeFrameworkVersion=$tilerRuntime", "-p:Version=$Version")
    Copy-Item -LiteralPath (Join-Path $tilerRoot 'native\windows-tiler.wh.cpp'),(Join-Path $tilerRoot 'native\COPYING'),
        (Join-Path $tilerRoot 'THIRD-PARTY-NOTICES.md'),(Join-Path $tilerRoot 'installer\Setup.txt') -Destination $tilerPayload

    # Retain notices from both runtime packs that are embedded into the single-file helper.
    $tilerNugetLine = & dotnet nuget locals global-packages --list
    if ($LASTEXITCODE -ne 0) { throw 'Could not locate the restored runtime licenses.' }
    $tilerNugetRoot = ($tilerNugetLine -join "`n").Split(':', 2)[1].Trim()
    foreach ($tilerPack in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        $tilerPackRoot = Join-Path $tilerNugetRoot "$tilerPack\$tilerRuntime"
        $tilerLicense = Get-ChildItem -LiteralPath $tilerPackRoot -File | Where-Object Name -Match '^LICENSE(\.TXT)?$' | Select-Object -First 1
        $tilerNotice = Get-ChildItem -LiteralPath $tilerPackRoot -File | Where-Object Name -Match 'THIRD.?PARTY.?NOTICES' | Select-Object -First 1
        if (-not $tilerLicense) { throw "Runtime license missing from $tilerPackRoot" }
        Add-Content -LiteralPath (Join-Path $tilerPayload 'DOTNET-LICENSE.txt') -Value ("$tilerPack $tilerRuntime`r`n" + (Get-Content -LiteralPath $tilerLicense.FullName -Raw))
        if ($tilerNotice) {
            Add-Content -LiteralPath (Join-Path $tilerPayload 'DOTNET-NOTICES.txt') -Value ("$tilerPack $tilerRuntime`r`n" + (Get-Content -LiteralPath $tilerNotice.FullName -Raw))
        }
    }
    # Fail if publishing changes its contract instead of silently shipping an incomplete application.
    $tilerExpected = @('WindowsTiler.exe','windows-tiler.wh.cpp','COPYING','Setup.txt','THIRD-PARTY-NOTICES.md','DOTNET-LICENSE.txt','DOTNET-NOTICES.txt')
    $tilerActual = @(Get-ChildItem -LiteralPath $tilerPayload -File | Select-Object -ExpandProperty Name)
    if (Compare-Object ($tilerExpected | Sort-Object) ($tilerActual | Sort-Object)) { throw 'Unexpected single-file publish payload. Update the MSI file list.' }
    if (Get-ChildItem -LiteralPath $tilerPayload -Directory) { throw 'Unexpected publish subdirectories. Update the MSI file list.' }
    Invoke-TilerBuild -Arguments @('build', (Join-Path $tilerRoot 'installer\WindowsTiler.Installer.wixproj'),
        '-c', 'Release', '--nologo', "-p:InstallerVersion=$Version", "-p:PayloadDir=$tilerPayload", "-p:OutputPath=$tilerOutput\")
    $tilerMsi = Join-Path $tilerOutput "WindowsTiler-$Version-x64.msi"
    if (-not (Test-Path -LiteralPath $tilerMsi)) { throw 'WiX did not produce the expected MSI.' }
    $tilerHash = (Get-FileHash -LiteralPath $tilerMsi -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$tilerMsi.sha256" -Value "$tilerHash  $([IO.Path]::GetFileName($tilerMsi))"
    Write-Output "Built $tilerMsi"
}
finally {
    # Delete only this invocation's generated GUID directory, inside the known staging root.
    $tilerResolvedStage = [IO.Path]::GetFullPath($tilerStage)
    if ($tilerResolvedStage.StartsWith($tilerStageRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($tilerResolvedStage) -match '^[a-f0-9]{32}$' -and (Test-Path -LiteralPath $tilerResolvedStage)) {
        Remove-Item -LiteralPath $tilerResolvedStage -Recurse -Force
    }
}
