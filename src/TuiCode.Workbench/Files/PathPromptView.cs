using System.Globalization;
using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Files;

/// <summary>
/// Modal prompt for a path relative to the workspace root, used to create an entry (New File or
/// Folder) and to rename or move one (#101). A single text field; Enter confirms, Esc cancels.
///
/// Owns its own <see cref="ICommandService"/> + <see cref="IKeybindingService"/>; the
/// <see cref="WorkbenchHost"/> pushes <see cref="Scope"/> on open and pops it on close.
/// </summary>
public sealed class PathPromptView : Window
{
    private readonly TextField _input;
    private readonly Label _error;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    private bool? _isDirectory;
    private NamePart _selected;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;
    public event EventHandler<string>? Submitted;

    public PathPromptView(string title, string hint, string prefill)
    {
        prefill ??= string.Empty;

        Title = title;
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 60;
        Height = 7;
        // Required for descendant focus — same reason as the other modals.
        CanFocus = true;

        var hintLabel = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Text = hint,
        };

        _input = new TextField
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Text = prefill,
        };
        // Park the caret at the end so typing appends to the pre-filled path.
        _input.InsertionPoint = prefill.Length;

        _error = new Label
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill(1),
            Text = string.Empty,
        };

        Add(hintLabel, _input, _error);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();
    }

    public string Path => _input.Text ?? string.Empty;

    public string SelectedText => _input.SelectedText ?? string.Empty;

    /// <summary>Select the name without its extension, as VS Code's rename does; F2 then cycles name → extension → stem.</summary>
    public void SelectName(bool isDirectory)
    {
        _isDirectory = isDirectory;
        Select(NamePart.Stem);
    }

    public bool FocusInput()
    {
        var focused = _input.SetFocus();
        // TextField selects all of its text on focus.
        if (_isDirectory is not null) Select(_selected);
        return focused;
    }

    /// <summary>Surface a failure (e.g. the path already exists) and keep the modal open.</summary>
    public void ShowError(string message) => _error.Text = message;

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.PathPromptCancel, () => Cancelled?.Invoke(this, EventArgs.Empty));
        _scopeCommands.Register(CommandIds.PathPromptConfirm, OnConfirm);
        _scopeCommands.Register(CommandIds.PathPromptCycleSelection, CycleSelection);

        _scopeKeybindings.Bind("Esc", CommandIds.PathPromptCancel);
        _scopeKeybindings.Bind("Enter", CommandIds.PathPromptConfirm);
        _scopeKeybindings.Bind("F2", CommandIds.PathPromptCycleSelection);
    }

    private void OnConfirm()
    {
        var raw = Path.Trim();
        // An empty (or slash-only) path names nothing — treat Enter as cancel.
        if (raw.Trim('/', '\\').Length == 0)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
            return;
        }

        Submitted?.Invoke(this, raw);
    }

    private void CycleSelection()
    {
        if (_isDirectory is null) return;
        Select(_selected switch
        {
            NamePart.Stem => NamePart.Name,
            NamePart.Name => NamePart.Extension,
            _ => NamePart.Stem,
        });
    }

    private void Select(NamePart part)
    {
        _selected = part;
        var text = Path;
        var (start, length) = NameSpan(text, _isDirectory ?? false, part);
        // TextField positions count text elements, not UTF-16 chars.
        var from = new StringInfo(text[..start]).LengthInTextElements;
        var to = new StringInfo(text[..(start + length)]).LengthInTextElements;
        _input.InsertionPoint = to;
        _input.SelectedStart = from;
    }

    public enum NamePart { Stem, Name, Extension }

    /// <summary>A span of the last path segment; folders and dotfiles have no extension, so every part is the whole name.</summary>
    internal static (int Start, int Length) NameSpan(string path, bool isDirectory, NamePart part)
    {
        var start = path.LastIndexOfAny(['/', '\\']) + 1;
        var name = path[start..];
        var dot = isDirectory ? -1 : name.LastIndexOf('.');
        if (dot <= 0) return (start, name.Length);
        return part switch
        {
            NamePart.Stem => (start, dot),
            NamePart.Extension => (start + dot + 1, name.Length - dot - 1),
            _ => (start, name.Length),
        };
    }
}
