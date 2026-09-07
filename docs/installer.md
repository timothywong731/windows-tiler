# MSI design and verification

The installer is a per-user x64 Windows Installer package. It owns the single-file C# helper,
Windhawk bridge source, setup guide, attribution files, Start Menu shortcuts and its HKCU installer values.
It does not install a service, require .NET separately, or register/unregister Windhawk mods.

`installer/Version.props` is the default package/runtime version source. The build script validates
MSI's three-field version limits before invoking tools, uses a fresh staging directory, verifies the
complete expected payload, builds with WiX, and emits a SHA-256 sidecar.
The self-contained .NET app may use .NET's normal extraction cache; that runtime-managed cache is not MSI-owned.

Each per-user file component has a stable GUID and HKCU key path. The stable UpgradeCode links versions.
Major upgrades remove the old package after InstallInitialize so Windows Installer can roll back failures.
Rebuilt packages with the same three-field version may replace each other; publish a higher version for each release.
Shortcuts target the fixed installation directory, avoiding cross-component file-key references.
Install always remains per-user; overriding ALLUSERS is explicitly rejected.

Two intentional ICE exceptions are documented in the project: ICE61 warns about same-version upgrades,
and ICE91 warns that a fixed user directory would not support all-users installation. All other validation
remains enabled with warnings treated as errors.

Final uninstall removes only MSI-owned resources and `undo-*.json` in the installation directory.
During an upgrade, the cleanup property stays unset so undo state survives. Windows Installer only removes
an empty installation directory; unrelated files are preserved. Disable the separately registered Windhawk mod
before uninstalling the helper so an active mod does not try to launch a missing executable.

The GitHub workflow builds on Windows Server 2025, whose build meets the Windows 11 API floor.
It does not claim to test Explorer's Windows 11 taskbar. MSI smoke tests perform real installation and inspect
the installed files, registration and shortcuts; repair restores a deliberately removed test file; the upgrade
fixture tests retained state and rejected downgrades; uninstall checks removal plus unrelated-file preservation.

Packages are currently unsigned and published as workflow artifacts, not automatic public GitHub Releases.
