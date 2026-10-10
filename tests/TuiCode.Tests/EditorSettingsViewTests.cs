using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// Boots a TG Application — serialised (#77).
public class EditorSettingsViewTests : StaticConfigurationTest
{
    private readonly InMemorySettingsService _settings = new();

    [Fact]
    public async Task Changed_settings_are_saved_and_applied_to_the_editor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, OpenEditorSettings(host, workbench),
            () => host.App.InjectKey(Key.CursorUp),
            () => host.App.InjectKey(Key.Tab),
            () => host.App.InjectKey(Key.Space),
            () => host.App.InjectKey(Key.Tab),
            () => host.App.InjectKey(Key.CursorRight),
            () => host.App.InjectKey(Key.Space),
            () => host.App.InjectKey(Key.Tab),
            () => host.App.InjectKey(Key.Space),
            () => host.App.InjectKey(Key.Tab),
            () => host.App.InjectKey(Key.Space),
            () => host.App.InjectKey(Key.Enter.WithCtrl));

        var expected = new EditorSettings
        {
            IndentSize = 5,
            InsertSpaces = false,
            LineEnding = LineEnding.LF,
            InsertFinalNewline = false,
            WordWrap = true,
        };
        Assert.Equal(expected, _settings.Editor);
        Assert.Equal(expected, workbench.Editor.Group.Settings);
        Assert.Equal(1, _settings.SaveCount);
    }

    [Fact]
    public async Task Wrap_long_lines_shows_the_saved_value_and_unchecking_it_saves_it()
    {
        _settings.Editor = EditorSettings.Default with { WordWrap = true };
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var shown = CheckState.UnChecked;

        await HostSteps.Run(host, OpenEditorSettings(host, workbench),
            () => { shown = WrapCheckBox(workbench).Value; },
            () => WrapCheckBox(workbench).SetFocus(),
            () => host.App.InjectKey(Key.Space),
            () => host.App.InjectKey(Key.Enter.WithCtrl));

        Assert.Equal(CheckState.Checked, shown);
        Assert.False(_settings.Editor.WordWrap);
    }

    [Fact]
    public async Task Show_sticky_lines_is_ticked_by_default_and_unticking_it_saves_it_to_open_tabs()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.cs", new MockFileData("class A { }\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var shown = CheckState.UnChecked;
        EditorTab? open = null;

        await HostSteps.Run(host,
            () => { open = workbench.Editor.Open(fs.FileInfo.New("/work/a.cs")); },
            OpenEditorSettings(host, workbench),
            () => { shown = StickyCheckBox(workbench).Value; },
            () => StickyCheckBox(workbench).SetFocus(),
            () => host.App.InjectKey(Key.Space),
            () => host.App.InjectKey(Key.Enter.WithCtrl));

        Assert.Equal(CheckState.Checked, shown);
        Assert.False(_settings.Editor.StickyLines);
        Assert.False(open!.Settings.StickyLines);
    }

    [Fact]
    public async Task Saving_wrap_long_lines_wraps_files_opened_afterwards_but_not_open_ones()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        fs.AddFile("/work/b.txt", new MockFileData("bravo\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        EditorTab? open = null, later = null;

        await HostSteps.Run(host,
            () => { open = workbench.Editor.Open(fs.FileInfo.New("/work/a.txt")); },
            OpenEditorSettings(host, workbench),
            () => WrapCheckBox(workbench).SetFocus(),
            () => host.App.InjectKey(Key.Space),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => { later = workbench.Editor.Open(fs.FileInfo.New("/work/b.txt")); });

        Assert.True(_settings.Editor.WordWrap);
        Assert.False(open!.WordWrap);
        Assert.True(later!.WordWrap);
    }

    [Theory]
    [InlineData(EditorSettings.MaxIndentSize, "CursorUp")]
    [InlineData(EditorSettings.MinIndentSize, "CursorDown")]
    public async Task Indent_size_stays_within_its_limits(int indentSize, string key)
    {
        _settings.Editor = EditorSettings.Default with { IndentSize = indentSize };
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, OpenEditorSettings(host, workbench),
            () => host.App.InjectKey(TestKeys.Chord(key)[0]),
            () => host.App.InjectKey(Key.Enter.WithCtrl));

        Assert.Equal(indentSize, _settings.Editor.IndentSize);
    }

    [Fact]
    public async Task Cancelling_keeps_the_original_settings()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, OpenEditorSettings(host, workbench),
            () => host.App.InjectKey(Key.CursorUp),
            () => host.App.InjectKey(Key.Esc));

        Assert.Empty(workbench.SubViews.OfType<SettingsView>());
        Assert.Equal(EditorSettings.Default, _settings.Editor);
        Assert.Equal(EditorSettings.Default, workbench.Editor.Group.Settings);
    }

    [Fact]
    public async Task Left_returns_to_the_categories()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var panelFocused = true;

        await HostSteps.Run(host, OpenEditorSettings(host, workbench),
            () => host.App.InjectKey(Key.Tab),
            () => host.App.InjectKey(Key.CursorLeft),
            () => { panelFocused = workbench.SubViews.OfType<SettingsView>().Single()
                .SubViews.OfType<EditorSettingsView>().Single().HasFocus; },
            () => host.App.InjectKey(Key.Esc));

        Assert.False(panelFocused);
    }

    [Fact]
    public void Saved_settings_apply_to_the_editor_at_startup()
    {
        _settings.Editor = EditorSettings.Default with { IndentSize = 2 };
        using var workbench = BuildWorkbench();

        using var host = BuildHost(workbench);

        Assert.Equal(2, workbench.Editor.Group.Settings.IndentSize);
    }

    // Opens Settings and moves from the categories into the Editor panel, the second category without file icons.
    private static Func<bool> OpenEditorSettings(WorkbenchHost host, Workbench.Workbench workbench)
    {
        var step = 0;
        return () =>
        {
            switch (step++)
            {
                case 0: host.App.InjectKey(TestKeys.Chord("Ctrl+,")[0]); return false;
                case 1: host.App.InjectKey(Key.CursorDown); return false;
                case 2: host.App.InjectKey(Key.CursorRight); return false;
                default:
                    return workbench.SubViews.OfType<SettingsView>().Single()
                        .SubViews.OfType<EditorSettingsView>().Single() is { Visible: true } panel
                        && panel.SubViews.Any(v => v.HasFocus);
            }
        };
    }

    private static CheckBox StickyCheckBox(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<SettingsView>().Single().SubViews.OfType<EditorSettingsView>().Single()
            .SubViews.OfType<CheckBox>().Single(c => c.Text == "Show sticky lines");

    private static CheckBox WrapCheckBox(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<SettingsView>().Single().SubViews.OfType<EditorSettingsView>().Single()
            .SubViews.OfType<CheckBox>().Single(c => c.Text == "Wrap long lines");

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(), _settings,
            driverName: DriverRegistry.Names.ANSI);
    }

    private static Workbench.Workbench BuildWorkbench() =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
}
