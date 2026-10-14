using Terminal.Gui.Drivers;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Actions;
using TuiCode.Workbench.Find;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Alt + a menu letter from a text box (#553). Boots a TG Application — serialised (#77).
public class MenuLetterWhileTypingHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly InMemorySettingsService _settings = new();

    public MenuLetterWhileTypingHostTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one\n"));
    }

    // What iTerm2 sends for Option+F with Option on Normal: Alt+F with `ƒ` attached.
    private static Key Iterm2(string sequence) => new KittyKeyboardPattern().GetKey(sequence)!;

    private static Key OptionF => Iterm2("\u001b[102;3;402u");

    [Fact]
    public void Iterm2s_option_f_decodes_as_alt_f_carrying_its_character()
    {
        Assert.Equal(Key.F.WithAlt, OptionF);
        Assert.Equal("ƒ", OptionF.AssociatedText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Option_f_in_the_editor_opens_the_file_menu_unless_turned_off(bool on)
    {
        _settings.AltOpensMenuWhileTyping = on;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        var opened = false;

        await HostSteps.Run(host, [
            .. OpenFile(workbench),
            () => host.App.InjectKey(OptionF),
            () => { },
            () => { opened = FileMenuOpen(workbench); },
            () => host.App.InjectKey(Key.Esc),
        ]);

        Assert.Equal(on, opened);
        Assert.Equal(on ? "one\n" : "ƒone\n", workbench.Editor.Group.ActiveTab!.Content.ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Option_f_in_the_find_bar_opens_the_file_menu_unless_turned_off(bool on)
    {
        _settings.AltOpensMenuWhileTyping = on;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        FindBarView? bar = null;
        var opened = false;

        await HostSteps.Run(host, [
            .. OpenFile(workbench),
            () => commands.TryExecute(CommandIds.FindInFile),
            () => (bar = workbench.SubViewsDeep().OfType<FindBarView>().SingleOrDefault()) is { HasFocus: true },
            () => host.App.InjectKey(OptionF),
            () => { },
            () => { opened = FileMenuOpen(workbench); },
            () => host.App.InjectKey(Key.Esc),
        ]);

        Assert.Equal(on, opened);
        Assert.Equal(on ? "" : "ƒ", bar!.Query);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_bare_alt_f_in_the_editor_opens_the_file_menu_either_way(bool on)
    {
        _settings.AltOpensMenuWhileTyping = on;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);

        await HostSteps.Run(host, [
            .. OpenFile(workbench),
            () => host.App.InjectKey(Key.F.WithAlt),
            () => FileMenuOpen(workbench),
            () => host.App.InjectKey(Key.Esc),
            () => !workbench.MenuBar.IsOpen(),
        ]);

        Assert.Equal("one\n", workbench.Editor.Group.ActiveTab!.Content.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Option_on_a_key_that_is_no_menus_letter_still_types()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        var opened = true;

        await HostSteps.Run(host, [
            .. OpenFile(workbench),
            () => host.App.InjectKey(Iterm2("\u001b[112;3;960u")),
            () => { },
            () => { opened = workbench.MenuBar.IsOpen(); },
        ]);

        Assert.False(opened);
        Assert.Equal("πone\n", workbench.Editor.Group.ActiveTab!.Content.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Option_f_over_a_dialog_does_not_open_the_menu()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var opened = true;

        await HostSteps.Run(host, [
            .. OpenFile(workbench),
            () => commands.TryExecute(CommandIds.ShowActions),
            () => workbench.SubViews.OfType<ActionView>().Any(),
            () => host.App.InjectKey(OptionF),
            () => { },
            () => { opened = workbench.MenuBar.IsOpen(); },
            () => host.App.InjectKey(Key.Esc),
        ]);

        Assert.False(opened);
    }

    [Fact]
    public async Task Turning_it_off_in_settings_lets_option_f_type_again()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);

        await HostSteps.Run(host, [
            .. OpenFile(workbench),
            () => host.App.InjectKey(TestKeys.Chord("Ctrl+,")[0]),
            () => workbench.SubViews.OfType<Workbench.Settings.SettingsView>().Any(),
            () => host.App.InjectKey(Key.End),
            () => host.App.InjectKey(Key.CursorRight),
            () => host.App.InjectKey(Key.Tab),
            () => host.App.InjectKey(Key.Space),
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => !workbench.SubViews.OfType<Workbench.Settings.SettingsView>().Any()
                && workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(OptionF),
            () => { },
        ]);

        Assert.False(_settings.AltOpensMenuWhileTyping);
        Assert.False(workbench.MenuBar.IsOpen());
        Assert.Equal("ƒone\n", workbench.Editor.Group.ActiveTab!.Content.ReplaceLineEndings("\n"));
    }

    private IEnumerable<Func<bool>> OpenFile(Workbench.Workbench workbench)
    {
        yield return () => { workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")); return true; };
        yield return () => workbench.StatusBar.DisplayedFocus == "Editor";
    }

    private static bool FileMenuOpen(Workbench.Workbench workbench) =>
        workbench.MenuBar.SubViews.OfType<MenuBarItem>().Single(m => m.Title == "_File").PopoverMenuOpen;

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(), _settings,
            driverName: DriverRegistry.Names.ANSI);
    }
}
