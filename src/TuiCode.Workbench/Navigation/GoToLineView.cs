using System.Numerics;
using MentalDesk.Tui.Fields;
using MentalDesk.Tui;
using MentalDesk.Tui.Dialogs;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Navigation;

/// <summary>
/// Modal "Go to line:column" prompt. Single text field — accepts either
/// <c>LINE</c> or <c>LINE:COL</c> (1-based, matching VS Code / IntelliJ).
/// </summary>
public sealed class GoToLineView : AppDialog
{
    private const string GoId = "goToLine.go";
    private const int ContentRows = 4;

    private readonly TextField _input;
    private readonly VerbatimLabel _label;
    private readonly int _totalLines;

    public GoToLineView(int totalLines, int currentLine)
        : base("Go to Line:Column", 50, ContentRows)
    {
        if (totalLines < 1) totalLines = 1;
        _totalLines = totalLines;

        _label = new VerbatimLabel
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Text = $"Line[:Col]  (1-{totalLines}, current: {currentLine})",
        };
        _input = new PlaceholderField { X = 1, Y = 1, Width = Dim.Fill(1) };
        Add(_label, _input);
        Check(_input, _label, Refusal);

        Commands.Register(GoId, "Go to", OnConfirm);
        Keys.Bind("Ctrl+Enter", GoId);
        var go = CommandButton(ButtonKind.Primary, GoId);
        go.IsDefault = false;
        AddButtonRow(ContentRows - 1, go, CommandButton(ButtonKind.Secondary, CancelId));
    }

    /// <summary>Zero-based, for <c>EditorTab.MoveCursor</c>; set once <see cref="AppDialog.Confirmed"/>.</summary>
    public (int Row, int Column) Target { get; private set; }

    public bool FocusInput() => _input.SetFocus();

    internal VerbatimLabel FieldLabel => _label;

    private string Typed => _input.Text?.Trim() ?? string.Empty;

    private string? Refusal()
    {
        if (Typed.Length == 0) return "Line is needed";
        var parts = Typed.Split(':', 2);
        if (!BigInteger.TryParse(parts[0], out var line) || (parts.Length == 2 && !IsColumn(parts[1])))
            return "Line must be a number, or LINE:COL";
        return line < 1 || line > _totalLines ? $"Line must be 1\u2013{_totalLines}" : null;
    }

    private static bool IsColumn(string typed) => int.TryParse(typed, out var column) && column >= 1;

    private void OnConfirm()
    {
        if (!Checked()) return;
        TryParse(Typed, out var line, out var col);
        Target = (line - 1, col - 1);
        Accept();
    }

    internal static bool TryParse(string raw, out int line, out int column)
    {
        line = 0;
        column = 1;
        var parts = raw.Split(':', 2);
        if (!int.TryParse(parts[0], out line) || line < 1) return false;
        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[1], out column) || column < 1) return false;
        }
        return true;
    }
}
