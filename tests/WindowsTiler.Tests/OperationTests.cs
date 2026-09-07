using WindowsTiler.Core;

/// <summary>Checks recovery while substituting only the external desktop and disk boundaries.</summary>
internal static class OperationTests
{
    /// <summary>Exercises save-before-move, rollback, exclusion, partial undo, retry and closed windows.</summary>
    public static void Run()
    {
        // Missing save-before-move must leave the windows untouched when storage fails.
        var desktop = new Desktop();
        var store = new Store { FailWrite = true };
        var operation = new TilingOperation(desktop, store);
        ExpectFailure(() => operation.Tile([1, 2], [new(0, 0, 1000, 800)]));
        Check(desktop.Bounds[1] == Desktop.Original && desktop.Bounds[2] == Desktop.Original, "Moved before saving undo.");

        // A failure on the second window must roll back the first too.
        store.FailWrite = false;
        desktop.FailMove = 2;
        ExpectFailure(() => operation.Tile([1, 2], [new(0, 0, 1000, 800)]));
        Check(desktop.Bounds.Values.All(b => b == Desktop.Original), "Partial tiling not rolled back.");

        desktop.FailMove = 0;
        var result = operation.Tile([1, 2, 3], [new(0, 0, 1000, 800)]);
        Check(result.Applied == 2 && result.Skipped == 1, "Ineligible window handling is wrong.");
        Check(desktop.Bounds.Values.All(b => b != Desktop.Original), "Windows were not tiled.");
        Check(store.Saved.Count == 2, "Undo missing.");

        // Failed restore must retain only the windows still needing undo.
        desktop.FailRestore = 2;
        ExpectFailure(() => operation.Undo());
        Check(desktop.Bounds[1] == Desktop.Original && desktop.Bounds[2] != Desktop.Original, "Partial undo incorrect.");
        Check(store.Saved.Count == 1 && store.Saved[0].Handle == 2, "Failed restore cannot be retried.");
        desktop.FailRestore = 0;
        Check(operation.Undo().Applied == 1 && store.Saved.Count == 0, "Undo retry failed.");

        operation.Tile([1, 2], [new(0, 0, 1000, 800)]);
        desktop.Bounds.Remove(2);
        result = operation.Undo();
        Check(result.Applied == 1 && result.Skipped == 1 && store.Saved.Count == 0, "Closed window blocks undo.");
    }

    /// <summary>Reports the observable operation contract that failed.</summary>
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    /// <summary>Requires a recoverable failure instead of silent success.</summary>
    private static void ExpectFailure(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Expected operation to fail.");
    }
    /// <summary>Simulates a disk that can refuse writes before any window mutation.</summary>
    private sealed class Store : IUndoStore
    {
        public bool FailWrite;
        public IReadOnlyList<WindowSnapshot> Saved = [];
        /// <summary>Returns the current simulated on-disk snapshot.</summary>
        public IReadOnlyList<WindowSnapshot> Read() => Saved;
        /// <summary>Replaces the snapshot or reproduces an unavailable disk.</summary>
        public void Write(IReadOnlyList<WindowSnapshot> windows)
        {
            if (FailWrite) throw new InvalidOperationException("Disk unavailable.");
            Saved = windows.ToArray();
        }
    }
    /// <summary>Models independently failing external applications with observable rectangles.</summary>
    private sealed class Desktop : IWindowAccess
    {
        public static readonly Rect Original = new(20, 30, 400, 300);
        public Dictionary<long, Rect> Bounds = new() { [1] = Original, [2] = Original };
        public long FailMove, FailRestore;
        /// <summary>Captures only existing test windows.</summary>
        public WindowSnapshot? Capture(long handle) => Bounds.TryGetValue(handle, out var bounds)
            ? new(handle, 1, 1, new(0, 1, default, default, bounds), new(100, 100)) : null;
        /// <summary>Simulates windows closing between capture and undo.</summary>
        public bool IsCurrent(WindowSnapshot window) => Bounds.ContainsKey(window.Handle);
        /// <summary>Applies a rectangle unless the fixture models a failed native move.</summary>
        public void Place(WindowSnapshot window, Rect bounds)
        {
            if (window.Handle == FailMove) throw new InvalidOperationException("App refused resize.");
            Bounds[window.Handle] = bounds;
        }
        /// <summary>Restores a rectangle unless the fixture models a failed native restore.</summary>
        public void Restore(WindowSnapshot window)
        {
            if (window.Handle == FailRestore) throw new InvalidOperationException("App refused restore.");
            Bounds[window.Handle] = window.Placement.NormalBounds;
        }
    }
}
