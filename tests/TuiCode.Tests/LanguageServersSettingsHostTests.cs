using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// Settings › Language Servers (#456) driven with keys. Boots a TG Application — serialised (#77).
public class LanguageServersSettingsHostTests : StaticConfigurationTest
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();
    private readonly FakeLanguageServer _server = new();
    private readonly InMemorySettingsService _settings = new();

    public LanguageServersSettingsHostTests()
    {
        _fs.AddFile("/work/main.go", new MockFileData("package main\n"));
    }

    [Fact]
    public async Task Choosing_gopls_for_Go_saves_it_and_starts_it_for_the_open_Go_file()
    {
        using var workbench = BuildWorkbench();
        var host = BuildHost(workbench);
        var dialogTitle = "";
        var status = "";
        var row = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/main.go")),
            OpenPane(host, workbench),
            SelectRow(host, workbench, "go"),
            () => host.App.InjectKey(Key.Enter),
            () => Pane(workbench).Dialog is not null,
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.Space),
            () => { dialogTitle = Pane(workbench).Dialog!.Title; status = Pane(workbench).Dialog!.Form.Status()[0]; },
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => Pane(workbench).Dialog is null,
            () => { row = Row(workbench, "go").Server; },
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => _server.Launches.Count > 0);

        Assert.Equal("Go language server", dialogTitle);
        Assert.Equal("✗ gopls isn't on PATH.", status);
        Assert.Equal("gopls", row);
        Assert.Equal(new LanguageServerSetting("gopls", []), _settings.LanguageServers["go"]);
        Assert.Equal(1, _settings.SaveCount);
        Assert.Equal("gopls", _server.Launches.Single().Command);
    }

    [Fact]
    public async Task Esc_leaves_the_row_as_it_was_and_Delete_resets_it_to_its_default()
    {
        _settings.LanguageServers = new Dictionary<string, LanguageServerSetting> { ["csharp"] = LanguageServerSetting.None };
        using var workbench = BuildWorkbench();
        var host = BuildHost(workbench);
        var afterEsc = "";
        var afterDelete = "";

        await HostSteps.Run(host,
            OpenPane(host, workbench),
            SelectRow(host, workbench, "csharp"),
            () => host.App.InjectKey(Key.Enter),
            () => Pane(workbench).Dialog is not null,
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.Space),
            () => host.App.InjectKey(Key.Esc),
            () => Pane(workbench).Dialog is null,
            () => { afterEsc = Row(workbench, "csharp").Server; host.App.InjectKey(Key.Delete); },
            () => { afterDelete = Row(workbench, "csharp").Server; host.App.InjectKey(Key.Enter.WithCtrl); },
            () => _settings.SaveCount == 1);

        Assert.Equal(LanguageServerRows.NoServer, afterEsc);
        Assert.Equal("csharp-ls (default)", afterDelete);
        Assert.Empty(_settings.LanguageServers);
    }

    private static Func<bool> OpenPane(WorkbenchHost host, Workbench.Workbench workbench)
    {
        var step = 0;
        return () =>
        {
            if (step++ == 0)
            {
                host.App.InjectKey(TestKeys.Chord("Ctrl+,")[0]);
                return false;
            }
            var settings = workbench.SubViews.OfType<SettingsView>().SingleOrDefault();
            if (settings is null) return false;
            if (!Pane(workbench).Visible)
            {
                host.App.InjectKey(Key.CursorDown);
                return false;
            }
            host.App.InjectKey(Key.CursorRight);
            return true;
        };
    }

    private static Func<bool> SelectRow(WorkbenchHost host, Workbench.Workbench workbench, string languageId)
    {
        var pressed = -1;
        return () =>
        {
            var index = Pane(workbench).Rows.ToList().FindIndex(r => r.Language.Id == languageId);
            if (pressed++ < index)
            {
                if (pressed > 0) host.App.InjectKey(Key.CursorDown);
                return false;
            }
            return true;
        };
    }

    private static LanguageServersView Pane(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<SettingsView>().Single().SubViews.OfType<LanguageServersView>().Single();

    private static LanguageServerRow Row(Workbench.Workbench workbench, string languageId) =>
        Pane(workbench).Rows.Single(r => r.Language.Id == languageId);

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(Syntax), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            _settings, driverName: DriverRegistry.Names.ANSI, languageServers: _server);
        HostSteps.PinScreenSize(host, 100, 30);
        return host;
    }
}
