using WindowsTiler;
using WindowsTiler.Core;
using Rect = WindowsTiler.Core.Rect;

/// <summary>Exercises real Win32 operations exclusively against windows created by this process.</summary>
internal static class Program
{
    /// <summary>Runs an isolated UI thread and guarantees that all owned test windows close afterwards.</summary>
    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var ready = new ManualResetEventSlim();
        Form? first = null, second = null, fixedWindow = null;
        Exception? uiError = null;
        var ui = new Thread(() =>
        {
            try
            {
                first = CreateWindow("Windows Tiler test A", 100);
                second = CreateWindow("Windows Tiler test B", 440);
                fixedWindow = CreateWindow("Windows Tiler fixed-size test", 780);
                fixedWindow.FormBorderStyle = FormBorderStyle.FixedDialog;
                first.Show(); second.Show(); fixedWindow.Show();
                ready.Set();
                Application.Run();
                first.Dispose(); second.Dispose(); fixedWindow.Dispose();
            }
            catch (Exception ex) { uiError = ex; ready.Set(); }
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.IsBackground = true;
        ui.Start();
        var folder = Path.Combine(Path.GetTempPath(), "WindowsTiler.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            if (!ready.Wait(TimeSpan.FromSeconds(10))) throw new Exception("Test UI did not start.");
            if (uiError is not null) throw uiError;
            using var desktop = new DesktopWindows();
            var a = first!.Handle.ToInt64();
            var b = second!.Handle.ToInt64();
            var originalA = desktop.Capture(a) ?? throw new Exception("Resizable test window was excluded.");
            var originalB = desktop.Capture(b) ?? throw new Exception("Second test window was excluded.");
            Check(desktop.Capture(fixedWindow!.Handle.ToInt64()) is null, "Fixed-size window was not excluded.");
            var store = new UndoStore(Path.Combine(folder, "undo.json"));
            var operation = new TilingOperation(desktop, store);
            var work = Screen.PrimaryScreen!.WorkingArea;
            var result = operation.Tile([a, b], [new(work.X, work.Y, work.Width, work.Height)]);
            Check(result.Applied == 2, "Both test windows must tile.");
            Check(store.Read().Count == 2, "Disk undo snapshot missing.");
            Check(desktop.Capture(a)!.Placement != originalA.Placement, "First window did not move.");
            operation.Undo();
            Check(desktop.Capture(a)!.Placement == originalA.Placement, "First placement did not restore exactly.");
            Check(desktop.Capture(b)!.Placement == originalB.Placement, "Second placement did not restore exactly.");

            first.Invoke(() => { first.WindowState = FormWindowState.Maximized; second.WindowState = FormWindowState.Minimized; });
            var maximized = desktop.Capture(a) ?? throw new Exception("Maximized window was excluded.");
            var minimized = desktop.Capture(b) ?? throw new Exception("Minimized window was excluded.");
            Check(maximized.Placement.ShowCommand == 3 && minimized.Placement.ShowCommand == 2, "Test state setup failed.");
            operation.Tile([a, b], [new(work.X, work.Y, work.Width, work.Height)]);
            operation.Undo();
            Check(desktop.Capture(a)!.Placement.ShowCommand == 3, "Maximized state not restored.");
            Check(desktop.Capture(b)!.Placement.ShowCommand == 2, "Minimized state not restored.");
            Console.WriteLine("PASS real windows: capture, fixed-size exclusion, tile, disk undo, minimized/maximized restoration");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex); return 1; }
        finally
        {
            if (first is { IsDisposed: false, IsHandleCreated: true })
                first.BeginInvoke(() => Application.ExitThread());
            ui.Join(TimeSpan.FromSeconds(5));
            if (Directory.Exists(folder)) Directory.Delete(folder, true); // Only this run's GUID-named test directory.
        }
    }

    /// <summary>Creates a visible resizable fixture with hand-specified dimensions.</summary>
    private static Form CreateWindow(string title, int left) => new()
    {
        Text = title, StartPosition = FormStartPosition.Manual,
        Bounds = new System.Drawing.Rectangle(left, 160, 320, 240), MinimumSize = new(200, 150)
    };

    /// <summary>Raises a useful failure when an externally observable contract is broken.</summary>
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
