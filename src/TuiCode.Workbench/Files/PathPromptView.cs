using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Fields;
using System.Globalization;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Files;

/// <summary>
/// Modal prompt for a path relative to the workspace root, used to create an entry (New File or
/// Folder) and to rename or move one (#101). A single text field; Ctrl+Enter confirms, Esc cancels.
/// </summary>
public sealed class PathPromptView : AppDialog
{
    private const int ContentRows = 4;

    private readonly TextField _input;
    private readonly VerbatimLabel _label;

    private bool? _isDirectory;
    private NamePart _selected;

    public event EventHandler<string>? Submitted;

    public PathPromptView(string title, string hint, string prefill, string confirm)
        : base(title, 60, ContentRows)
    {
        prefill ??= string.Empty;

        _label = new VerbatimLabel
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Text = hint,
        };

        _input = new PlaceholderField
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Text = prefill,
        };
        // Park the caret at the end so typing appends to the pre-filled path.
        _input.InsertionPoint = prefill.Length;

        Add(_label, _input);
        Check(_input, _label, () => Path.Trim().Trim('/', '\\').Length == 0 ? "Path is needed" : null);

        Commands.Register(CommandIds.PathPromptConfirm, confirm, OnConfirm);
        Commands.Register(CommandIds.PathPromptCycleSelection, "Cycle selection", CycleSelection);
        Keys.Bind("Ctrl+Enter", CommandIds.PathPromptConfirm);
        Keys.Bind("F2", CommandIds.PathPromptCycleSelection);

        var go = CommandButton(ButtonKind.Primary, CommandIds.PathPromptConfirm);
        go.IsDefault = false;
        AddButtonRow(ContentRows - 1, go, CommandButton(ButtonKind.Secondary, CancelId));
    }

    public string Path => _input.Text ?? string.Empty;

    internal VerbatimLabel FieldLabel => _label;

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

    /// <summary>Marks the path with why the disk refused it, keeping the modal open.</summary>
    public void ShowError(string message) => Refuse(_input, $"Path: {char.ToLowerInvariant(message[0])}{message[1..]}");

    /// <summary>Closes the prompt once the host has done what it was asked.</summary>
    public void Finish() => Accept();

    private void OnConfirm()
    {
        if (Checked()) Submitted?.Invoke(this, Path.Trim());
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
