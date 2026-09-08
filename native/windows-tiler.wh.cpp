// ==WindhawkMod==
// @id              windows-tiler
// @name            Windows Tiler
// @description     Tile a taskbar application's windows on one monitor or all monitors
// @version         0.1.3
// @author          Windows Tiler contributors
// @include         explorer.exe
// @architecture    x86-64
// @compilerOptions -lcomctl32
// ==/WindhawkMod==

// SPDX-License-Identifier: GPL-3.0-only
// The classic-menu routing is adapted from m417z's taskbar-classic-menu
// and taskbar-wheel-cycle mods, published under GPL v3:
// https://github.com/ramensoftware/windhawk-mods
// See THIRD-PARTY-NOTICES.md and native/COPYING for attribution and license.

// ==WindhawkModReadme==
/*
# Windows Tiler

Requires the WindowsTiler C# helper, published to
`%LOCALAPPDATA%\WindowsTiler\WindowsTiler.exe` (or configure its absolute path).

Right-click a running application's taskbar button. The existing Windows classic
group menu contains a **Windows Tiler** submenu: tile on this monitor, tile across
all monitors, and undo last tiling. Shift + right-click retains the normal Jump List.

Supports the stock Windows 11 x64 taskbar. Private Explorer symbols are required;
the mod refuses initialization if required symbols cannot be found. Do not combine
with another mod that swaps taskbar right-click and Shift + right-click.

This bridge is experimental until verified on your exact Windows build. It does
not install a replacement taskbar and never loads the .NET runtime into Explorer.
*/
// ==/WindhawkModReadme==

// ==WindhawkModSettings==
/*
- helperPath: '%LOCALAPPDATA%\WindowsTiler\WindowsTiler.exe'
  $name: Windows Tiler helper path
  $description: Absolute path to the published WindowsTiler.exe; environment variables are allowed.
*/
// ==/WindhawkModSettings==

#include <cstddef>
using std::nullptr_t; // Windhawk's headers also support toolchains exposing this name globally.
#include <windhawk_utils.h>
#include <commctrl.h>
#include <atomic>
#include <cstdint>
#include <cwctype>
#include <string>
#include <string_view>
#include <vector>

// Resolved shell interfaces; signatures are from Microsoft's public symbol files.
using HandleClickFn = HRESULT(WINAPI*)(void*, void*, void*, void*);
using OnContextMenuFn = void(WINAPI*)(void*, POINT, HWND, bool, void*, void*);
using ContextRequestedFn = void(WINAPI*)(void*, void*, void*);
using StaticContextRequestedFn = void(WINAPI*)(void*, void*);
using GetWindowFn = HWND(WINAPI*)(void*);
using GetGroupFn = void*(WINAPI*)(void*, void*, int*);
using GetCountFn = int(WINAPI*)(void*);
using GetItemFn = void*(WINAPI*)(void*, int);

HandleClickFn g_handleClick;
OnContextMenuFn g_onContextMenu;
// Different Windows 11 builds route a taskbar right-click through different Taskbar.View
// entry points (TaskbarResources, TaskListButton, or a static TaskListButtonHandlers helper).
// All three are hooked as optional; whichever one the current build actually calls wins.
ContextRequestedFn g_contextRequested;
ContextRequestedFn g_contextRequestedButton;
StaticContextRequestedFn g_contextRequestedHandlers;
GetWindowFn g_getListWindow, g_getWindow, g_getImmersiveWindow;
GetGroupFn g_getButtonGroup;
GetCountFn g_getGroupType, g_getCount;
GetItemFn g_getItem;
void* g_listBaseVtable;
void* g_listSiteVtable;
void* g_immersiveVtable;

// Original public APIs are retained so ordinary menus keep their normal behavior.
decltype(&GetKeyState) g_getKeyState;
decltype(&TrackPopupMenu) g_trackMenu;
decltype(&TrackPopupMenuEx) g_trackMenuEx;
decltype(&LoadLibraryExW) g_loadLibrary;
std::atomic<bool> g_viewHooked{false};
thread_local bool g_insideContextRequest = false;
// Not thread_local: Windows can dispatch CTaskListWnd::HandleClick on a worker thread
// instead of the UI thread that ran OnTaskListButtonContextRequested (the documented
// TaskbarShiftRightClickCrash mitigation), so HandleClickHook must see the request that
// was recorded on a different thread.
std::atomic<bool> g_requestClassic{false};
std::atomic<ULONGLONG> g_requestTime{0};

/// Owns copied HWNDs for one synchronous Windows context-menu invocation.
struct MenuContext {
    std::vector<HWND> windows;
    POINT point{};
    bool attached = false;
    bool menuShown = false;
    UINT commands[3]{};
    UINT selected = 0;
};
thread_local MenuContext* g_menuContext = nullptr;

/// Finds a known COM subobject within a bounded readable memory region.
void* FindSubobject(void* object, void* vtable, int direction) {
    if (!object || !vtable) return nullptr;
    auto address = reinterpret_cast<uintptr_t>(object);
    for (int i = 0; i < 64; i++) {
        auto candidate = address + static_cast<intptr_t>(i * direction) * sizeof(void*);
        MEMORY_BASIC_INFORMATION memory{};
        if (!VirtualQuery(reinterpret_cast<void*>(candidate), &memory, sizeof(memory)) ||
            memory.State != MEM_COMMIT || (memory.Protect & (PAGE_NOACCESS | PAGE_GUARD)) ||
            candidate + sizeof(void*) > reinterpret_cast<uintptr_t>(memory.BaseAddress) + memory.RegionSize) break;
        if (*reinterpret_cast<void**>(candidate) == vtable) return reinterpret_cast<void*>(candidate);
    }
    return nullptr;
}

/// Copies exact taskbar-group members without guessing by process name or executable path.
std::vector<HWND> GroupWindows(void* listSite, void* taskGroup) {
    std::vector<HWND> windows;
    auto base = FindSubobject(listSite, g_listBaseVtable, -1);
    if (!base || !taskGroup) return windows;
    auto group = g_getButtonGroup(base, taskGroup, nullptr);
    if (!group) return windows;
    int count = g_getCount(group);
    if (count < 1 || count > 128) return windows;
    for (int index = 0; index < count; index++) {
        auto item = g_getItem(group, index);
        if (!item) continue;
        HWND window = *reinterpret_cast<void**>(item) == g_immersiveVtable
            ? g_getImmersiveWindow(item) : g_getWindow(item);
        if (IsWindow(window) && std::find(windows.begin(), windows.end(), window) == windows.end()) windows.push_back(window);
    }
    return windows;
}

/// Launches the C# helper without a console, command shell, inherited handles, or a wait in Explorer.
void LaunchHelper(const MenuContext& context) {
    PCWSTR setting = Wh_GetStringSetting(L"helperPath");
    std::wstring configured = setting ? setting : L"";
    if (setting) Wh_FreeStringSetting(setting);
    DWORD required = ExpandEnvironmentStringsW(configured.c_str(), nullptr, 0);
    if (required == 0 || required > 32767) return;
    std::wstring path(required, L'\0');
    if (ExpandEnvironmentStringsW(configured.c_str(), path.data(), required) != required) return;
    path.resize(required - 1);
    bool absolute = (path.size() >= 3 && path[1] == L':' && path[2] == L'\\') || path.starts_with(L"\\\\");
    if (!absolute || path.find(L'"') != std::wstring::npos || GetFileAttributesW(path.c_str()) == INVALID_FILE_ATTRIBUTES) {
        MessageBoxW(nullptr, L"Publish WindowsTiler.exe and set its absolute path in the Windows Tiler mod settings.", L"Windows Tiler", MB_OK | MB_ICONWARNING);
        return;
    }
    std::wstring command = L"\"" + path + L"\"";
    if (context.selected == context.commands[2]) command += L" undo";
    else {
        command += context.selected == context.commands[0] ? L" tile --scope monitor" : L" tile --scope all";
        command += L" --point " + std::to_wstring(context.point.x) + L" " + std::to_wstring(context.point.y) + L" --windows";
        for (HWND window : context.windows)
            command += L" " + std::to_wstring(static_cast<uint32_t>(reinterpret_cast<uintptr_t>(window)));
    }
    STARTUPINFOW startup{sizeof(startup)};
    PROCESS_INFORMATION process{};
    if (CreateProcessW(path.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process)) {
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
    } else {
        MessageBoxW(nullptr, L"Windows Tiler could not start. Check the helper path and that .NET 8 Desktop Runtime is installed.", L"Windows Tiler", MB_OK | MB_ICONWARNING);
    }
}

/// Consumes only our commands when Windows uses notification-style menu dispatch.
LRESULT CALLBACK MenuOwnerSubclass(HWND window, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR, DWORD_PTR data) {
    auto context = reinterpret_cast<MenuContext*>(data);
    if ((message == WM_COMMAND && HIWORD(wParam) == 0) || message == WM_SYSCOMMAND) {
        UINT command = message == WM_SYSCOMMAND ? (static_cast<UINT>(wParam) & 0xFFF0) : LOWORD(wParam);
        for (UINT id : context->commands) {
            if (command == id) { context->selected = command; return 0; }
        }
    }
    return DefSubclassProc(window, message, wParam, lParam);
}

/// Adds a temporary submenu to the actual Windows menu and restores it before returning to Explorer.
template<typename ShowMenu>
BOOL WithTilingMenu(HMENU menu, UINT flags, int x, int y, HWND owner, ShowMenu show) {
    auto context = g_menuContext;
    if (context) {
        context->menuShown = true;
        Wh_Log(L"Windows Tiler: TrackPopupMenu menu=%p attached=%d windows=%zu", menu, context->attached, context->windows.size());
    }
    if (!context || context->attached || context->windows.empty()) return show();
    int originalCount = GetMenuItemCount(menu);
    if (originalCount < 0) return show();
    // Use aligned command IDs so WM_SYSCOMMAND's low-nibble masking remains safe.
    UINT candidate = 0xB100;
    for (auto& command : context->commands) {
        while (candidate < 0xE000 && GetMenuState(menu, candidate, MF_BYCOMMAND) != static_cast<UINT>(-1)) candidate += 0x10;
        if (candidate >= 0xE000) return show();
        command = candidate;
        candidate += 0x10;
    }
    HMENU submenu = CreatePopupMenu();
    if (!submenu) return show();
    bool inserted = AppendMenuW(submenu, MF_STRING, context->commands[0], L"Tile on this monitor") &&
        AppendMenuW(submenu, MF_STRING, context->commands[1], L"Tile across all monitors") &&
        AppendMenuW(submenu, MF_STRING, context->commands[2], L"Undo last tiling");
    bool subclassed = false;
    if (inserted && !(flags & TPM_RETURNCMD)) {
        subclassed = SetWindowSubclass(owner, MenuOwnerSubclass, reinterpret_cast<UINT_PTR>(context), reinterpret_cast<DWORD_PTR>(context));
        inserted = subclassed;
    }
    bool separatorAdded = inserted && AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    bool submenuAdded = separatorAdded && AppendMenuW(menu, MF_POPUP, reinterpret_cast<UINT_PTR>(submenu), L"Windows Tiler");
    if (submenuAdded) {
        context->attached = true;
        // Popup coordinates also cover keyboard-opened menus whose original point was (-1,-1).
        context->point = {x, y};
    }
    BOOL result = show();
    if (submenuAdded && (flags & TPM_RETURNCMD)) {
        for (UINT command : context->commands)
            if (static_cast<UINT>(result) == command) { context->selected = command; result = 0; }
    }
    if (subclassed) RemoveWindowSubclass(owner, MenuOwnerSubclass, reinterpret_cast<UINT_PTR>(context));
    if (submenuAdded) RemoveMenu(menu, originalCount + 1, MF_BYPOSITION);
    if (separatorAdded) RemoveMenu(menu, originalCount, MF_BYPOSITION);
    DestroyMenu(submenu);
    context->attached = false;
    return result;
}

/// Extends legacy TrackPopupMenu calls only while an identified application group is opening its menu.
BOOL WINAPI TrackMenuHook(HMENU menu, UINT flags, int x, int y, int reserved, HWND owner, const RECT* rect) {
    return WithTilingMenu(menu, flags, x, y, owner, [&] { return g_trackMenu(menu, flags, x, y, reserved, owner, rect); });
}

/// Extends TrackPopupMenuEx calls using the same scoped menu augmentation.
BOOL WINAPI TrackMenuExHook(HMENU menu, UINT flags, int x, int y, HWND owner, LPTPMPARAMS parameters) {
    return WithTilingMenu(menu, flags, x, y, owner, [&] { return g_trackMenuEx(menu, flags, x, y, owner, parameters); });
}

/// Scopes copied group handles to the synchronous native context-menu call, including nested calls.
/// Returns whether Explorer actually displayed a menu, so callers can fall back to a different item.
bool InvokeContextMenu(void* self, POINT point, HWND window, bool value, void* group, void* item) {
    MenuContext context{GroupWindows(self, group), point};
    Wh_Log(L"Windows Tiler: OnContextMenu self=%p group=%p item=%p windows=%zu", self, group, item, context.windows.size());
    auto previous = g_menuContext;
    g_menuContext = &context;
    g_onContextMenu(self, point, window, value, group, item);
    g_menuContext = previous;
    Wh_Log(L"Windows Tiler: OnContextMenu returned selected=%u menuShown=%d", context.selected, context.menuShown);
    if (context.selected) LaunchHelper(context);
    return context.menuShown;
}

/// Symbol hook for CTaskListWnd::OnContextMenu; kept void to match the real signature exactly.
void WINAPI OnContextMenuHook(void* self, POINT point, HWND window, bool value, void* group, void* item) {
    InvokeContextMenu(self, point, window, value, group, item);
}

/// Redirects a classic-menu request through Windows' existing group-menu implementation.
HRESULT WINAPI HandleClickHook(void* self, void* group, void* item, void* options) {
    bool wantsClassic = g_requestClassic;
    ULONGLONG elapsed = GetTickCount64() - g_requestTime;
    Wh_Log(L"Windows Tiler: HandleClick wantsClassic=%d elapsed=%llu self=%p group=%p item=%p", wantsClassic, elapsed, self, group, item);
    if (wantsClassic && elapsed <= 200) {
        g_requestClassic = false;
        auto base = FindSubobject(self, g_listBaseVtable, -1);
        auto site = FindSubobject(base, g_listSiteVtable, 1);
        Wh_Log(L"Windows Tiler: HandleClick base=%p site=%p", base, site);
        if (site) {
            POINT point{};
            GetCursorPos(&point);
            // Substituting a representative item for a single-item group (groupType 1) suppresses
            // the menu entirely on some builds, and calling OnContextMenu a second time for the
            // same click doesn't recover it either - the first call leaves Explorer's internal
            // state unable to show a menu on a second attempt regardless of the item passed. Always
            // pass item as Explorer gave it: the only configuration proven to reliably show a menu.
            auto buttonGroup = group ? g_getButtonGroup(base, group, nullptr) : nullptr;
            int groupType = buttonGroup ? g_getGroupType(buttonGroup) : -1;
            Wh_Log(L"Windows Tiler: HandleClick buttonGroup=%p groupType=%d item=%p", buttonGroup, groupType, item);
            InvokeContextMenu(site, point, g_getListWindow(site), false, group, item);
            return S_OK;
        }
    }
    return g_handleClick(self, group, item, options);
}

/// Records the UI-thread request; the bounded timestamp accommodates newer asynchronous taskbar dispatch.
void RecordContextRequest(const wchar_t* route) {
    SHORT rawShift = g_getKeyState(VK_SHIFT);
    g_requestClassic = !(rawShift & 0x8000);
    g_requestTime = GetTickCount64();
    Wh_Log(L"Windows Tiler: ContextRequested route=%s rawShift=%d requestClassic=%d", route, rawShift & 0x8000 ? 1 : 0, (bool)g_requestClassic);
}

/// TaskbarResources' instance-method route — historically the primary entry point.
void WINAPI ContextRequestedHook(void* self, void* sender, void* args) {
    RecordContextRequest(L"resources");
    g_insideContextRequest = true;
    g_contextRequested(self, sender, args);
    g_insideContextRequest = false;
}

/// TaskListButton's own instance-method route, used instead of TaskbarResources on some builds.
void WINAPI ContextRequestedButtonHook(void* self, void* sender, void* args) {
    RecordContextRequest(L"button");
    g_insideContextRequest = true;
    g_contextRequestedButton(self, sender, args);
    g_insideContextRequest = false;
}

/// TaskListButtonHandlers' static-method route, used instead of the above on some builds.
void WINAPI ContextRequestedHandlersHook(void* sender, void* args) {
    RecordContextRequest(L"handlers");
    g_insideContextRequest = true;
    g_contextRequestedHandlers(sender, args);
    g_insideContextRequest = false;
}

/// Swaps the Shift interpretation only inside the taskbar context request, preserving all other input.
SHORT WINAPI GetKeyStateHook(int key) {
    SHORT value = g_getKeyState(key);
    return key == VK_SHIFT && g_insideContextRequest ? value ^ 0x8000 : value;
}

/// Resolves whichever context-request entry point the current taskbar build actually uses.
bool HookView(HMODULE module) {
    WindhawkUtils::SYMBOL_HOOK hooks[] = {
        {{LR"(public: void __cdecl winrt::Taskbar::implementation::TaskbarResources::OnTaskListButtonContextRequested(struct winrt::Windows::UI::Xaml::UIElement const &,struct winrt::Windows::UI::Xaml::Input::ContextRequestedEventArgs const &))"}, &g_contextRequested, ContextRequestedHook, true},
        {{LR"(private: void __cdecl winrt::Taskbar::implementation::TaskListButton::OnContextRequested(struct winrt::Windows::UI::Xaml::UIElement const &,struct winrt::Windows::UI::Xaml::Input::ContextRequestedEventArgs const &))"}, &g_contextRequestedButton, ContextRequestedButtonHook, true},
        {{LR"(public: static void __cdecl winrt::Taskbar::implementation::TaskListButtonHandlers::HandleContextRequested(struct winrt::Windows::UI::Xaml::UIElement const &,struct winrt::Windows::UI::Xaml::Input::ContextRequestedEventArgs const &))"}, &g_contextRequestedHandlers, ContextRequestedHandlersHook, true},
    };
    if (!WindhawkUtils::HookSymbols(module, hooks, ARRAYSIZE(hooks))) return false;
    Wh_Log(L"Windows Tiler: context-request routes resources=%d button=%d handlers=%d",
        g_contextRequested ? 1 : 0, g_contextRequestedButton ? 1 : 0, g_contextRequestedHandlers ? 1 : 0);
    return g_contextRequested || g_contextRequestedButton || g_contextRequestedHandlers;
}

/// Finds the taskbar view module used by the current Windows 11 build.
HMODULE ViewModule() {
    HMODULE module = GetModuleHandleW(L"Taskbar.View.dll");
    return module ? module : GetModuleHandleW(L"ExplorerExtensions.dll");
}

/// Case-insensitive substring search, used only to spot a taskbar module by an unexpected name.
bool ContainsCaseInsensitive(std::wstring_view text, std::wstring_view needle) {
    if (needle.empty() || text.size() < needle.size()) return false;
    for (size_t i = 0; i + needle.size() <= text.size(); i++) {
        bool match = true;
        for (size_t j = 0; j < needle.size(); j++) {
            if (towlower(text[i + j]) != towlower(needle[j])) { match = false; break; }
        }
        if (match) return true;
    }
    return false;
}

/// Completes view-hook installation when Explorer loads its taskbar after this mod initializes.
HMODULE WINAPI LoadLibraryHook(LPCWSTR path, HANDLE file, DWORD flags) {
    HMODULE module = g_loadLibrary(path, file, flags);
    if (module && path && ContainsCaseInsensitive(path, L"taskbar")) {
        Wh_Log(L"Windows Tiler: loaded module %s", path);
    }
    if (module && module == ViewModule() && !g_viewHooked.exchange(true)) {
        if (HookView(module)) {
            Wh_ApplyHookOperations();
            Wh_Log(L"Windows Tiler: taskbar view hook installed via LoadLibraryHook.");
        } else Wh_Log(L"Windows Tiler: taskbar view symbols unavailable; reload the mod after updating.");
    }
    return module;
}

/// Installs hooks only when all required native taskbar symbols resolve on the current build.
BOOL Wh_ModInit() {
    HMODULE taskbar = LoadLibraryExW(L"taskbar.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!taskbar) {
        Wh_Log(L"Windows Tiler: taskbar.dll failed to load.");
        return FALSE;
    }
    WindhawkUtils::SYMBOL_HOOK hooks[] = {
        {{LR"(public: virtual long __cdecl CTaskListWnd::HandleClick(struct ITaskGroup *,struct ITaskItem *,struct winrt::Windows::System::LauncherOptions const &))"}, &g_handleClick, HandleClickHook},
        {{LR"(public: virtual void __cdecl CTaskListWnd::OnContextMenu(struct tagPOINT,struct HWND__ *,bool,struct ITaskGroup *,struct ITaskItem *))"}, &g_onContextMenu, OnContextMenuHook},
        {{LR"(public: virtual struct HWND__ * __cdecl CTaskListWnd::GetWindow(void))"}, &g_getListWindow},
        {{LR"(protected: struct ITaskBtnGroup * __cdecl CTaskListWnd::_GetTBGroupFromGroup(struct ITaskGroup *,int *))"}, &g_getButtonGroup},
        {{LR"(public: virtual enum eTBGROUPTYPE __cdecl CTaskBtnGroup::GetGroupType(void))"}, &g_getGroupType},
        {{LR"(public: virtual int __cdecl CTaskBtnGroup::GetNumItems(void))"}, &g_getCount},
        {{LR"(public: virtual struct ITaskItem * __cdecl CTaskBtnGroup::GetTaskItem(int))"}, &g_getItem},
        {{LR"(public: virtual struct HWND__ * __cdecl CWindowTaskItem::GetWindow(void))"}, &g_getWindow},
        {{LR"(public: virtual struct HWND__ * __cdecl CImmersiveTaskItem::GetWindow(void))"}, &g_getImmersiveWindow},
        {{LR"(const CImmersiveTaskItem::`vftable'{for `ITaskItem'})"}, &g_immersiveVtable},
        {{LR"(const CTaskListWnd::`vftable'{for `CImpWndProc'})"}, &g_listBaseVtable},
        {{LR"(const CTaskListWnd::`vftable'{for `ITaskListSite'})"}, &g_listSiteVtable},
    };
    bool resolved = WindhawkUtils::HookSymbols(taskbar, hooks, ARRAYSIZE(hooks));
    // Retain the module reference: queued detours must never point into an unloaded taskbar DLL.
    if (!resolved) {
        Wh_Log(L"Windows Tiler: taskbar.dll symbols unavailable on this Windows build.");
        return FALSE;
    }
    if (HMODULE view = ViewModule()) {
        if (!HookView(view)) {
            Wh_Log(L"Windows Tiler: taskbar view symbols unavailable on this Windows build.");
            return FALSE;
        }
        g_viewHooked = true;
        Wh_Log(L"Windows Tiler: taskbar view hook installed synchronously in Wh_ModInit.");
    } else {
        Wh_Log(L"Windows Tiler: taskbar view module not yet loaded; deferring hook installation.");
    }
    if (!(WindhawkUtils::SetFunctionHook(GetKeyState, GetKeyStateHook, &g_getKeyState) &&
        WindhawkUtils::SetFunctionHook(TrackPopupMenu, TrackMenuHook, &g_trackMenu) &&
        WindhawkUtils::SetFunctionHook(TrackPopupMenuEx, TrackMenuExHook, &g_trackMenuEx) &&
        WindhawkUtils::SetFunctionHook(LoadLibraryExW, LoadLibraryHook, &g_loadLibrary))) {
        Wh_Log(L"Windows Tiler: failed to hook a public Win32 API.");
        return FALSE;
    }
    Wh_Log(L"Windows Tiler: initialized.");
    return TRUE;
}

/// Closes the race between the initial module lookup and registering the library-load hook.
void Wh_ModAfterInit() {
    if (HMODULE view = ViewModule(); view && !g_viewHooked.exchange(true)) {
        if (HookView(view)) Wh_ApplyHookOperations();
        else Wh_Log(L"Windows Tiler: unable to install the taskbar view hook.");
    }
}

/// Windhawk removes registered hooks and waits for active hook invocations during unload.
void Wh_ModUninit() { Wh_Log(L"Windows Tiler unloaded"); }
