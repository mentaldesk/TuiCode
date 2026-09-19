using TuiCode.Abstractions;

namespace TuiCode.Workbench.Settings;

internal sealed record KeybindingRow(string CommandId, string Label, CommandScope Scope, KeyBinding? Binding)
{
    public string Keys => Binding?.Display ?? KeybindingRows.Unbound;

    public string Display(int width) => KeybindingRows.Columns(Label, Keys, Scope.ToString(), width);
}

/// <summary>The Keyboard Shortcuts pane's rows (#142): one per (command, binding), or one unbound row per command, filtered.</summary>
internal static class KeybindingRows
{
    public const string Unbound = "(unbound)";

    private const int Gap = 2;
    private const int WhenWidth = 8;
    private const int MinKeysWidth = 12;
    private const int MinCommandWidth = 10;

    public static string Header(int width) => Columns("Command", "Keys", "When", width);

    public static IReadOnlyList<KeybindingRow> Build(
        IEnumerable<CommandDescriptor> commands, IReadOnlyCollection<KeyBinding> bindings, string filter)
    {
        var rows = new List<KeybindingRow>();
        foreach (var command in commands.OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase))
        {
            var bound = bindings.Where(b => string.Equals(b.CommandId, command.Id, StringComparison.Ordinal)).ToArray();
            if (bound.Length == 0)
                rows.Add(new KeybindingRow(command.Id, command.Label, command.Scope, null));
            foreach (var b in bound)
                rows.Add(new KeybindingRow(command.Id, command.Label, command.Scope, b));
        }

        filter = filter.Trim();
        return filter.Length == 0 ? rows : rows.Where(r => Matches(r, filter)).ToList();
    }

    private static bool Matches(KeybindingRow r, string filter) =>
        r.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (r.Binding?.Display.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
        || r.Scope.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase);

    // A chord longer than the Keys column takes the room from its own row's command.
    internal static string Columns(string command, string keys, string when, int width)
    {
        var available = Math.Max(width - WhenWidth - 2 * Gap, MinCommandWidth + MinKeysWidth);
        keys = Truncate(keys, available - MinCommandWidth);
        var commandWidth = Math.Min(available - Math.Max(MinKeysWidth, available / 3), available - keys.Length);
        var keysWidth = available - commandWidth;
        return $"{Truncate(command, commandWidth).PadRight(commandWidth + Gap)}{keys.PadRight(keysWidth + Gap)}{when}";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
