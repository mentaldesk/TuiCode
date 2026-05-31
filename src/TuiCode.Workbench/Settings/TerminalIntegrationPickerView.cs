using TuiCode.Abstractions;

namespace TuiCode.Workbench.Settings;

/// <summary>
/// Right-pane panel for the "Terminal Integration" category. Surfaces the integration that
/// matches the host's current terminal (if any) — Install / Reinstall / Remove buttons act
/// directly on <see cref="ITerminalIntegration"/>, which writes to the user's terminal config
/// immediately. No staging via <see cref="ISettingsService.Save"/>; the operation isn't a
/// TuiCode setting, it's a one-shot file write to an external app's config.
/// </summary>
/// <remarks>
/// Per <see href="https://github.com/mentaldesk/TuiCode/issues/59">#59</see> we only show the
/// integration for the *detected* terminal. When detection fails the panel still renders — it
/// shows the env vars we read so the user can see why nothing matched.
/// </remarks>
public sealed class TerminalIntegrationPickerView : View
{
    private readonly IReadOnlyList<ITerminalIntegration> _integrations;
    private readonly IEnvironment _environment;

    public TerminalIntegrationPickerView(
        IEnumerable<ITerminalIntegration> integrations, IEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(integrations);
        ArgumentNullException.ThrowIfNull(environment);
        _integrations = integrations.ToArray();
        _environment = environment;

        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        // Required: TG only allows focus on a descendant if every ancestor has CanFocus = true.
        CanFocus = true;

        Render();

        KeyDown += OnKey;
    }

    private void OnKey(object? sender, Key key)
    {
        if (key == Key.CursorLeft && SuperView is SettingsView settings)
        {
            settings.FocusCategories();
            key.Handled = true;
        }
    }

    /// <summary>
    /// Move focus to the first interactive element in the panel. Returns true if anything was
    /// focused — false when there are no buttons (unsupported-terminal case), which keeps focus
    /// on the categories list so the user isn't stranded on an inert panel.
    /// </summary>
    public bool FocusContent()
    {
        foreach (var sub in SubViews)
        {
            if (sub is Button button)
                return button.SetFocus();
        }
        return false;
    }

    private void Render()
    {
        foreach (var sub in SubViews.ToArray())
        {
            Remove(sub);
            sub.Dispose();
        }

        var state = TerminalIntegrationPanelState.Build(_integrations, _environment);

        int row = 0;
        foreach (var line in state.Lines)
        {
            Add(new Label { X = 0, Y = row, Text = line });
            row++;
        }

        if (state.Actions.Count == 0 || state.Detected is null)
            return;

        row++;
        int col = 0;
        foreach (var action in state.Actions)
        {
            var captured = action;
            var button = new Button { X = col, Y = row, Text = $"[ {captured.Label()} ]" };
            button.Accepting += (_, _) => Invoke(state.Detected, captured);
            Add(button);
            col += button.Text.Length + 4;
        }
    }

    private void Invoke(ITerminalIntegration integration, TerminalIntegrationAction action)
    {
        switch (action)
        {
            case TerminalIntegrationAction.Install:
            case TerminalIntegrationAction.Reinstall:
            case TerminalIntegrationAction.Update:
                integration.Install();
                break;
            case TerminalIntegrationAction.Remove:
                integration.Uninstall();
                break;
        }
        Render();
        FocusContent();
    }
}
