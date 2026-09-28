using Terminal.Gui.ViewBase;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Editor;

namespace TuiCode.Tests;

public class EditorScrollBarTests
{
    private const int Height = 10;
    private const int Width = 40;

    [Fact]
    public void A_file_taller_than_the_tab_shows_the_vertical_bar()
    {
        using var tab = SizedTab(Lines(100));
        var bar = tab.TextView.VerticalScrollBar;

        Assert.True(bar.Visible);
        Assert.Equal(tab.TextView.GetContentSize().Height, bar.ScrollableContentSize);
        Assert.Equal(0, bar.Value);
    }

    [Fact]
    public void A_file_shorter_than_the_tab_shows_no_bar_and_keeps_its_width()
    {
        using var tab = SizedTab(Lines(3));
        using var plain = SizedTab(Lines(3), bar: false);

        Assert.False(tab.TextView.VerticalScrollBar.Visible);
        Assert.Equal(plain.TextView.Viewport.Width, tab.TextView.Viewport.Width);
    }

    [Fact]
    public void The_bar_takes_one_column_from_the_text_and_none_from_the_gutter()
    {
        using var tab = SizedTab(Lines(100));
        using var plain = SizedTab(Lines(100), bar: false);

        Assert.Equal(plain.TextView.Viewport.Width - 1, tab.TextView.Viewport.Width);
        Assert.Equal(plain.TextView.Frame.X, tab.TextView.Frame.X);
    }

    [Fact]
    public void Scrolling_the_text_moves_the_slider()
    {
        using var tab = SizedTab(Lines(100));

        tab.TextView.Viewport = tab.TextView.Viewport with { Y = 40 };

        Assert.Equal(40, tab.TextView.VerticalScrollBar.Value);
    }

    [Fact]
    public void Moving_the_slider_scrolls_the_text()
    {
        using var tab = SizedTab(Lines(100));

        tab.TextView.VerticalScrollBar.Value = 30;

        Assert.Equal(30, tab.TopRow);
    }

    [Fact]
    public void Resizing_so_the_file_fits_hides_the_bar_and_back()
    {
        using var tab = SizedTab(Lines(20));

        tab.Height = 30;
        tab.Layout();
        var hidden = !tab.TextView.VerticalScrollBar.Visible;
        tab.Height = Height;
        tab.Layout();

        Assert.True(hidden);
        Assert.True(tab.TextView.VerticalScrollBar.Visible);
    }

    private static string Lines(int count) =>
        string.Join('\n', Enumerable.Range(1, count).Select(i => $"line {i}"));

    private static EditorTab SizedTab(string content, bool bar = true)
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/file.txt", new MockFileData(content));
        var tab = new EditorTab(fs.FileInfo.New("/work/file.txt"));
        if (!bar) tab.TextView.ViewportSettings &= ~ViewportSettingsFlags.HasVerticalScrollBar;
        tab.Width = Width;
        tab.Height = Height;
        tab.BeginInit();
        tab.EndInit();
        tab.Layout();
        return tab;
    }
}

// Renders through a TG Application — serialised (#77).
public class EditorScrollBarHostTests : StaticConfigurationTest
{
    [Fact]
    public async Task A_tall_file_draws_the_bar_and_leaves_the_gutter_alone()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData(string.Join('\n', Enumerable.Range(1, 200).Select(i => $"line {i}"))));
        using var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        var commands = new CommandService();
        using var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
        var screen = "";

        await HostSteps.Run(host,
            () => { workbench.Editor.Open(fs.FileInfo.New("/work/a.txt")); },
            () => { screen = host.App.Driver!.ToString(); });

        Assert.Contains("  1  line 1", screen);
        Assert.Contains("▲", screen);
        Assert.Contains("▼", screen);
    }

    // #315: each resize is followed by an idle iteration and no key.
    [Fact]
    public async Task Shrinking_the_terminal_shows_the_vertical_bar_straight_away()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData(string.Join('\n', Enumerable.Range(1, 16).Select(i => $"line {i}"))));
        using var workbench = Workbench();
        using var host = Host(workbench);
        var fitted = true;
        var screen = "";

        await HostSteps.Run(host,
            Size(host, 80, 30),
            () => { workbench.Editor.Open(fs.FileInfo.New("/work/a.txt")); },
            () => { fitted = !Tab(workbench).TextView.VerticalScrollBar.Visible; },
            Size(host, 80, 10),
            () => { },
            () => { screen = host.App.Driver!.ToString(); });

        Assert.True(fitted);
        Assert.True(Tab(workbench).TextView.VerticalScrollBar.Visible);
        Assert.Contains("▼", screen);
    }

    [Fact]
    public async Task Narrowing_the_terminal_shows_the_sideways_bar_and_widening_it_takes_it_away()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/long.txt", new MockFileData(
            string.Join('\n', new[] { new string('x', 130) }.Concat(Enumerable.Range(1, 40).Select(i => $"line {i}")))));
        using var workbench = Workbench();
        using var host = Host(workbench);
        bool wideHidden = false, narrowShown = false, narrowGutterLevel = false;

        await HostSteps.Run(host,
            Size(host, 260, 20),
            () => { workbench.Editor.Open(fs.FileInfo.New("/work/long.txt")); },
            () => { wideHidden = !Tab(workbench).TextView.HorizontalScrollBar.Visible; },
            Size(host, 120, 20),
            () => { },
            () =>
            {
                var tab = Tab(workbench);
                narrowShown = tab.TextView.HorizontalScrollBar.Visible;
                narrowGutterLevel = GutterHeight(tab) == tab.TextView.Viewport.Height;
            },
            Size(host, 260, 20),
            () => { });

        var tab = Tab(workbench);
        Assert.True(wideHidden);
        Assert.True(narrowShown);
        Assert.True(narrowGutterLevel);
        Assert.False(tab.TextView.HorizontalScrollBar.Visible);
        Assert.Equal(tab.TextView.Frame.Height, tab.TextView.Viewport.Height);
        Assert.Equal(tab.TextView.Viewport.Height, GutterHeight(tab));
        Assert.Equal(tab.TextView.Viewport.Height, tab.TextView.VerticalScrollBar.Frame.Height);
    }

    [Fact]
    public async Task A_tab_in_the_background_during_a_resize_shows_its_bar_when_switched_to()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData(string.Join('\n', Enumerable.Range(1, 16).Select(i => $"line {i}"))));
        fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var workbench = Workbench();
        using var host = Host(workbench);
        EditorTab? a = null;
        var screen = "";

        await HostSteps.Run(host,
            Size(host, 80, 30),
            () => { workbench.Editor.Open(fs.FileInfo.New("/work/a.txt")); },
            () => { a = Tab(workbench); workbench.Editor.Open(fs.FileInfo.New("/work/b.txt")); },
            Size(host, 80, 10),
            () => { },
            () => { workbench.Editor.Open(fs.FileInfo.New("/work/a.txt")); },
            () => { },
            () => { screen = host.App.Driver!.ToString(); });

        Assert.Same(a, Tab(workbench));
        Assert.True(a!.TextView.VerticalScrollBar.Visible);
        Assert.Contains("▼", screen);
    }

    private static Action Size(WorkbenchHost host, int width, int height) =>
        () => host.App.Driver!.SetScreenSize(width, height);

    private static EditorTab Tab(Workbench.Workbench workbench) => workbench.Editor.Group.ActiveTab!;

    private static int GutterHeight(EditorTab tab) => tab.SubViews.First(v => v is not EditorTextView).Frame.Height;

    private static Workbench.Workbench Workbench() =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());

    private static WorkbenchHost Host(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
