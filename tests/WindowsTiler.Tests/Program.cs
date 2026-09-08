using WindowsTiler.Core;

// Dependency-free runner: each case checks behavior and contributes to the process exit code.
var tests = new (string Name, Action Run)[]
{
    ("undo survives restart and rejects corrupt or oversized state", UndoStoreTests.Run),
    ("tiling is recoverable across storage, move, restore and window-lifetime failures", OperationTests.Run),
    ("native command preserves negative coordinates and deduplicates HWNDs", () =>
    {
        var command = Command.Parse(["tile", "--scope", "all", "--point", "-1280", "-50", "--windows", "123", "456", "123"]);
        Check(command.Action == "tile" && command.AllMonitors && command.X == -1280 && command.Y == -50, "Command meaning changed.");
        Check(command.Windows!.SequenceEqual(new long[] { 123, 456 }), "Window handles not preserved.");
    }),
    ("malformed native commands cannot select windows", () =>
    {
        foreach (var args in new string[][] {
            ["tile"], ["tile", "--scope", "everything", "--point", "0", "0", "--windows", "123"],
            ["tile", "--scope", "monitor", "--point", "0", "0", "--windows", "-1"],
            ["tile", "--scope", "monitor", "--point", "0", "0", "--windows", "0"],
            ["tile", "--scope", "monitor", "--point", "0", "0", "--windows", "4294967296"],
            ["undo", "--windows", "123"], ["unknown"] })
            Throws<ArgumentException>(() => Command.Parse(args));
    }),
    ("single window fills the work area including negative origin", () =>
    {
        var tiles = Layout.Create([new(100, 100)], [new(-1920, 40, 1920, 1040)]);
        Check(tiles.Count == 1, "Expected one tile.");
        Check(tiles[0] == new Tile(0, 0, new(-1920, 40, 1920, 1040)), "Work area was not preserved.");
    }),
    ("odd counts cover the monitor without overlaps or rounding gaps", () =>
    {
        for (var count = 2; count <= 31; count++)
        {
            Rect area = new(-101, 37, 1001, 733);
            var tiles = Layout.Create(Enumerable.Repeat(new WindowSize(1, 1), count).ToArray(), [area]);
            Check(tiles.Count == count, "A window is missing.");
            Check(tiles.Select(t => t.WindowIndex).Distinct().Count() == count, "A window was tiled twice.");
            Check(tiles.Sum(t => (long)t.Bounds.Width * t.Bounds.Height) == 733733, "There is unused space.");
            foreach (var tile in tiles)
            {
                var b = tile.Bounds;
                Check(b.X >= -101 && b.Y >= 37 && b.Right <= 900 && b.Bottom <= 770, "Tile leaves the work area.");
                Check(b.Width > 0 && b.Height > 0, "Empty tile.");
                foreach (var other in tiles.Where(t => t.WindowIndex != tile.WindowIndex))
                    Check(b.Right <= other.Bounds.X || other.Bounds.Right <= b.X || b.Bottom <= other.Bounds.Y || other.Bounds.Bottom <= b.Y, "Tiles overlap.");
            }
        }
    }),
    ("all monitors distributes each window once and uses each display", () =>
    {
        var tiles = Layout.Create(Enumerable.Repeat(new WindowSize(100, 100), 5).ToArray(),
            [new(-1280, 0, 1280, 984), new(0, 0, 1920, 1040)]);
        Check(tiles.Count == 5, "A window is missing.");
        Check(tiles.Count(t => t.MonitorIndex == 0) == 3 && tiles.Count(t => t.MonitorIndex == 1) == 2, "Displays are not balanced.");
        Check(tiles.Select(t => t.WindowIndex).Order().SequenceEqual(Enumerable.Range(0, 5)), "Window identity lost.");
    }),
    ("minimum sizes choose a feasible orientation", () =>
    {
        var tiles = Layout.Create([new(900, 100), new(900, 100)], [new(0, 0, 1000, 800)]);
        Check(tiles.Count == 2, "A window is missing.");
        Check(tiles.All(t => t.Bounds.Width >= 900 && t.Bounds.Height >= 100), "Minimum size ignored.");
    }),
    ("mixed window sizes split into bands when one grid can't fit either", () =>
    {
        WindowSize big = new(774, 774), small = new(516, 516);
        WindowSize[] windows = [big, small, big, small, big, small, big, small, big, small];
        foreach (Rect area in new Rect[] { new(0, 0, 3840, 2088), new(0, 0, 5120, 1392) })
        {
            var tiles = Layout.Create(windows, [area]);
            Check(tiles.Count == 10, "A window is missing.");
            Check(tiles.Select(t => t.WindowIndex).Distinct().Count() == 10, "A window was tiled twice.");
            foreach (var tile in tiles)
            {
                var min = windows[tile.WindowIndex];
                Check(tile.Bounds.Width >= min.Width && tile.Bounds.Height >= min.Height, "Minimum size ignored.");
                Check(tile.Bounds.X >= area.X && tile.Bounds.Y >= area.Y &&
                    tile.Bounds.Right <= area.Right && tile.Bounds.Bottom <= area.Bottom, "Tile leaves the work area.");
                foreach (var other in tiles.Where(t => t.WindowIndex != tile.WindowIndex))
                    Check(tile.Bounds.Right <= other.Bounds.X || other.Bounds.Right <= tile.Bounds.X ||
                        tile.Bounds.Bottom <= other.Bounds.Y || other.Bounds.Bottom <= tile.Bounds.Y, "Tiles overlap.");
            }
        }
    }),
    ("impossible layout fails before any windows can move", () =>
        Throws<InvalidOperationException>(() => Layout.Create([new(900, 700), new(900, 700)], [new(0, 0, 1000, 800)]))),
    ("empty and invalid inputs rejected", () =>
    {
        Throws<ArgumentException>(() => Layout.Create([], [new(0, 0, 100, 100)]));
        Throws<ArgumentException>(() => Layout.Create([new(1, 1)], []));
        Throws<ArgumentException>(() => Layout.Create([new(1, 1)], [new(0, 0, 0, 100)]));
        Throws<ArgumentException>(() => Layout.Create([new(-1, 1)], [new(0, 0, 100, 100)]));
    })
};
var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

/// <summary>Fails the current test with its contract-specific explanation.</summary>
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
/// <summary>Requires an exception type so invalid input cannot silently proceed.</summary>
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
