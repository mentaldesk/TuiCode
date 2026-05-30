using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Navigation;

/// <summary>
/// Modal "Go to line:column" prompt. Single text field — accepts either
/// <c>LINE</c> or <c>LINE:COL</c> (1-based, matching VS Code / IntelliJ).
/// </summary>
public sealed class GoToLineView : Window
{
    private readonly TextField _input;
    private readonly Label _error;
    private readonly int _totalLines;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;
    public event EventHandler<(int Row, int Column)>? Submitted;

    public GoToLineView(int totalLines, int currentLine)
    {
        if (totalLines < 1) totalLines = 1;
        _totalLines = totalLines;

        Title = "Go to Line:Column";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 50;
        Height = 7;
        CanFocus = true;

        var hint = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Text = $"Line[:Col]  (1-{totalLines}, current: {currentLine})",
        };

        _input = new TextField
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
        };

        _error = new Label
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill(1),
            Text = string.Empty,
        };

        Add(hint, _input, _error);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();
    }

    public bool FocusInput() => _input.SetFocus();

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.GoToLineCancel, () => Cancelled?.Invoke(this, EventArgs.Empty));
        _scopeCommands.Register(CommandIds.GoToLineConfirm, OnConfirm);

        _scopeKeybindings.Bind("Esc", CommandIds.GoToLineCancel);
        _scopeKeybindings.Bind("Enter", CommandIds.GoToLineConfirm);
    }

    private void OnConfirm()
    {
        var raw = _input.Text?.ToString()?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!TryParse(raw, out var line, out var col))
        {
            _error.Text = "Expected: LINE or LINE:COL";
            return;
        }

        if (line < 1 || line > _totalLines)
        {
            _error.Text = $"Line out of range (1-{_totalLines})";
            return;
        }

        // Convert to zero-based for EditorTab.MoveCursor.
        Submitted?.Invoke(this, (line - 1, Math.Max(0, col - 1)));
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
