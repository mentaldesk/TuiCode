using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Fields;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Rename;

/// <summary>Rename symbol's prompt (#697): the new name, starting as the old one, all of it selected.</summary>
public sealed class RenameSymbolView : AppDialog
{
    private const string RenameId = "renameSymbol.rename";
    private const int ContentRows = 4;

    private readonly TextField _input;
    private readonly VerbatimLabel _label;

    public event EventHandler<string>? Submitted;

    public RenameSymbolView(string name)
        : base($"Rename {name}", 60, ContentRows)
    {
        HotKeySpecifier = VerbatimLabel.NoHotKey;
        OldName = name;
        _label = new VerbatimLabel { X = 1, Y = 0, Width = Dim.Fill(1), Text = "New name:" };
        _input = new PlaceholderField { X = 1, Y = 1, Width = Dim.Fill(1), Text = name };
        Add(_label, _input);
        Check(_input, _label, Refusal);

        Commands.Register(RenameId, "Rename", OnConfirm);
        Keys.Bind("Ctrl+Enter", RenameId);
        var rename = CommandButton(ButtonKind.Primary, RenameId);
        rename.IsDefault = false;
        AddButtonRow(ContentRows - 1, rename, CommandButton(ButtonKind.Secondary, CancelId));
    }

    public string OldName { get; }

    public string NewName => _input.Text?.Trim() ?? "";

    internal VerbatimLabel FieldLabel => _label;

    internal string SelectedText => _input.SelectedText ?? "";

    public bool FocusInput() => _input.SetFocus();

    /// <summary>Marks the name with why the server refused it, keeping the dialog open.</summary>
    public void ShowError(string message) => Refuse(_input, $"New name: {message}");

    /// <summary>Closes the dialog once the host has the rename.</summary>
    public void Finish() => Accept();

    private string? Refusal()
    {
        if (NewName.Length == 0) return "New name is needed";
        return NewName == OldName ? $"New name is still {OldName}" : null;
    }

    private void OnConfirm()
    {
        if (Checked()) Submitted?.Invoke(this, NewName);
    }
}
