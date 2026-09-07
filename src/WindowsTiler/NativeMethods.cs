using System.Runtime.InteropServices;

namespace WindowsTiler;

/// <summary>Contains the Win32 ABI definitions used by the desktop adapter.</summary>
internal static class NativeMethods
{
    /// <summary>Native POINT layout, shared by placement and tracking-size structures.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }

    /// <summary>Native RECT stores exclusive edges rather than width and height.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }

    /// <summary>Native WINDOWPLACEMENT uses workspace coordinates for ordinary application windows.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPlacement
    {
        public uint Length, Flags, ShowCommand;
        public Point MinPosition, MaxPosition;
        public Rect NormalPosition;
    }

    /// <summary>WM_GETMINMAXINFO data; applications can override the default tracking limits.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MinMaxInfo { public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

    /// <summary>Checks whether an HWND currently exists.</summary>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint window);
    /// <summary>Tests the visibility style, including windows currently minimized.</summary>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);
    /// <summary>Detects minimized windows before requesting normal placement.</summary>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint window);
    /// <summary>Detects maximized windows before requesting normal placement.</summary>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(nint window);
    /// <summary>Rejects applications already identified by Windows as unresponsive.</summary>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsHungAppWindow(nint window);
    /// <summary>Obtains owner-window relationships for dialog exclusion.</summary>
    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint window, uint command);
    /// <summary>Reads style bits using the 64-bit-safe entry point.</summary>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint window, int index);
    /// <summary>Retrieves the process owning a window for identity checks.</summary>
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    /// <summary>Captures the normal rectangle and show state without converting coordinate systems.</summary>
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(nint window, ref WindowPlacement placement);
    /// <summary>Requests restoration using the same workspace-coordinate structure captured earlier.</summary>
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPlacement(nint window, in WindowPlacement placement);
    /// <summary>Reads the outer rectangle in physical pixels when the caller is per-monitor aware.</summary>
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out Rect rect);
    /// <summary>Queues a non-activating, non-z-order-changing move on the target UI thread.</summary>
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    /// <summary>Queries minimum tracking size with a bounded wait; Windows marshals this system message.</summary>
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    internal static extern nint GetMinMaxInfo(nint window, uint message, nint wParam, ref MinMaxInfo info, uint flags, uint timeout, out nuint result);
    /// <summary>Reads DWM cloaking so invisible virtual-desktop and shell windows are excluded.</summary>
    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, uint size);
    /// <summary>Returns the window's effective DPI for tracking-size scaling.</summary>
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint window);
    /// <summary>Reads minimum tracking metrics at the window's current DPI.</summary>
    [DllImport("user32.dll")]
    internal static extern int GetSystemMetricsForDpi(int index, uint dpi);
}

/// <summary>The documented COM interface for checking membership of the current virtual desktop.</summary>
[ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManager
{
    /// <summary>Checks whether a top-level window belongs to the active desktop.</summary>
    [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);
    /// <summary>Maintains the documented vtable slot for retrieving a desktop identifier.</summary>
    [PreserveSig] int GetWindowDesktopId(nint window, out Guid desktopId);
    /// <summary>Maintains the documented vtable slot for moving a window; Windows Tiler never calls it.</summary>
    [PreserveSig] int MoveWindowToDesktop(nint window, in Guid desktopId);
}
