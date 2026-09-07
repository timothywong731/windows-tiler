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
            if (command.Action is "status" or "setup" or "--help")
            {
                ShowSetupStatus(command.Action == "setup");
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

    /// <summary>Reports bridge registration and gives the user the exact one-time setup steps.</summary>
    private static void ShowSetupStatus(bool openWindhawk)
    {
        var windhawk = FindWindhawk();
        var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Windhawk", "userprofile.json");
        var enabled = File.Exists(profile) && File.ReadAllText(profile).Contains("local@windows-tiler", StringComparison.OrdinalIgnoreCase);
        var state = enabled ? "Windhawk reports the Windows Tiler mod is registered." : "Windhawk does not report the Windows Tiler mod as enabled.";
        var text = $"Windows Tiler\n\n{state}\n\n1. Open Windhawk and choose Create a new mod.\n2. Paste windows-tiler.wh.cpp from this installation folder into the editor.\n3. Compile and enable the mod, then wait for Explorer to reload.\n4. Right-click a grouped taskbar app to see Windows Tiler.\n\nShift + right-click keeps the normal Jump List. DbgViewMini's Listening line only means the viewer is running.\n\nWindhawk: {(windhawk is null ? "not found" : windhawk)}";
        MessageBox.Show(text, "Windows Tiler setup", MessageBoxButtons.OK, enabled ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (openWindhawk && windhawk is not null) Process.Start(new ProcessStartInfo(windhawk) { UseShellExecute = true });
    }

    /// <summary>Finds the installed Windhawk UI in either native program-files location.</summary>
    private static string? FindWindhawk()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windhawk", "windhawk.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windhawk", "windhawk.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
