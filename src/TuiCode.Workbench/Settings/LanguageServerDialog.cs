using TuiCode.Abstractions;
using TuiCode.Syntax;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Settings;

/// <summary>Chooses one language's server (#456). The owner pushes <see cref="Scope"/> and removes it on <see cref="Closed"/>.</summary>
public sealed class LanguageServerDialog : Window
{
    private const int FieldColumn = 12;

    private readonly LanguageServerForm _form;
    private readonly OptionSelector _server;
    private readonly TextField _command;
    private readonly TextField _arguments;
    private readonly Label _status;
    private readonly ICommandService _scopeCommands = new CommandService();
    private bool _filling;

    public IKeybindingService Scope { get; }

    /// <summary>Raised with the server to save.</summary>
    public event EventHandler<LanguageServerSetting>? Saved;

    /// <summary>Raised when the dialog should be removed: after a save, or on Esc.</summary>
    public event EventHandler? Closed;

    internal LanguageServerDialog(SyntaxLanguage language, LanguageServerSetting current, Func<string, bool> isInstalled)
    {
        _form = new LanguageServerForm(language.Id, current, isInstalled);

        Title = $"{language.Name} language server";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 64;
        Height = _form.Options.Count + 10;
        CanFocus = true;

        var serverLabel = new Label { X = 1, Y = 0, Text = "Server:" };
        _server = new OptionSelector
        {
            X = FieldColumn,
            Y = 0,
            Labels = [.. _form.Options],
            Value = _form.Selected,
        };
        _server.ValueChanged += (_, _) => OnServerChanged();

        var fieldsTop = _form.Options.Count + 1;
        var commandLabel = new Label { X = 1, Y = fieldsTop, Text = "Command:" };
        _command = new TextField { X = FieldColumn, Y = fieldsTop, Width = Dim.Fill(1), Text = _form.Command };
        var argumentsLabel = new Label { X = 1, Y = fieldsTop + 1, Text = "Arguments:" };
        _arguments = new TextField { X = FieldColumn, Y = fieldsTop + 1, Width = Dim.Fill(1), Text = _form.Arguments };
        _command.TextChanged += (_, _) => OnFieldsChanged();
        _arguments.TextChanged += (_, _) => OnFieldsChanged();

        _status = new Label { X = 1, Y = fieldsTop + 3, Width = Dim.Fill(1), Height = 2 };
        var hint = new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Ctrl+Enter: Save   Esc: Cancel" };

        Add(serverLabel, _server, commandLabel, _command, argumentsLabel, _arguments, _status, hint);

        Scope = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.LanguageServerSave, Save);
        _scopeCommands.Register(CommandIds.LanguageServerCancel, () => Closed?.Invoke(this, EventArgs.Empty));
        Scope.Bind("Ctrl+Enter", CommandIds.LanguageServerSave);
        Scope.Bind("Esc", CommandIds.LanguageServerCancel);

        ShowStatus();
    }

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

    private void Save()
    {
        Saved?.Invoke(this, _form.Setting);
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
