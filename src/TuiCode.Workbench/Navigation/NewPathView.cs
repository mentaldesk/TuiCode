using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Navigation;

/// <summary>
/// Modal prompt for creating a new file or folder. A single text field, pre-filled with the
/// target directory (relative to the workspace root) so the user only types the leaf name —
/// though a deeper path like <c>src/utils/io.cs</c> creates the intermediate directories too.
/// Enter confirms, Esc cancels.
///
/// Owns its own <see cref="ICommandService"/> + <see cref="IKeybindingService"/>; the
/// <see cref="WorkbenchHost"/> pushes <see cref="Scope"/> on open and pops it on close.
/// </summary>
public sealed class NewPathView : Window
{
    private readonly TextField _input;
    private readonly Label _error;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;
    public event EventHandler<string>? Submitted;

    public NewPathView(bool directory, string prefill)
    {
        prefill ??= string.Empty;

        Title = directory ? "New Folder" : "New File";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 60;
        Height = 7;
        // Required for descendant focus — same reason as the other modals.
        CanFocus = true;

        var hint = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Text = directory
                ? "Folder path (relative to workspace root)"
                : "File path (relative to workspace root)",
        };

        _input = new TextField
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Text = prefill,
        };
        // Park the caret after the pre-filled directory so typing appends the leaf name.
        _input.InsertionPoint = prefill.Length;

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

    /// <summary>Surface a creation failure (e.g. the path already exists) and keep the modal open.</summary>
    public void ShowError(string message) => _error.Text = message;

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.NewPathCancel, () => Cancelled?.Invoke(this, EventArgs.Empty));
        _scopeCommands.Register(CommandIds.NewPathConfirm, OnConfirm);

        _scopeKeybindings.Bind("Esc", CommandIds.NewPathCancel);
        _scopeKeybindings.Bind("Enter", CommandIds.NewPathConfirm);
    }

    private void OnConfirm()
    {
        var raw = _input.Text?.ToString()?.Trim() ?? string.Empty;
        // An empty (or slash-only) path has no leaf to create — treat Enter as cancel.
        if (raw.Trim('/', '\\').Length == 0)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
            return;
        }

        Submitted?.Invoke(this, raw);
    }
}
