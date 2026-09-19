using TuiCode.Abstractions;

namespace TuiCode.Workbench.Settings;

internal sealed record KeybindingRow(string CommandId, string Label, CommandScope Scope, KeyBinding? Binding)
{
    public string Keys => Binding?.Display ?? KeybindingRows.Unbound;

    public string Display => KeybindingRows.Columns(Label, Keys, Scope.ToString());
}

/// <summary>The Keyboard Shortcuts pane's rows (#142): one per (command, binding), or one unbound row per command, filtered.</summary>
internal static class KeybindingRows
{
    public const string Unbound = "(unbound)";

    // Fits the pane at 80 columns.
    private const int RowWidth = 49;
    private const int KeysWidth = 14;
    private const int WhenWidth = 8;

    public static string Header => Columns("Command", "Keys", "When");

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

    internal static string Columns(string command, string keys, string when)
    {
        keys = Truncate(keys, RowWidth - WhenWidth - 12);
        var keysWidth = Math.Max(KeysWidth, keys.Length + 1);
        var commandWidth = RowWidth - WhenWidth - keysWidth;
        return $"{Truncate(command, commandWidth - 1).PadRight(commandWidth)}{keys.PadRight(keysWidth)}{when}";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
