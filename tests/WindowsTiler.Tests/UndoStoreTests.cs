using System.Text.Json;
using WindowsTiler.Core;

/// <summary>Exercises real snapshot storage in a unique temporary directory.</summary>
internal static class UndoStoreTests
{
    /// <summary>Checks restart persistence, clearing, corrupt state rejection and temporary-file cleanup.</summary>
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WindowsTiler.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "undo.json");
        try
        {
            var store = new UndoStore(path);
            WindowSnapshot snapshot = new(123, 456, 789, new(2, 2, new(-1, -1), new(-1, -1), new(-1900, 40, 800, 600)), new(200, 100));
            if (store.Read().Count != 0) throw new Exception("Fresh installation has undo state.");
            store.Write([snapshot]);
            if (new UndoStore(path).Read().Single() != snapshot) throw new Exception("Snapshot changed across restart.");
            store.Write([]);
            if (new UndoStore(path).Read().Count != 0) throw new Exception("Undo was not cleared.");
            if (Directory.GetFiles(directory, "*.tmp").Length != 0) throw new Exception("Temporary snapshots leaked.");
            File.WriteAllText(path, "{\"Version\":999,\"Windows\":[]}");
            ExpectInvalid(store);
            File.WriteAllText(path, "truncated json");
            ExpectInvalid(store);
            File.WriteAllText(path, new string('x', 131073));
            ExpectInvalid(store);
        }
        finally
        {
            // Exclusively the GUID-named directory created by this test invocation.
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>Requires malformed persisted input to fail before it can influence window operations.</summary>
    private static void ExpectInvalid(UndoStore store)
    {
        try { store.Read(); }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException) { return; }
        throw new Exception("Invalid persisted data was accepted.");
    }
}
