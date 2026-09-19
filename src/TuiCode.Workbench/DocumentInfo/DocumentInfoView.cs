using System.Globalization;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.DocumentInfo;

/// <summary>The <c>di</c> dialog (#152): the active file's facts, and its counts beside the selection's.</summary>
public sealed class DocumentInfoView : Window
{
    private const int NameWidth = 22;
    private const int Gap = 4;

    private static readonly string[] Rows = ["Lines", "Words", "Characters", "Characters (no spaces)"];

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Closed;

    public DocumentInfoView(string path, string facts, DocumentStats document, DocumentStats? selection)
    {
        Title = "Document info";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        CanFocus = true;

        var columns = new List<(string Header, string[] Values)> { ("Document", Values(document)) };
        if (selection is { } selected) columns.Add(("Selection", Values(selected)));
        Columns = columns;
        var columnWidth = columns.SelectMany(c => c.Values.Append(c.Header)).Max(s => s.Length);

        Add(new Label { X = 1, Y = 0, Text = path });
        Add(new Label { X = 1, Y = 1, Text = facts });

        for (var row = 0; row < Rows.Length; row++)
            Add(new Label { X = 1, Y = 4 + row, Text = Rows[row] });

        var x = 1 + NameWidth + Gap;
        foreach (var (header, values) in columns)
        {
            Add(Cell(x, 3, columnWidth, header));
            for (var row = 0; row < values.Length; row++)
                Add(Cell(x, 4 + row, columnWidth, values[row]));
            x += columnWidth + Gap;
        }

        var close = new Button { Text = "Close", X = Pos.Center(), Y = Pos.AnchorEnd(1), IsDefault = true };
        close.Accepting += (_, e) => { e.Handled = true; Closed?.Invoke(this, EventArgs.Empty); };
        Add(close);

        var tableWidth = x - Gap - 1;
        Width = Math.Max(tableWidth, Math.Max(path.Length, facts.Length)) + 4;
        Height = 12;

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.DocumentInfoClose, () => Closed?.Invoke(this, EventArgs.Empty));
        _scopeKeybindings.Bind("Esc", CommandIds.DocumentInfoClose);
        _scopeKeybindings.Bind("Enter", CommandIds.DocumentInfoClose);
    }

    /// <summary>Each column's header and numbers, top to bottom.</summary>
    internal IReadOnlyList<(string Header, string[] Values)> Columns { get; }

    public static string Facts(string grammar, LineEnding lineEnding, long? sizeOnDisk, bool dirty)
    {
        var parts = new List<string> { grammar, lineEnding.ToString() };
        if (sizeOnDisk is { } size) parts.Add($"{FormatSize(size)} on disk");
        if (dirty) parts.Add("unsaved changes");
        return string.Join("  •  ", parts);
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["KB", "MB", "GB", "TB"];
        if (bytes < 1024) return $"{bytes} B";
        double size = bytes;
        var unit = -1;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return string.Format(CultureInfo.CurrentCulture, "{0:0.0} {1}", size, units[unit]);
    }

    private static string[] Values(DocumentStats stats) =>
        [N(stats.Lines), N(stats.Words), N(stats.Characters), N(stats.NonWhitespaceCharacters)];

    private static string N(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static Label Cell(int x, int y, int width, string text) =>
        new() { X = x, Y = y, Width = width, Text = text, TextAlignment = Alignment.End };
}
