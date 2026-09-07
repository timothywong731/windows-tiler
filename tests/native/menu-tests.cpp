// Windhawk's editing mode supplies inert host APIs. These tests exercise real
// Win32 HMENU/subclass behavior without registering hooks or loading into Explorer.
#define WH_MOD
#define WH_EDITING
#include "../../native/windows-tiler.wh.cpp"
#include <iostream>
#include <stdexcept>

/// Fails the executable when a user-visible menu contract is violated.
void Check(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

/// Tests selection, collision avoidance, native-item preservation, cleanup, and notification dispatch.
int main() {
    HWND owner = CreateWindowExW(0, L"STATIC", L"Windows Tiler menu test", WS_OVERLAPPED,
        0, 0, 100, 100, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    HMENU menu = CreatePopupMenu();
    try {
        Check(owner && menu, "Test fixture creation failed.");
        AppendMenuW(menu, MF_STRING, 0xB100, L"Existing Windows command");
        MenuContext context{{owner}, {0, 0}};
        g_menuContext = &context;
        BOOL result = WithTilingMenu(menu, TPM_RETURNCMD, -1200, 900, owner, [&]() -> BOOL {
            Check(GetMenuItemCount(menu) == 3, "Submenu not appended to the original menu.");
            HMENU submenu = GetSubMenu(menu, 2);
            Check(submenu && GetMenuItemCount(submenu) == 3, "Missing tiling commands.");
            Check(context.commands[0] != 0xB100, "Native command ID collision.");
            return static_cast<BOOL>(GetMenuItemID(submenu, 1));
        });
        Check(result == 0 && context.selected == context.commands[1], "Tiling command leaked into Explorer dispatch.");
        Check(context.point.x == -1200 && context.point.y == 900, "Wrong monitor point.");
        Check(GetMenuItemCount(menu) == 1 && GetMenuItemID(menu, 0) == 0xB100, "Original menu was not restored.");

        context.selected = 0;
        result = WithTilingMenu(menu, TPM_RETURNCMD, 0, 0, owner, []() -> BOOL { return 0xB100; });
        Check(result == 0xB100 && context.selected == 0, "Native menu command was intercepted.");

        result = WithTilingMenu(menu, 0, 0, 0, owner, [&]() -> BOOL {
            SendMessageW(owner, WM_COMMAND, context.commands[2], 0);
            return TRUE;
        });
        Check(result && context.selected == context.commands[2], "Notification-style command was lost.");
        DWORD_PTR data = 0;
        Check(!GetWindowSubclass(owner, MenuOwnerSubclass, reinterpret_cast<UINT_PTR>(&context), &data), "Temporary subclass leaked.");
        Check(GetMenuItemCount(menu) == 1, "Notification menu cleanup failed.");
        g_menuContext = nullptr;
        result = WithTilingMenu(menu, TPM_RETURNCMD, 0, 0, owner, []() -> BOOL { return 123; });
        Check(result == 123 && GetMenuItemCount(menu) == 1, "Unrelated menu was changed.");
        std::cout << "PASS native menu: augmentation, command IDs, selection, native commands, notification dispatch, cleanup\n";
        DestroyMenu(menu); DestroyWindow(owner);
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL " << error.what() << '\n';
        g_menuContext = nullptr;
        if (menu) DestroyMenu(menu);
        if (owner) DestroyWindow(owner);
        return 1;
    }
}
