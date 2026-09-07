using System.Text.Json;

namespace WindowsTiler.Core;

/// <summary>Persists a versioned undo snapshot using an atomic file replacement.</summary>
public sealed class UndoStore(string path) : IUndoStore
{
    /// <summary>The on-disk contract; unknown versions are rejected rather than interpreted speculatively.</summary>
    private sealed record Document(int Version, WindowSnapshot[] Windows);

    /// <summary>Loads a bounded, validated snapshot; malformed state prevents a new operation from overwriting it.</summary>
    public IReadOnlyList<WindowSnapshot> Read()
    {
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 131072) throw new InvalidOperationException("Undo data is too large.");
        var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
        if (document is null || document.Version != 1 || document.Windows is null || document.Windows.Length > 128 ||
            document.Windows.Any(w => w is null || w.Handle <= 0 || w.Handle > uint.MaxValue || w.ProcessId <= 0 ||
                w.ProcessStarted <= 0 || w.Placement is null || w.Placement.ShowCommand is not (1 or 2 or 3) ||
                w.Placement.NormalBounds.Width <= 0 || w.Placement.NormalBounds.Height <= 0))
            throw new InvalidOperationException("Undo data is invalid or belongs to an unsupported version.");
        return document.Windows;
    }

    /// <summary>Flushes a complete temporary document before replacing the last recoverable snapshot.</summary>
    public void Write(IReadOnlyList<WindowSnapshot> windows)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Document(1, windows.ToArray()));
                stream.Flush(true);
            }
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
