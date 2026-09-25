using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Files;

/// <summary>Modal for a destructive action. Cancel starts focused, so a stray Enter never confirms.</summary>
public sealed class ConfirmView : Window
{
    private readonly Button _confirm;
    private readonly Button? _alternative;
    private readonly Button _cancel;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Confirmed;

    /// <summary>A third way out, offered when <c>alternativeLabel</c> is given — e.g. showing the diff before deciding (#267).</summary>
    public event EventHandler? Alternative;

    public event EventHandler? Cancelled;

    public ConfirmView(string title, string message, string confirmLabel, string? alternativeLabel = null)
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

        _confirm = Choice(confirmLabel);
        _confirm.Accepting += (_, e) => { e.Handled = true; Confirmed?.Invoke(this, EventArgs.Empty); };
        if (alternativeLabel is not null)
        {
            _alternative = Choice(alternativeLabel);
            _alternative.Accepting += (_, e) => { e.Handled = true; Alternative?.Invoke(this, EventArgs.Empty); };
        }
        _cancel = Choice("Cancel");
        _cancel.Accepting += (_, e) => { e.Handled = true; Cancelled?.Invoke(this, EventArgs.Empty); };

        Add(text, _confirm);
        if (_alternative is not null) Add(_alternative);
        Add(_cancel);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.ConfirmCancel, () => Cancelled?.Invoke(this, EventArgs.Empty));
        _scopeKeybindings.Bind("Esc", CommandIds.ConfirmCancel);
    }

    // Added in order, so Pos.Align centres the row however many choices there are; cancel last (style guide §2).
    private static Button Choice(string label) => new()
    {
        Text = label,
        X = Pos.Align(Alignment.Center),
        Y = Pos.AnchorEnd(1),
    };

    public bool FocusCancel() => _cancel.SetFocus();

    public bool ConfirmHasFocus => _confirm.HasFocus;

    public bool AlternativeHasFocus => _alternative?.HasFocus is true;
}
