# Native bridge attribution

`native/windows-tiler.wh.cpp` adapts the Windows 11 classic taskbar menu routing,
taskbar item identification and private symbol signatures from:

- [Taskbar classic context menu](https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-classic-menu.wh.cpp), version 1.0.3, by m417z.
- [Taskbar wheel cycle](https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-wheel-cycle.wh.cpp), by m417z.

These sources are published under the GNU General Public License version 3.
The derived native bridge is GPL-3.0-only; a copy is provided in `native/COPYING`.
The C# helper is original project code and communicates with the bridge as a separate executable.

Windhawk is a separate application; it is not bundled or installed by this repository.
Development syntax checking used the official Windhawk 1.7.3 headers from the same repository.
The Sonatype dependency check could not run because the service lacked authentication;
no security assessment of Windhawk is asserted here.

## Installer build and bundled runtime

The MSI is built using `WixToolset.Sdk` 5.0.2, a build-time dependency under the
[Microsoft Reciprocal License](https://github.com/wixtoolset/wix/blob/v5.0.2/LICENSE.TXT).
This version is pinned before the build-tool maintenance-fee terms introduced with WiX 6;
review both compatibility and licensing before changing the toolset major version.

Self-contained MSI packages embed Microsoft's .NET and Windows Desktop runtimes.
Their original runtime-pack licenses and available third-party notices are installed
as `DOTNET-LICENSE.txt` and `DOTNET-NOTICES.txt`.

The Sonatype checks for the installer toolchain also failed because the service
lacked authentication. Dependency analysis has not been completed; maintainers
should verify the pinned build dependencies through their dependency-review service.
