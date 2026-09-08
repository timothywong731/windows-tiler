namespace WindowsTiler.Core;

/// <summary>A rectangle in physical desktop pixels; origins can be negative.</summary>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    /// <summary>The exclusive right edge, checked for integer overflow.</summary>
    public int Right => checked(X + Width);
    /// <summary>The exclusive bottom edge, checked for integer overflow.</summary>
    public int Bottom => checked(Y + Height);
}

/// <summary>The minimum outer dimensions a window permits.</summary>
public readonly record struct WindowSize(int Width, int Height);
/// <summary>Associates a source window and destination monitor with a computed rectangle.</summary>
public readonly record struct Tile(int WindowIndex, int MonitorIndex, Rect Bounds);

/// <summary>Builds balanced grids that fill monitor work areas and respect minimum window sizes.</summary>
public static class Layout
{
    /// <summary>Returns a complete feasible layout, or throws before any platform mutation can occur.</summary>
    public static IReadOnlyList<Tile> Create(IReadOnlyList<WindowSize> windows, IReadOnlyList<Rect> monitors)
    {
        if (windows.Count is < 1 or > 128 || monitors.Count is < 1 or > 64)
            throw new ArgumentException("Select between 1 and 128 windows and at least one monitor.");
        if (windows.Any(w => w.Width < 1 || w.Height < 1) ||
            monitors.Any(m => m.Width < 1 || m.Height < 1))
            throw new ArgumentException("Window and monitor dimensions must be positive.");

        List<Tile> result = [];
        for (var monitor = 0; monitor < Math.Min(monitors.Count, windows.Count); monitor++)
        {
            var indices = Enumerable.Range(0, windows.Count).Where(i => i % monitors.Count == monitor).ToArray();
            var area = monitors[monitor];
            List<Tile>? best = null;
            var bestScore = double.PositiveInfinity;
            // Compare orientations instead of assuming landscape displays or a fixed column count.
            for (var rows = 1; rows <= indices.Length; rows++)
            {
                List<Tile> candidate = [];
                var next = 0;
                var score = 0.0;
                var valid = true;
                for (var row = 0; row < rows && valid; row++)
                {
                    var columns = indices.Length / rows + (row < indices.Length % rows ? 1 : 0);
                    // Divide boundaries, rather than rounded cell sizes, to retain every last pixel.
                    var top = area.Y + (int)((long)area.Height * row / rows);
                    var bottom = area.Y + (int)((long)area.Height * (row + 1) / rows);
                    for (var col = 0; col < columns; col++)
                    {
                        var left = area.X + (int)((long)area.Width * col / columns);
                        var right = area.X + (int)((long)area.Width * (col + 1) / columns);
                        var index = indices[next++];
                        Rect bounds = new(left, top, right - left, bottom - top);
                        if (bounds.Width < windows[index].Width || bounds.Height < windows[index].Height)
                        {
                            valid = false;
                            break;
                        }
                        score += Math.Pow(Math.Log((double)bounds.Width / bounds.Height / 1.6), 2);
                        candidate.Add(new(index, monitor, bounds));
                    }
                }
                if (valid && score < bestScore) { best = candidate; bestScore = score; }
            }
            if (best is null)
            {
                var sizes = string.Join(", ", indices.Select(i => $"{windows[i].Width}x{windows[i].Height}"));
                throw new InvalidOperationException("These windows cannot fit without violating their minimum sizes. " +
                    $"Try fewer windows or another monitor scope. Monitor area {area.Width}x{area.Height}, " +
                    $"{indices.Length} window(s) with minimum sizes: {sizes}.");
            }
            result.AddRange(best);
        }
        return result;
    }
}
