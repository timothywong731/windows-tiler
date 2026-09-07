# Windows 11 acceptance checks

Run these after publishing the helper and enabling the native bridge in Windhawk.
Record the OS build, taskbar module versions, Windhawk version and monitor scales.
These are manual checks, not claims that this checkout has passed them.

| Check | Expected behavior |
| --- | --- |
| Right-click a group with 3 windows | Existing classic menu contains Windows Tiler submenu |
| Shift + right-click the same group | Normal Windows Jump List opens |
| Existing Close/minimize commands | Windows handles the command normally |
| Tile on this monitor | Only that group's eligible windows fill the clicked monitor's work area |
| Repeat from a secondary monitor | One-monitor target follows where the menu opens |
| Tile across all monitors | Every selected window appears once, with balanced counts |
| Monitor left/above primary | Negative coordinates are respected |
| 100% + 150%, portrait + landscape | Tiles fit work areas or a clear failure rolls back |
| Undo ordinary/minimized/maximized windows | Positions and show states restore |
| Close one window, then undo | Remaining windows restore; closed window is skipped |
| Open a fixed-size dialog | Dialog is excluded; normal windows still work |
| Windows on another virtual desktop | They remain untouched |
| Distinct groups from the same browser executable | Only the clicked group moves |
| Missing helper | Action explains the helper path problem |
| Missing taskbar symbols | Initialization fails or logs deferred view-hook failure |
| Disable/re-enable; restart Explorer | Hooks unload/reload and ordinary menus work |
| Read-only undo file or app refusing resize | Failure is visible; rollback retains unresolved recovery |

Automated validation is narrower: `scripts/test.ps1 -Windows` checks controlled windows,
and `scripts/test-native.ps1` checks menu handling without injecting into Explorer.
