using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Type-to-filter in the Ctrl+O dialog (#109). Boots a TG Application — serialised (#77).
public class OpenViewHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();

    [Fact]
    public async Task Typing_filters_across_subfolders_and_Enter_opens_the_selected_match()
    {
        _fs.AddFile("/work/readme.md", new MockFileData(""));
        _fs.AddFile("/work/src/Navigation/OpenView.cs", new MockFileData(""));
        _fs.AddFile("/work/src/Navigation/GoToLineView.cs", new MockFileData(""));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        OpenView? view = null;

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.O.WithCtrl),
            () =>
            {
                view = workbench.SubViews.OfType<OpenView>().Single();
                foreach (var c in "ox") host.App.InjectKey(new Key(c));
                host.App.InjectKey(Key.Backspace);
                host.App.InjectKey(new Key('v'));
            },
            () => view!.VisibleItems.SequenceEqual(["src/Navigation/OpenView.cs"]),
            () => host.App.InjectKey(Key.Enter));

        Assert.Equal("ov", view!.Filter);
        Assert.Empty(workbench.SubViews.OfType<OpenView>());
        Assert.Equal("/work/src/Navigation/OpenView.cs", workbench.Editor.Group.ActiveTab?.File.FullName);
    }

    [Fact]
    public async Task Esc_clears_the_filter_back_to_the_listing_then_closes_the_dialog()
    {
        _fs.AddFile("/work/readme.md", new MockFileData(""));
        _fs.AddFile("/work/src/Program.cs", new MockFileData(""));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        OpenView? view = null;
        IReadOnlyList<string> afterFirstEsc = [];
        var openAfterFirstEsc = false;

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.O.WithCtrl),
            () =>
            {
                view = workbench.SubViews.OfType<OpenView>().Single();
                host.App.InjectKey(new Key('p'));
            },
            () => view!.VisibleItems.SequenceEqual(["src/Program.cs"]),
            () => host.App.InjectKey(Key.Esc),
            () =>
            {
                afterFirstEsc = view!.VisibleItems;
                openAfterFirstEsc = workbench.SubViews.OfType<OpenView>().Any();
                host.App.InjectKey(Key.Esc);
            });

        Assert.True(openAfterFirstEsc, "the first Esc should only clear the filter");
        Assert.Equal(["../", "src/", "readme.md"], afterFirstEsc);
        Assert.Empty(workbench.SubViews.OfType<OpenView>());
    }

    [Fact]
    public async Task Enter_on_a_filtered_folder_browses_into_it_with_the_filter_cleared()
    {
        _fs.AddFile("/work/src/Navigation/OpenView.cs", new MockFileData(""));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        OpenView? view = null;

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.O.WithCtrl),
            () =>
            {
                view = workbench.SubViews.OfType<OpenView>().Single();
                foreach (var c in "nav") host.App.InjectKey(new Key(c));
            },
            () => view!.VisibleItems.SequenceEqual(["src/Navigation/"]),
            () => host.App.InjectKey(Key.Enter),
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal("", view!.Filter);
        Assert.Equal(["../", "OpenView.cs"], view.VisibleItems);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
