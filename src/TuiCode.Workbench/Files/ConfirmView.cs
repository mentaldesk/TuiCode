using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Files;

/// <summary>One way out of a <see cref="ConfirmView"/>, other than cancelling.</summary>
/// <param name="Label">The button's text.</param>
/// <param name="Run">What taking it does, once the modal has closed.</param>
public sealed record ConfirmChoice(string Label, Action Run);

/// <summary>Modal for a destructive action. Cancel starts focused, so a stray Enter never confirms.</summary>
public sealed class ConfirmView : Window
{
    private readonly Button[] _buttons;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    /// <summary>The way out the user took. The host closes the modal first, then runs it.</summary>
    public event EventHandler<ConfirmChoice>? Chosen;

    public event EventHandler? Cancelled;

    public ConfirmView(string title, string message, params ConfirmChoice[] choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(message);

        Title = title;
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 60;
        Height = message.Split('\n').Length + 5;
        CanFocus = true;

        Add(new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Text = message,
        });

        _buttons = new Button[choices.Length + 1];
        for (var i = 0; i < choices.Length; i++)
        {
            var choice = choices[i];
            _buttons[i] = Choice(choice.Label);
            _buttons[i].Accepting += (_, e) => { e.Handled = true; Chosen?.Invoke(this, choice); };
        }
        _buttons[^1] = Choice("Cancel");
        _buttons[^1].Accepting += (_, e) => { e.Handled = true; Cancelled?.Invoke(this, EventArgs.Empty); };
        foreach (var button in _buttons) Add(button);

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

    public bool FocusCancel() => _buttons[^1].SetFocus();

    /// <summary>The label of whichever button has focus, <c>Cancel</c> included; null when none does.</summary>
    public string? FocusedChoice => Array.Find(_buttons, b => b.HasFocus)?.Text;
}
