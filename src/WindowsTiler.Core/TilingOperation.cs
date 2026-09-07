namespace WindowsTiler.Core;

/// <summary>A point in Windows desktop coordinates.</summary>
public readonly record struct Point(int X, int Y);
/// <summary>The restorable state returned by GetWindowPlacement; normal bounds use workspace coordinates.</summary>
public sealed record Placement(uint Flags, uint ShowCommand, Point MinPosition, Point MaxPosition, Rect NormalBounds);
/// <summary>A window identity, original placement and minimum permitted outer size.</summary>
public sealed record WindowSnapshot(long Handle, int ProcessId, long ProcessStarted, Placement Placement, WindowSize Minimum);
/// <summary>Counts windows successfully changed and windows excluded from an operation.</summary>
public readonly record struct OperationResult(int Applied, int Skipped);

/// <summary>Isolates foreign-window operations from the platform-independent transaction logic.</summary>
public interface IWindowAccess
{
    /// <summary>Captures an eligible window, or returns null when it cannot safely be tiled.</summary>
    WindowSnapshot? Capture(long handle);
    /// <summary>Checks that the handle still belongs to the captured process on the current desktop.</summary>
    bool IsCurrent(WindowSnapshot window);
    /// <summary>Restores and moves a window to screen-coordinate bounds, or throws on failure.</summary>
    void Place(WindowSnapshot window, Rect bounds);
    /// <summary>Restores the original placement, including its minimized or maximized state.</summary>
    void Restore(WindowSnapshot window);
}

/// <summary>Stores the last recoverable operation before any windows are changed.</summary>
public interface IUndoStore
{
    /// <summary>Reads remaining undo entries, returning an empty list when none exist.</summary>
    IReadOnlyList<WindowSnapshot> Read();
    /// <summary>Atomically replaces undo entries; an empty list clears the previous operation.</summary>
    void Write(IReadOnlyList<WindowSnapshot> windows);
}

/// <summary>Coordinates validation, write-ahead undo storage, tiling and best-effort recovery.</summary>
public sealed class TilingOperation(IWindowAccess windows, IUndoStore undo)
{
    /// <summary>Tiles eligible handles; preserves recovery information if any placement fails.</summary>
    public OperationResult Tile(IReadOnlyList<long> handles, IReadOnlyList<Rect> monitors)
    {
        if (handles.Count is < 1 or > 128) throw new ArgumentException("Select between 1 and 128 windows.");
        var selected = handles.Distinct().Select(windows.Capture).OfType<WindowSnapshot>().ToArray();
        if (selected.Length == 0) throw new InvalidOperationException("No resizable windows in this group are available on the current desktop.");
        var layout = Layout.Create(selected.Select(w => w.Minimum).ToArray(), monitors);
        var previous = undo.Read();
        undo.Write(selected); // A disk failure must occur before the first desktop mutation.
        List<WindowSnapshot> attempted = [];
        try
        {
            foreach (var tile in layout)
            {
                var window = selected[tile.WindowIndex];
                if (!windows.IsCurrent(window)) throw new InvalidOperationException("A selected window closed or changed desktop. Try again.");
                attempted.Add(window); // A failed native call can still have partially changed this window.
                windows.Place(window, tile.Bounds);
            }
        }
        catch (Exception error)
        {
            var remaining = RestoreEntries(attempted).Remaining;
            undo.Write(remaining.Count == 0 ? previous : remaining);
            throw new InvalidOperationException(remaining.Count == 0
                ? "Tiling failed; changed windows were restored. " + error.Message
                : "Tiling failed; some windows could not be restored. Use Undo tiling to retry. " + error.Message, error);
        }
        return new(selected.Length, handles.Distinct().Count() - selected.Length);
    }
    /// <summary>Restores remaining live windows and retains entries whose restore failed.</summary>
    public OperationResult Undo()
    {
        var result = RestoreEntries(undo.Read());
        undo.Write(result.Remaining);
        if (result.Remaining.Count > 0)
            throw new InvalidOperationException($"{result.Remaining.Count} window(s) could not be restored. Use Undo tiling to retry.");
        return result.Result;
    }

    /// <summary>Attempts each restore independently so a hung or closed window cannot block the rest.</summary>
    private (OperationResult Result, List<WindowSnapshot> Remaining) RestoreEntries(IReadOnlyList<WindowSnapshot> entries)
    {
        var applied = 0;
        var skipped = 0;
        List<WindowSnapshot> remaining = [];
        foreach (var window in entries)
        {
            if (!windows.IsCurrent(window)) { skipped++; continue; }
            try { windows.Restore(window); applied++; }
            catch (Exception) { remaining.Add(window); }
        }
        return (new(applied, skipped), remaining);
    }
}
