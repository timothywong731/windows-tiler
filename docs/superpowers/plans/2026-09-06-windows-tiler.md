# Windows Tiler Implementation Plan

> Execute inline in the current feature branch. Use the executing-plans and test-driven-development workflows.

**Goal:** Add taskbar-group tiling for one or all monitors and undo.
**Architecture:** A native menu bridge supplies exact HWNDs and the click point to an on-demand C# helper.
Pure C# layout and operation logic are isolated from Win32 calls and disk storage.
**Tech Stack:** C# 12, .NET 8, Win32; native bridge for Explorer.
**Spec:** `docs/superpowers/specs/2026-09-06-windows-tiler-design.md`

## Global Constraints

- Windows 11 x64.
- No third-party NuGet packages.
- No managed code inside Explorer.
- Work-area coordinates, per-monitor-V2 DPI awareness, bounded external-window operations.
- Validate before moving; persist undo first; rollback on failures.

## Task 1: Layout and command boundary

Files: `src/WindowsTiler.Core/{Layout,Command}.cs`, `tests/WindowsTiler.Tests/Program.cs`.
Interface: `Layout.Create(IReadOnlyList<WindowSize>, IReadOnlyList<Rect>)` returns `IReadOnlyList<Tile>`.

- [x] Add tests for work-area coverage, negative coordinates, uneven counts, minimum sizes and invalid inputs.
- [x] Run `dotnet run --project tests/WindowsTiler.Tests`; confirm failures against an empty layout implementation.
- [x] Enumerate candidate row counts, divide each row's width with integer boundaries, select the feasible grid closest to 1.6 window aspect ratio.
- [x] Add strict parsing tests and parse `tile --scope monitor|all --point X Y --windows HWND...`, `undo`, and `status`.
- [x] Re-run tests and require exit code zero.

## Task 2: Operation lifecycle and Win32 helper

Files: `src/WindowsTiler.Core/{TilingOperation,UndoStore}.cs`, `src/WindowsTiler/{Program,NativeMethods,DesktopWindows}.cs`.
Interface: Window snapshots contain HWND, PID, process creation time and WINDOWPLACEMENT data; the platform adapter captures, places and restores windows.

- [x] Add operation tests for persistence before mutation, rollback and partial undo.
- [x] Implement atomic JSON state writes and serialize helper invocations with a session-local mutex.
- [x] Implement bounded window inspection, monitor enumeration, virtual desktop filtering, placement and restore.
- [x] Create a test-owned-window smoke harness; verify tiling and restored positions without enumerating user applications.
- [x] Build Release and run core and Windows tests.

## Task 3: Native menu bridge and packaging

Files: `native/`, `scripts/`, `README.md`, `docs/manual-test.md`.

- [x] Resolve the menu choice and document the supported native integration and host requirement.
- [x] Capture actual task-group window handles and add one-monitor, all-monitor and undo actions.
- [x] Launch the helper with a quoted absolute path and numeric arguments; report missing helper.
- [x] Validate bridge compilation when a compatible native toolchain is available.
- [x] Provide publish/install instructions and an explicit menu acceptance matrix.
- [x] Run final builds/tests and report any live integration checks that remain unverified.
