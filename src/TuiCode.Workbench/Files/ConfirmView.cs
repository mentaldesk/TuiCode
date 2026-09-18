using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Files;

/// <summary>Yes/no modal for a destructive action. Cancel starts focused, so a stray Enter never confirms.</summary>
public sealed class ConfirmView : Window
{
    private readonly Button _confirm;
    private readonly Button _cancel;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Confirmed;
    public event EventHandler? Cancelled;

    public ConfirmView(string title, string message, string confirmLabel)
    {
        Title = title;
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 60;
        Height = message.Split('\n').Length + 5;
        CanFocus = true;

        var text = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Text = message,
        };

        _confirm = new Button { Text = confirmLabel, X = Pos.Center() - 12, Y = Pos.AnchorEnd(1) };
        _cancel = new Button { Text = "Cancel", X = Pos.Center() + 2, Y = Pos.AnchorEnd(1) };
        _confirm.Accepting += (_, e) => { e.Handled = true; Confirmed?.Invoke(this, EventArgs.Empty); };
        _cancel.Accepting += (_, e) => { e.Handled = true; Cancelled?.Invoke(this, EventArgs.Empty); };

        Add(text, _confirm, _cancel);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.ConfirmCancel, () => Cancelled?.Invoke(this, EventArgs.Empty));
        _scopeKeybindings.Bind("Esc", CommandIds.ConfirmCancel);
    }

    public bool FocusCancel() => _cancel.SetFocus();

    public bool ConfirmHasFocus => _confirm.HasFocus;
}
