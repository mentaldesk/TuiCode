using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Fields;
using TuiCode.Abstractions;
using TuiCode.Syntax;

namespace TuiCode.Workbench.Settings;

/// <summary>Chooses one language's server (#456).</summary>
public sealed class LanguageServerDialog : AppDialog
{
    private const string SaveId = "languageServer.save";
    private const int FieldColumn = 12;

    private readonly LanguageServerForm _form;
    private readonly OptionSelector _server;
    private readonly TextField _command;
    private readonly TextField _arguments;
    private readonly VerbatimLabel _status;
    private bool _filling;

    internal LanguageServerDialog(SyntaxLanguage language, LanguageServerSetting current, Func<string, bool> isInstalled)
        : this(new LanguageServerForm(language.Id, current, isInstalled), language.Name)
    {
    }

    private LanguageServerDialog(LanguageServerForm form, string languageName)
        : base($"{languageName} language server", 64, form.Options.Count + 8)
    {
        _form = form;

        var serverLabel = new VerbatimLabel { X = 1, Y = 0, Text = "Server:" };
        _server = new OptionSelector
        {
            X = FieldColumn,
            Y = 0,
            Labels = [.. _form.Options],
            Value = _form.Selected,
        }.SelectOnArrows();
        _server.ValueChanged += (_, _) => OnServerChanged();

        var fieldsTop = _form.Options.Count + 1;
        var commandLabel = new VerbatimLabel { X = 1, Y = fieldsTop, Text = "Command:" };
        _command = new TextField { X = FieldColumn, Y = fieldsTop, Width = Dim.Fill(1), Text = _form.Command };
        var argumentsLabel = new VerbatimLabel { X = 1, Y = fieldsTop + 1, Text = "Arguments:" };
        _arguments = new TextField { X = FieldColumn, Y = fieldsTop + 1, Width = Dim.Fill(1), Text = _form.Arguments };
        _command.TextChanged += (_, _) => OnFieldsChanged();
        _arguments.TextChanged += (_, _) => OnFieldsChanged();

        _status = new VerbatimLabel { X = 1, Y = fieldsTop + 3, Width = Dim.Fill(1), Height = 2 };

        Add(serverLabel, _server, commandLabel, _command, argumentsLabel, _arguments, _status);

        Commands.Register(SaveId, "Save", Accept);
        Keys.Bind("Ctrl+Enter", SaveId);
        AddButtonRow(fieldsTop + 6, CommandButton(ButtonKind.Primary, SaveId), CommandButton(ButtonKind.Secondary, CancelId));

        ShowStatus();
    }

    /// <summary>The server to save, once <see cref="AppDialog.Confirmed"/>.</summary>
    public LanguageServerSetting Setting => _form.Setting;

    internal LanguageServerForm Form => _form;

    public bool FocusServer() => _server.SetFocus();

    private void OnServerChanged()
    {
        if (_filling || _server.Value is not { } option || option == _form.Selected) return;
        _form.Select(option);
        _filling = true;
        _command.Text = _form.Command;
        _arguments.Text = _form.Arguments;
        _filling = false;
        ShowStatus();
    }

    private void OnFieldsChanged()
    {
        if (_filling) return;
        _form.Edit(_command.Text ?? "", _arguments.Text ?? "");
        _filling = true;
        _server.Value = _form.Selected;
        _filling = false;
        ShowStatus();
    }

    private void ShowStatus() => _status.Text = string.Join('\n', _form.Status());
}
