using System.Diagnostics;
using WindowsTiler.Core;

namespace WindowsTiler;

/// <summary>Launch-on-demand entry point for taskbar commands.</summary>
internal static class Program
{
    /// <summary>Executes one command and exits without a persistent tray process.</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        try
        {
            var command = Command.Parse(args);
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || !Environment.Is64BitProcess)
                throw new InvalidOperationException("Windows Tiler requires Windows 11 x64.");
            if (command.Action is "status" or "--help")
            {
                MessageBox.Show("Windows Tiler\n\nRight-click a taskbar application group to tile on this monitor or across all monitors.\n\nThe native Windows Tiler bridge must be enabled in Windhawk. Shift + right-click opens the normal Jump List.\n\nCommands: tile --scope monitor|all --point X Y --windows HWND...; undo; status.",
                    "Windows Tiler", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            // One operation per interactive session; a crashed helper leaves its snapshot recoverable.
            using var mutex = new Mutex(false, "Local\\WindowsTiler.Operation");
            var ownsMutex = false;
            try
            {
                try { ownsMutex = mutex.WaitOne(0); } catch (AbandonedMutexException) { ownsMutex = true; }
                if (!ownsMutex) throw new InvalidOperationException("A tiling operation is already running. Try again when it finishes.");
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsTiler");
                var session = Process.GetCurrentProcess().SessionId;
                var store = new UndoStore(Path.Combine(directory, $"undo-{session}.json"));
                var areas = DesktopWindows.WorkAreas(command);
                using var desktop = new DesktopWindows(DesktopWindows.MaximumDpi(areas));
                var operation = new TilingOperation(desktop, store);
                var result = command.Action == "undo" ? operation.Undo() : operation.Tile(command.Windows!, areas);
                if (result.Skipped > 0)
                    MessageBox.Show($"{result.Applied} window(s) updated. {result.Skipped} window(s) were unavailable, fixed-size, or on another desktop.",
                        "Windows Tiler", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            finally { if (ownsMutex) mutex.ReleaseMutex(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Windows Tiler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }
}
