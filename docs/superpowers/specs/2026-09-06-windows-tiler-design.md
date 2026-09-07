# Windows Tiler

Windows 11 x64 utility for tiling the windows represented by a taskbar application group.
The taskbar integration passes the group's actual window handles to a short-lived C# helper.
Process-name matching is not used: browsers and packaged apps can have multiple taskbar groups.

## Behavior

- Tile on this monitor uses the work area of the monitor containing the clicked taskbar button.
- Tile across all monitors balances windows across connected work areas, ordered left-to-right then top-to-bottom.
- An adaptive grid fills each work area without overlap and with no empty cells in its final row.
- Respect minimum tracking sizes; reject an infeasible arrangement before changing any windows.
- Include minimized and maximized group members, restoring them for tiling.
- Exclude hidden, cloaked, non-resizable, owned/dialog, and other virtual desktop windows.
- Undo restores the last operation's original window placements, including minimized/maximized state.
- Recheck identities before moving/restoring. Skip closed windows during undo. Save undo before moving.
- Report failures; if a move fails, attempt rollback and preserve any unrecovered undo entries.

## Components

`WindowsTiler.Core`: pure integer layout, strict command parsing, and operation coordination.
`WindowsTiler`: per-monitor-V2-aware Win32 C# helper, with no third-party NuGet packages.
Native Explorer bridge: required for an existing Windows menu; managed code is not loaded into Explorer.
The bridge targets Windows' existing classic group menu via Windhawk. Ordinary right-click opens
that menu, with Shift + right-click retaining the normal Jump List. The normal Jump List itself is
not modified. This was the stated default while implementation proceeded.

## Verification

Run dependency-free executable tests for coverage, non-overlap, negative coordinates, monitor distribution,
minimum sizes, input validation, state persistence and rollback. Run Windows smoke tests on windows
created by the test harness only. Build in Release with warnings as errors.
Native menu integration requires a live taskbar acceptance check; a successful C# build does not prove it.

## References

- https://learn.microsoft.com/en-us/windows/win32/shell/taskbar-extensions
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowplacement
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
- https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager
- https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-classic-menu.wh.cpp
