using MentalDesk.Tui.Keys;
using TuiCode.Abstractions;
using TuiCode.Workbench.Menus;

namespace TuiCode.Workbench.Help;

/// <summary>What F1 shows: the place's keys, and every Global command that has a key and can run now under its menu's heading.</summary>
public static class HelpSheet
{
    public static KeySheet For(KeyColumn? here, ICommandService commands, IKeybindingService keybindings) =>
        new(here, Everywhere(commands, keybindings, CommandMenu.Layout));

    internal static KeyColumn Everywhere(ICommandService commands, IKeybindingService keybindings, IEnumerable<(string Title, string[] Ids)> menu)
    {
        var bound = commands.Registered
            .Where(command => command.Scope == CommandScope.Global && commands.IsEnabled(command.Id))
            .Select(command => Row(command, keybindings))
            .OfType<(string Id, KeyRow Row)>()
            .ToList();
        var rows = bound.ToDictionary(command => command.Id, command => command.Row, StringComparer.Ordinal);
        var listed = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<KeyGroup>();
        foreach (var (title, ids) in menu)
        {
            KeyRow[] items = [.. ids.Where(id => rows.ContainsKey(id) && listed.Add(id)).Select(id => rows[id])];
            if (items.Length > 0) groups.Add(new KeyGroup(title.Replace("_", "", StringComparison.Ordinal), items));
        }
        KeyRow[] other = [.. bound.Where(command => !listed.Contains(command.Id)).Select(command => command.Row)];
        if (other.Length > 0) groups.Add(new KeyGroup(KeySheet.OtherHeading, other));
        return new KeyColumn("Everywhere", groups);
    }

    public static KeyColumn Place(string heading, IEnumerable<KeyRow> rows) => new(heading, [new KeyGroup(null, [.. rows])]);

    public static IReadOnlyList<KeyRow> Rows(this KeyColumn column) => [.. column.Groups.SelectMany(group => group.Rows)];

    private static (string Id, KeyRow Row)? Row(CommandDescriptor command, IKeybindingService keybindings)
    {
        var keys = keybindings.Bindings.Where(binding => binding.CommandId == command.Id).Select(binding => binding.Display).ToList();
        return keys.Count == 0 ? null : (command.Id, new KeyRow(string.Join(", ", keys), command.Label));
    }
}
