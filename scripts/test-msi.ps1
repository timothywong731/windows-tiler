<# .SYNOPSIS
Exercises real MSI install, repair, optional upgrade/downgrade, and uninstall in a fresh test directory.
Refuses to run when an existing Windows Tiler MSI or Start Menu installation could be affected.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$MsiPath, [string]$UpgradeMsiPath)
$ErrorActionPreference = 'Stop'
$tilerMsi = (Resolve-Path -LiteralPath $MsiPath).Path
$tilerCom = New-Object -ComObject WindowsInstaller.Installer
$tilerUpgradeCode = '{76F85DCF-DDFD-4D92-A477-DF22FE3CBB37}'
$tilerShortcutDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Windows Tiler'
$tilerRegistry = 'HKCU:\Software\WindowsTiler\Installer'
if (@($tilerCom.RelatedProducts($tilerUpgradeCode)).Count -gt 0 -or (Test-Path -LiteralPath $tilerShortcutDir) -or (Test-Path -LiteralPath $tilerRegistry)) {
    throw 'MSI smoke tests require an account without an existing Windows Tiler MSI/Start Menu installation.'
}
$tilerTestRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) '.local\msi-tests'))
$tilerRun = Join-Path $tilerTestRoot ([guid]::NewGuid().ToString('N'))
$tilerInstall = Join-Path $tilerRun 'installed helper'
New-Item -ItemType Directory -Path $tilerRun -Force | Out-Null

# Read package metadata without installing or triggering Windows Installer product repair queries.
function Get-TilerMsiProperty {
    param([string]$Path, [string]$Name)
    $database = $tilerCom.OpenDatabase($Path, 0)
    try {
        $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$Name'")
        try {
            [void]$view.Execute()
            $record = $view.Fetch()
            if (-not $record) { throw "Missing MSI property: $Name" }
            try { return $record.StringData(1) }
            finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        }
        finally { [void]$view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
    }
    finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }
}

# Run Windows Installer quietly and preserve verbose logs for local debugging and CI artifacts.
function Invoke-TilerMsi {
    param([string]$Operation, [string]$Target, [string]$LogName, [string]$Properties = '', [switch]$ExpectFailure)
    $log = Join-Path $tilerRun "$LogName.log"
    $arguments = "$Operation `"$Target`" /qn /norestart /l*v `"$log`" $Properties"
    $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\msiexec.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(180000)) { throw "Windows Installer timed out. Inspect $log before running another test." }
    $code = $process.ExitCode
    $process.Dispose()
    if ($ExpectFailure) {
        if ($code -notin @(1603, 1638)) { throw "Downgrade unexpectedly returned $code. See $log" }
    }
    elseif ($code -notin @(0, 3010)) { throw "MSI $Operation failed with exit code $code. See $log" }
}

# Assert externally observable installation state with a useful failure message.
function Assert-TilerInstall {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$tilerOriginalProduct = Get-TilerMsiProperty -Path $tilerMsi -Name 'ProductCode'
$tilerProductsToClean = @($tilerOriginalProduct)
try {
    Invoke-TilerMsi -Operation '/i' -Target $tilerMsi -LogName 'install' -Properties "INSTALLFOLDER=`"$tilerInstall`""
    $tilerRequired = @('WindowsTiler.exe','windows-tiler.wh.cpp','COPYING','Setup.txt','THIRD-PARTY-NOTICES.md','DOTNET-LICENSE.txt','DOTNET-NOTICES.txt')
    foreach ($file in $tilerRequired) {
        Assert-TilerInstall (Test-Path -LiteralPath (Join-Path $tilerInstall $file)) "Missing installed file: $file"
    }
    Assert-TilerInstall (@($tilerCom.RelatedProducts($tilerUpgradeCode)).Count -eq 1) 'MSI product registration is missing or duplicated.'
    Assert-TilerInstall (Test-Path -LiteralPath (Join-Path $tilerShortcutDir 'Uninstall Windows Tiler.lnk')) 'Uninstall shortcut is missing.'
    Assert-TilerInstall (Test-Path -LiteralPath $tilerRegistry) 'Per-user installation registration is missing.'
    Write-Output 'PASS MSI installation and registration'

    # Removing only a test-installed file verifies repair restores payload, rather than merely returning success.
    $tilerBridge = Join-Path $tilerInstall 'windows-tiler.wh.cpp'
    $tilerBridgeHash = (Get-FileHash -LiteralPath $tilerBridge).Hash
    Remove-Item -LiteralPath $tilerBridge
    Invoke-TilerMsi -Operation '/fa' -Target $tilerMsi -LogName 'repair'
    Assert-TilerInstall ((Get-FileHash -LiteralPath $tilerBridge).Hash -eq $tilerBridgeHash) 'Repair failed to restore the original bridge.'
    Write-Output 'PASS MSI repair'

    $tilerUndo = Join-Path $tilerInstall 'undo-12345.json'
    Set-Content -LiteralPath $tilerUndo -Value '{}'
    $tilerUnrelated = Join-Path $tilerInstall 'unrelated-user-file.txt'
    Set-Content -LiteralPath $tilerUnrelated -Value 'Preserve this file on uninstall.'
    $tilerCurrentProduct = $tilerOriginalProduct
    if ($UpgradeMsiPath) {
        $tilerUpgradeMsi = (Resolve-Path -LiteralPath $UpgradeMsiPath).Path
        $tilerUpgradeProduct = Get-TilerMsiProperty -Path $tilerUpgradeMsi -Name 'ProductCode'
        $tilerProductsToClean += $tilerUpgradeProduct
        Invoke-TilerMsi -Operation '/i' -Target $tilerUpgradeMsi -LogName 'upgrade' -Properties "INSTALLFOLDER=`"$tilerInstall`""
        $tilerCurrentProduct = $tilerUpgradeProduct
        Assert-TilerInstall (@($tilerCom.RelatedProducts($tilerUpgradeCode)).Count -eq 1) 'Upgrade left duplicate Installed Apps entries.'
        Assert-TilerInstall (Test-Path -LiteralPath $tilerUndo) 'Upgrade deleted undo state.'
        Invoke-TilerMsi -Operation '/i' -Target $tilerMsi -LogName 'downgrade' -Properties "INSTALLFOLDER=`"$tilerInstall`"" -ExpectFailure
        Assert-TilerInstall (@($tilerCom.RelatedProducts($tilerUpgradeCode)) -contains $tilerUpgradeProduct) 'Rejected downgrade damaged the newer installation.'
        Write-Output 'PASS MSI upgrade, undo preservation and downgrade rejection'
    }

    Invoke-TilerMsi -Operation '/x' -Target $tilerCurrentProduct -LogName 'uninstall'
    foreach ($file in $tilerRequired) {
        Assert-TilerInstall (-not (Test-Path -LiteralPath (Join-Path $tilerInstall $file))) "Uninstall left a packaged file: $file"
    }
    Assert-TilerInstall (-not (Test-Path -LiteralPath $tilerShortcutDir)) 'Uninstall left Start Menu entries.'
    Assert-TilerInstall (-not (Test-Path -LiteralPath $tilerRegistry)) 'Uninstall left installer registry values.'
    Assert-TilerInstall (-not (Test-Path -LiteralPath $tilerUndo)) 'Uninstall left an undo snapshot.'
    Assert-TilerInstall (Test-Path -LiteralPath $tilerUnrelated) 'Uninstall deleted unrelated user data.'
    Assert-TilerInstall (@($tilerCom.RelatedProducts($tilerUpgradeCode)).Count -eq 0) 'Uninstall left a registered MSI.'
    Write-Output 'PASS MSI uninstall, state cleanup and unrelated-file preservation'
}
finally {
    # Clean only products introduced by this test, and retain all logs even on failure.
    foreach ($product in $tilerProductsToClean) {
        if (@($tilerCom.RelatedProducts($tilerUpgradeCode)) -contains $product) {
            Invoke-TilerMsi -Operation '/x' -Target $product -LogName 'cleanup'
        }
    }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($tilerCom)
    Write-Output "MSI test logs: $tilerRun"
}
