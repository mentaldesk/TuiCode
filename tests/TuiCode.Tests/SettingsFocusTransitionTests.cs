using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

/// <summary>
/// Reproduces the user-reported focus problem: after opening the settings overlay and pressing
/// CursorRight on the categories list, focus should drill into the active panel. This test
/// drives the workbench via key injection and asserts on the focus tree at each step, so we
/// can iterate locally instead of asking the user to re-run the app.
/// </summary>
public class SettingsFocusTransitionTests
{
    [Fact]
    public async Task CursorRight_on_categories_list_focuses_the_active_panel()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings);

        SettingsView? overlay = null;
        var step = 0;
        string? failureReason = null;

        host.App.Iteration += OnIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.Null(failureReason);

        void OnIteration(object? sender, EventArgs<IApplication?> e)
        {
            step++;
            switch (step)
            {
                case 1:
                    InjectKey("Ctrl+,");
                    break;
                case 2:
                    overlay = workbench.SubViews.OfType<SettingsView>().FirstOrDefault();
                    if (overlay is null) { failureReason = "settings overlay didn't appear"; Quit(); return; }
                    InjectKey("CursorDown"); // move category to "Keyboard Shortcuts"
                    break;
                case 3:
                    InjectKey("CursorRight"); // expected to drill into the keybindings picker
                    break;
                case 4:
                    var picker = overlay!.SubViews.OfType<KeybindingsPickerView>().FirstOrDefault();
                    if (picker is null) { failureReason = "keybindings picker not in overlay subviews"; Quit(); return; }
                    if (!picker.Visible) { failureReason = "keybindings picker is not Visible after CursorDown selected the category"; Quit(); return; }
                    if (!HasFocusInside(picker)) { failureReason = $"focus did not move into the picker after CursorRight; tree = {DescribeFocus(workbench)}"; Quit(); return; }
                    Quit();
                    break;
            }
        }

        void InjectKey(string seq)
        {
            if (Key.TryParse(seq, out var k)) host.App.InjectKey(k);
        }
        void Quit()
        {
            if (Key.TryParse("Ctrl+Q", out var k)) host.App.InjectKey(k);
        }
    }

    private static bool HasFocusInside(View v)
    {
        if (v.HasFocus) return true;
        foreach (var c in v.SubViews)
            if (HasFocusInside(c)) return true;
        return false;
    }

    private static string DescribeFocus(View root)
    {
        var sb = new System.Text.StringBuilder();
        Walk(root, sb);
        return sb.ToString();
    }

    private static void Walk(View v, System.Text.StringBuilder sb)
    {
        if (v.HasFocus)
        {
            sb.Append(v.GetType().Name);
            sb.Append('(');
            sb.Append(string.IsNullOrEmpty(v.Title?.ToString()) ? (v.SuperView?.GetType().Name ?? "?") : v.Title.ToString());
            sb.Append(")>");
        }
        foreach (var c in v.SubViews) Walk(c, sb);
    }

    private static Workbench.Workbench BuildWorkbench() =>
        new(
            new SidebarPart(new FileExplorerView()),
            new EditorPart(),
            new StatusBarPart());
}
