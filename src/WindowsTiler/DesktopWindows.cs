using WindowsTiler.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Rect = WindowsTiler.Core.Rect;

namespace WindowsTiler;

/// <summary>Adapts Win32 desktop windows to the recoverable tiling engine.</summary>
public sealed class DesktopWindows : IWindowAccess, IDisposable
{
    // Keep the COM object alive for a single STA helper invocation.
    private readonly IVirtualDesktopManager desktopManager;
    private readonly uint targetDpi;

    /// <summary>Creates a desktop adapter; target DPI conservatively scales minimum sizes when moving displays.</summary>
    public DesktopWindows(uint targetDpi = 0)
    {
        this.targetDpi = targetDpi;
        var type = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), true)!;
        desktopManager = (IVirtualDesktopManager)Activator.CreateInstance(type)!;
    }

    /// <summary>Captures an eligible window's identity, placement and minimum dimensions.</summary>
    public WindowSnapshot? Capture(long handle)
    {
        if (handle <= 0 || handle > uint.MaxValue) return null;
        var hwnd = (nint)handle;
        if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd) ||
            NativeMethods.IsHungAppWindow(hwnd) || NativeMethods.GetWindow(hwnd, 4) != 0 ||
            (NativeMethods.GetWindowLongPtr(hwnd, -16).ToInt64() & 0x40000) == 0 ||
            (NativeMethods.GetWindowLongPtr(hwnd, -16).ToInt64() & 0x40000000) != 0 ||
            (NativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64() & 0x80) != 0 || !OnCurrentDesktop(hwnd)) return null;
        if (NativeMethods.DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) < 0 || cloaked != 0) return null;
        var identity = Identity(hwnd);
        if (identity is null) return null;
        var placement = ReadPlacement(hwnd);
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        if (dpi == 0) return null;
        NativeMethods.MinMaxInfo limits = new()
        {
            MinTrackSize = new() { X = NativeMethods.GetSystemMetricsForDpi(34, dpi), Y = NativeMethods.GetSystemMetricsForDpi(35, dpi) }
        };
        // SMTO_ABORTIFHUNG | SMTO_BLOCK bounds each foreign-app query to 200ms.
        if (NativeMethods.GetMinMaxInfo(hwnd, 0x24, 0, ref limits, 3, 200, out _) == 0) return null;
        var scale = targetDpi == 0 ? 1 : Math.Max(1, (double)targetDpi / dpi);
        var minimum = new WindowSize(Math.Max(1, (int)Math.Ceiling(limits.MinTrackSize.X * scale)),
            Math.Max(1, (int)Math.Ceiling(limits.MinTrackSize.Y * scale)));
        return new(handle, identity.Value.Pid, identity.Value.Started, ToPlacement(placement), minimum);
    }
    /// <summary>Checks the captured identity before each desktop mutation.</summary>
    public bool IsCurrent(WindowSnapshot window)
    {
        var hwnd = (nint)window.Handle;
        var identity = Identity(hwnd);
        return identity is not null && identity.Value.Pid == window.ProcessId &&
            identity.Value.Started == window.ProcessStarted && OnCurrentDesktop(hwnd);
    }
    /// <summary>Moves a window and verifies the application accepted its new bounds.</summary>
    public void Place(WindowSnapshot window, Rect bounds)
    {
        EnsureCurrent(window);
        var hwnd = (nint)window.Handle;
        if (NativeMethods.IsIconic(hwnd) || NativeMethods.IsZoomed(hwnd))
        {
            var normal = ReadPlacement(hwnd);
            normal.Flags = 4; // WPF_ASYNCWINDOWPLACEMENT; clear restore-to-maximized for tiling.
            normal.ShowCommand = 4; // SW_SHOWNOACTIVATE restores without stealing focus.
            if (!NativeMethods.SetWindowPlacement(hwnd, in normal)) throw new Win32Exception();
            WaitFor(() => !NativeMethods.IsIconic(hwnd) && !NativeMethods.IsZoomed(hwnd), "Window did not restore.");
        }
        // SWP_ASYNCWINDOWPOS | SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER.
        if (!NativeMethods.SetWindowPos(hwnd, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x4214))
            throw new Win32Exception();
        WaitFor(() => NativeMethods.GetWindowRect(hwnd, out var actual) && ToRect(actual) == bounds,
            "The application did not accept its tile size or position.");
    }
    /// <summary>Restores the original placement and show state.</summary>
    public void Restore(WindowSnapshot window)
    {
        EnsureCurrent(window);
        var original = FromPlacement(window.Placement);
        original.Flags |= 4; // Request cross-thread restoration without waiting inside an unresponsive app.
        if (!NativeMethods.SetWindowPlacement((nint)window.Handle, in original)) throw new Win32Exception();
        WaitFor(() =>
        {
            var actual = ReadPlacement((nint)window.Handle);
            return actual.ShowCommand == window.Placement.ShowCommand && ToRect(actual.NormalPosition) == window.Placement.NormalBounds;
        }, "The application did not restore its original placement.");
    }
    /// <summary>Releases the virtual desktop manager when the operation finishes.</summary>
    public void Dispose() => Marshal.FinalReleaseComObject(desktopManager);

    /// <summary>Enumerates current monitor work areas in stable geometric order, or selects the clicked monitor.</summary>
    public static IReadOnlyList<Rect> WorkAreas(Command command)
    {
        var screens = command.AllMonitors ? Screen.AllScreens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToArray()
            : [Screen.FromPoint(new(command.X, command.Y))];
        return screens.Select(s => new Rect(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height)).ToArray();
    }

    /// <summary>Finds the highest target DPI to avoid proposing tiles smaller than scaled tracking constraints.</summary>
    public static uint MaximumDpi(IReadOnlyList<Rect> areas)
    {
        uint maximum = 96;
        foreach (var area in areas)
        {
            var monitor = NativeMethods.MonitorFromPoint(new() { X = area.X, Y = area.Y }, 2);
            if (NativeMethods.GetDpiForMonitor(monitor, 0, out var dpi, out _) >= 0) maximum = Math.Max(maximum, dpi);
        }
        return maximum;
    }

    /// <summary>Fails closed when Windows cannot establish a window's virtual desktop membership.</summary>
    private bool OnCurrentDesktop(nint hwnd) => desktopManager.IsWindowOnCurrentVirtualDesktop(hwnd, out var current) >= 0 && current;

    /// <summary>Captures PID plus process creation time to guard against recycled process identifiers.</summary>
    private static (int Pid, long Started)? Identity(nint hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd) || NativeMethods.GetWindowThreadProcessId(hwnd, out var pid) == 0) return null;
        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            return (process.Id, process.StartTime.ToUniversalTime().Ticks);
        }
        catch (Exception ex) when (ex is ArgumentException or Win32Exception or InvalidOperationException) { return null; }
    }

    /// <summary>Rejects stale handles and already-hung applications immediately before a native write.</summary>
    private void EnsureCurrent(WindowSnapshot window)
    {
        if (!IsCurrent(window) || NativeMethods.IsHungAppWindow((nint)window.Handle))
            throw new InvalidOperationException("The window is no longer available or is unresponsive.");
    }

    /// <summary>Initializes the required structure size before capturing a native placement.</summary>
    private static NativeMethods.WindowPlacement ReadPlacement(nint hwnd)
    {
        NativeMethods.WindowPlacement placement = new() { Length = (uint)Marshal.SizeOf<NativeMethods.WindowPlacement>() };
        if (!NativeMethods.GetWindowPlacement(hwnd, ref placement)) throw new Win32Exception();
        return placement;
    }

    /// <summary>Converts edge coordinates without changing their screen/workspace coordinate system.</summary>
    private static Rect ToRect(NativeMethods.Rect rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    /// <summary>Copies native placement data into the serializable core model.</summary>
    private static Placement ToPlacement(NativeMethods.WindowPlacement value) => new(value.Flags, value.ShowCommand,
        new(value.MinPosition.X, value.MinPosition.Y), new(value.MaxPosition.X, value.MaxPosition.Y), ToRect(value.NormalPosition));

    /// <summary>Reconstructs the original Win32 placement without confusing workspace and screen coordinates.</summary>
    private static NativeMethods.WindowPlacement FromPlacement(Placement value) => new()
    {
        Length = (uint)Marshal.SizeOf<NativeMethods.WindowPlacement>(), Flags = value.Flags, ShowCommand = value.ShowCommand,
        MinPosition = new() { X = value.MinPosition.X, Y = value.MinPosition.Y },
        MaxPosition = new() { X = value.MaxPosition.X, Y = value.MaxPosition.Y },
        NormalPosition = new() { Left = value.NormalBounds.X, Top = value.NormalBounds.Y, Right = value.NormalBounds.Right, Bottom = value.NormalBounds.Bottom }
    };

    /// <summary>Allows asynchronous window operations one second to complete, then triggers transaction recovery.</summary>
    private static void WaitFor(Func<bool> condition, string failure)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            if (condition()) return;
            Thread.Sleep(15);
        } while (timer.ElapsedMilliseconds < 1000);
        throw new InvalidOperationException(failure);
    }
}
