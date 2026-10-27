using TuiCode.Abstractions;
using TuiCode.Workbench.Assistant;

namespace TuiCode.Workbench.Settings;

/// <summary>Settings › AI Assistance (#566): which agent the AI assistance tab runs, and whether it shows tool calls in full (#618). <see cref="SettingsView"/> saves <see cref="Current"/>.</summary>
public sealed class AssistantSettingsView : View
{
    private const int RowColumn = 17;

    private readonly AssistantForm _form;
    private readonly OptionSelector _agents;
    private readonly TextField _command;
    private readonly CheckBox _inFull;
    private readonly VerbatimLabel _status;

    public AssistantSettingsView(AssistantSetting current, Func<string, bool> isInstalled)
    {
        _form = new AssistantForm(current, isInstalled);
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        var caption = new VerbatimLabel { X = 0, Y = 0, Text = "The coding agent the AI assistance tab asks:" };
        _agents = new OptionSelector { X = 0, Y = 2, Labels = [.. _form.Options], Value = _form.Selected };
        _agents.ValueChanged += (_, _) =>
        {
            if (_agents.Value is not { } option) return;
            _form.Selected = option;
            ShowStatus();
        };
        Add(caption, _agents);
        for (var i = 0; i < _form.Custom; i++)
            Add(new VerbatimLabel { X = RowColumn, Y = 2 + i, Width = Dim.Fill(), Text = _form.Row(i) });
        _command = new TextField { X = RowColumn, Y = 2 + _form.Custom, Width = Dim.Fill(1), Text = _form.Command };
        _command.TextChanged += (_, _) =>
        {
            _form.Command = _command.Text ?? "";
            if (_form.Command.Length > 0 && _agents.Value != _form.Custom) _agents.Value = _form.Custom;
            ShowStatus();
        };
        _inFull = new CheckBox
        {
            X = 0,
            Y = 3 + _form.Options.Count,
            Text = "Show tool calls in full",
            Value = _form.ToolCallsInFull ? CheckState.Checked : CheckState.UnChecked,
        };
        _inFull.ValueChanged += (_, _) => _form.ToolCallsInFull = _inFull.Value == CheckState.Checked;
        _status = new VerbatimLabel { X = 0, Y = 5 + _form.Options.Count, Width = Dim.Fill(), Height = 4 };
        Add(_command, _inFull, _status);
        KeyDown += OnKey;
        ShowStatus();
    }

    public AssistantSetting Current => _form.Setting;

    public bool FocusContent() => _agents.SetFocus();

    private void ShowStatus() => _status.Text = string.Join('\n', _form.Status());

    private void OnKey(object? sender, Key key)
    {
        if (key == Key.CursorLeft && _agents.HasFocus && SuperView is SettingsView settings)
        {
            settings.FocusCategories();
            key.Handled = true;
        }
    }
}
