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
    private readonly int _totalLines;

    public GoToLineView(int totalLines, int currentLine)
        : base("Go to Line:Column", 50, ContentRows)
    {
        if (totalLines < 1) totalLines = 1;
        _totalLines = totalLines;

        var hint = new VerbatimLabel
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Text = $"Line[:Col]  (1-{totalLines}, current: {currentLine})",
        };
        _input = new TextField { X = 1, Y = 1, Width = Dim.Fill(1) };
        Add(hint, _input);

        Commands.Register(GoId, "Go to", OnConfirm);
        Keys.Bind("Enter", GoId);
        AddButtonRow(ContentRows - 1, CommandButton(ButtonKind.Primary, GoId), CommandButton(ButtonKind.Secondary, CancelId));
    }

    /// <summary>Zero-based, for <c>EditorTab.MoveCursor</c>; set once <see cref="AppDialog.Confirmed"/>.</summary>
    public (int Row, int Column) Target { get; private set; }

    public bool FocusInput() => _input.SetFocus();

    private void OnConfirm()
    {
        var raw = _input.Text?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            Cancel();
            return;
        }

        if (!TryParse(raw, out var line, out var col))
        {
            ShowAlert("Expected: LINE or LINE:COL", Severity.Error);
            return;
        }

        if (line < 1 || line > _totalLines)
        {
            ShowAlert($"Line out of range (1-{_totalLines})", Severity.Error);
            return;
        }

        Target = (line - 1, Math.Max(0, col - 1));
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
