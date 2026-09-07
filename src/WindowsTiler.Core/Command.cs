namespace WindowsTiler.Core;

/// <summary>A validated command sent by the native menu bridge.</summary>
public sealed record Command(string Action, bool AllMonitors = false, int X = 0, int Y = 0, long[]? Windows = null)
{
    /// <summary>Accepts only the documented grammar and numeric HWNDs; never expands process names or shell text.</summary>
    public static Command Parse(string[] args)
    {
        if (args.Length == 0) return new("status");
        if (args.Length == 1 && args[0] is "status" or "setup" or "undo" or "--help") return new(args[0]);
        if (args.Length < 8 || args[0] != "tile" || args[1] != "--scope" ||
            args[2] is not ("monitor" or "all") || args[3] != "--point" || args[6] != "--windows" ||
            !int.TryParse(args[4], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(args[5], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var y))
            throw new ArgumentException("Usage: tile --scope monitor|all --point X Y --windows HWND [HWND ...], undo, or status.");
        if (args.Length > 135) throw new ArgumentException("At most 128 windows can be tiled at once.");
        List<long> windows = [];
        foreach (var value in args.Skip(7))
        {
            if (!uint.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var handle) || handle == 0)
                throw new ArgumentException("Window handles must be nonzero unsigned 32-bit decimal values.");
            if (!windows.Contains(handle)) windows.Add(handle);
        }
        return new("tile", args[2] == "all", x, y, windows.ToArray());
    }
}
