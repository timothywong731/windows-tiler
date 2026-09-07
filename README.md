# Windows Tiler

A lightweight Windows 11 utility for tiling a taskbar application's windows on one monitor or across all monitors.
The engine is C#/.NET 8. A native Windhawk bridge adds commands to Windows' **existing classic taskbar group menu**.

**Status: initial implementation.** The C# engine and native menu handling have automated tests.
The bridge still needs acceptance testing inside Explorer on the target Windows build.

## Interaction

Right-click a running application's taskbar button, then choose **Windows Tiler**:

- **Tile on this monitor** uses the work area of the monitor where the menu opens.
- **Tile across all monitors** distributes that group's windows evenly across displays, ordered by position.
- **Undo last tiling** restores the previous operation's positions and minimized/maximized states.

Ordinary right-click opens the classic group menu. **Shift + right-click** opens the normal Jump List.
The commands are not injected into the modern Jump List. Existing classic menu commands remain Windows-owned.
Only windows from the clicked group are submitted; the bridge does not select other windows by process name.
The helper runs for one command and exits, with no persistent managed tray application.

## Build and test

Requires Windows 11 x64 and the .NET 8 SDK (8.0.400 or a newer .NET 8 feature band).
There are no third-party NuGet packages.

```powershell
.\scripts\test.ps1 -Windows
.\scripts\publish.ps1
```

The `-Windows` tests briefly create their own windows, tile them, and close them. They do not enumerate or move your application windows.
Without the flag, the script builds everything and runs the core tests.
Tests use executable harnesses; use the script, rather than `dotnet test`.

Native compilation and menu tests require MinGW-w64 `g++`, `windres`, and the
[official Windhawk 1.7.3 headers](https://github.com/ramensoftware/windhawk-mods/tree/main/.vscode/windhawk_headers_1.7.3):

```powershell
.\scripts\test-native.ps1 -Headers C:\path\to\windhawk_headers_1.7.3
```

The native test uses real menu handles and window subclassing outside Explorer.
It verifies command selection, ID collisions, existing commands and cleanup; it does not verify private taskbar hooks.

## Install / uninstall (MSI)

Download the `WindowsTiler-<version>-x64-msi` artifact from a successful
[Build and test MSI workflow run](https://github.com/timothywong731/windows-tiler/actions/workflows/installer.yml).
Extract it and double-click `WindowsTiler-<version>-x64.msi` to install for your Windows account.
The MSI includes the .NET runtime, the native bridge source, licenses, and a setup guide.
It installs to `%LOCALAPPDATA%\WindowsTiler` and adds Start Menu entries and a Windows Installed Apps entry.

Open **Windows Tiler > Set up taskbar menu** in the Start Menu to enable the bridge in Windhawk.
Windhawk remains a separate installation and activation step; the MSI does not change Explorer hooks.

To uninstall, first disable the Windows Tiler mod in Windhawk, then use
**Settings > Apps > Installed apps > Windows Tiler > Uninstall**, or the Start Menu uninstall entry.
Uninstall removes packaged files, shortcuts, installer registration and known undo snapshots.
It preserves unrelated files and does not uninstall Windhawk or delete its separately registered mod.
Newer MSI versions upgrade in place; undo snapshots survive upgrades. A lower version is rejected.

For unattended installation, repair, and removal:

```powershell
msiexec /i "WindowsTiler-0.1.2-x64.msi" /qn /norestart
msiexec /fa "WindowsTiler-0.1.2-x64.msi" /qn /norestart
msiexec /x "WindowsTiler-0.1.2-x64.msi" /qn /norestart
```

These initial packages are unsigned. Workflow artifacts include a SHA-256 checksum.

## Build the MSI / CI

```powershell
.\scripts\build-msi.ps1                         # installer/Version.props supplies the version
.\scripts\build-msi.ps1 -Version 1.2.3
```

The MSI and checksum are written to `artifacts/installer`. The build restores WiX Toolset SDK 5.0.2
and publishes a self-contained x64 single-file helper with the runtime pinned in `installer/Version.props`.
The application itself still has no third-party NuGet references. Runtime licenses/notices are included.
The package project is separate from `WindowsTiler.sln`; use `build-msi.ps1` to supply its published payload.

GitHub Actions runs on pushes to `main`, pull requests to `main`, `v*` tags, and manual dispatch.
It builds the application, runs core tests, builds the distributable plus a newer test fixture,
and tests install, file repair, upgrade, downgrade rejection, uninstall, and data preservation.
Only the tested distributable MSI is uploaded, with logs retained for diagnosis.
Artifacts are retained for 30 days; this workflow does not automatically create GitHub Releases.
Tag versions must have three numeric fields, such as `v1.2.3`. Manual runs can supply a version.

For local lifecycle testing on an account without an existing Windows Tiler MSI/Start Menu installation:

```powershell
.\scripts\build-msi.ps1 -Version 0.1.3 -OutputDirectory .local/upgrade-fixture
.\scripts\test-msi.ps1 -MsiPath artifacts/installer/WindowsTiler-0.1.2-x64.msi -UpgradeMsiPath .local/upgrade-fixture/WindowsTiler-0.1.3-x64.msi
```

The test uses a unique installation directory, temporarily registers the MSI, and uninstalls it afterwards.
It refuses to replace an existing product. Logs remain under `.local/msi-tests`.
Native Explorer integration still requires the separate [acceptance checks](docs/manual-test.md).

## Portable developer setup

1. Run the build/test and publish commands above.
2. Run `.\scripts\install-helper.ps1`. This copies the helper to `%LOCALAPPDATA%\WindowsTiler`.
3. Install [Windhawk](https://windhawk.net/) separately, if needed. This repository does not install it or enable hooks automatically.
4. In Windhawk, select **Create a new mod**, replace its source with `native/windows-tiler.wh.cpp`, then compile and enable it.
5. Keep the default helper path, or set **Windows Tiler helper path** to the executable's absolute path.
6. Right-click a group with multiple windows and follow [the acceptance checks](docs/manual-test.md).

The portable executable from `publish.ps1` requires the .NET 8 **Desktop Runtime x64** on the destination machine.
The MSI build includes that runtime.
Keep all published helper files together. Disable other mods that swap taskbar right-click and Shift + right-click.
Disable Windows Tiler in Windhawk to restore ordinary menu behavior; the helper has no startup entry or service.

## Boundaries

- Targets the stock Windows 11 x64 taskbar; ARM64, ExplorerPatcher and StartAllBack are not supported.
- Explorer integration uses private symbols and may need updates when Windows changes. Unresolved required symbols prevent initialization.
- Excludes non-resizable, hidden, cloaked, owned/dialog, tool, unresponsive and other virtual-desktop windows.
- Elevated or protected windows may reject inspection or movement; there is no automatic elevation.
- Restores minimized/maximized windows before tiling. Minimum sizes are respected; infeasible grids are rejected before moving anything.
- Uses physical pixels and monitor work areas. Minimum-size planning across mixed-DPI displays is conservative; apps that reject their destination trigger rollback.
- Balances window counts across monitors. A fixed assignment can be infeasible even if a different assignment might fit; it does not perform general rectangle packing.
- Stores undo in `%LOCALAPPDATA%\WindowsTiler\undo-<session>.json`. Only the most recent operation is undoable.
- Undo checks HWND, PID, process creation time and current desktop membership. Closed/reassigned windows are skipped. HWND reuse within the same process remains a native identity limitation.
- Restores Windows workspace coordinates, preserving taskbar offsets. It cannot reproduce a removed monitor or prevent an app overriding its own placement.
- Limits operations to 128 windows. Native calls and completion checks are bounded per window. Failures trigger rollback and retain entries still needing recovery.

## Source map

- `src/WindowsTiler.Core`: layout, parsing, recovery and persistence.
- `src/WindowsTiler`: per-monitor-aware Win32 adapter and executable.
- `native/windows-tiler.wh.cpp`: menu integration; never loads .NET into Explorer.
- `tests`: core, real-window and native menu tests.

Classes, methods, hooks and non-obvious platform operations are documented inline.
The native bridge derives from GPLv3 Windhawk examples; see [third-party notices](THIRD-PARTY-NOTICES.md).
