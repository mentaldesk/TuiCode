using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// A jump into a file whose pins aren't known yet (#588). Boots a TG Application — serialised (#77).
public class StickyJumpHostTests : StaticConfigurationTest
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private const int Target = 40;
    private const int Pinned = 2;

    private static readonly string[] Lines =
    [
        "public sealed class Widget",
        "{",
        "    public void Run()",
        "    {",
        .. Enumerable.Range(0, 100).Select(i => $"        Step({i});"),
        "    }",
        "}",
    ];

    private readonly MockFileSystem _fs = new();

    private IFileInfo File => _fs.FileInfo.New("/work/Widget.cs");

    public StickyJumpHostTests() => _fs.AddFile("/work/Widget.cs", new MockFileData(string.Join('\n', Lines)));

    [Fact]
    public async Task A_command_line_line_lands_two_rows_below_the_pins_once_they_are_known()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 100, 40),
            () => host.OpenWhenRunning(new StartupTarget(_fs.DirectoryInfo.New("/work"), [new StartupFile(File, new FilePosition(Target + 1, 1))])),
            () => Scanning(workbench),
            () => Hurry(workbench),
            () => PinsKnown(workbench));

        Assert.Equal(Target, Tab(workbench)!.CursorRow);
        Assert.Equal(Target - Pinned - Reveal.Margin, Tab(workbench)!.TopRow);
    }

    [Fact]
    public async Task A_find_result_in_a_file_that_is_not_open_lands_two_rows_below_the_pins()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        host.OpenWhenRunning(new StartupTarget(_fs.DirectoryInfo.New("/work"), []));
        var search = workbench.Sidebar.Search;

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 100, 40),
            () => host.App.InjectKey(Key.F.WithCtrl.WithShift),
            () => { foreach (var c in $"Step({Target - 4})") host.App.InjectKey(new Key(c)); },
            () => search.Result.MatchCount == 1,
            () => host.App.InjectKey(Key.Enter),
            () => host.App.InjectKey(Key.Enter),
            () => Scanning(workbench),
            () => Hurry(workbench),
            () => PinsKnown(workbench));

        Assert.Equal(Target, Tab(workbench)!.CursorRow);
        Assert.Equal(Target - Pinned - Reveal.Margin, Tab(workbench)!.TopRow);
    }

    [Fact]
    public async Task With_word_wrap_the_line_still_lands_two_rows_below_the_pins()
    {
        var lines = Lines.ToArray();
        lines[Target - 3] = "        // " + string.Concat(Enumerable.Repeat("word ", 16));
        _fs.AddFile("/work/Widget.cs", new MockFileData(string.Join('\n', lines)));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, new InMemorySettingsService { Editor = EditorSettings.Default with { WordWrap = true } });

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 100, 40),
            () => host.OpenWhenRunning(new StartupTarget(_fs.DirectoryInfo.New("/work"), [new StartupFile(File, new FilePosition(Target + 1, 1))])),
            () => Scanning(workbench),
            () => Hurry(workbench),
            () => PinsKnown(workbench));

        Assert.True(Tab(workbench)!.WordWrap);
        // The long line above takes two rows, so the target is a row further down the screen than its number.
        Assert.Equal(Target + 1 - Pinned - Reveal.Margin, Tab(workbench)!.TopRow);
    }

    public static TheoryData<string> Moves => ["cursor", "type", "scroll"];

    [Theory]
    [MemberData(nameof(Moves))]
    public async Task Moving_before_the_pins_are_known_keeps_the_view_where_it_is(string move)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var top = -1;

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 100, 40),
            () => host.OpenWhenRunning(new StartupTarget(_fs.DirectoryInfo.New("/work"), [new StartupFile(File, new FilePosition(Target + 1, 1))])),
            () => Scanning(workbench),
            () =>
            {
                var tab = Tab(workbench)!;
                switch (move)
                {
                    case "cursor": host.App.InjectKey(Key.CursorDown); break;
                    case "type": host.App.InjectKey(Key.Space); break;
                    default: tab.Code.Viewport = tab.Code.Viewport with { Y = tab.Code.Viewport.Y - 1 }; break;
                }
            },
            () =>
            {
                top = Tab(workbench)!.TopRow;
                Hurry(workbench);
            },
            () => PinsKnown(workbench));

        Assert.Equal(top, Tab(workbench)!.TopRow);
    }

    [Fact]
    public async Task An_open_at_a_remembered_position_does_not_scroll_when_the_pins_arrive()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        host.OpenWhenRunning(new StartupTarget(_fs.DirectoryInfo.New("/work"), []));

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 100, 40),
            () =>
            {
                workbench.OpenFile(File);
                Tab(workbench)!.RestoreViewPosition(new ViewPosition(Target + 5, 0, Target - 2, 0));
            },
            () => Scanning(workbench),
            () => Hurry(workbench),
            () => PinsKnown(workbench));

        Assert.Equal(Target - 2, Tab(workbench)!.TopRow);
    }

    private static EditorTab? Tab(Workbench.Workbench workbench) => workbench.Editor.Group.ActiveTab;

    private static bool Scanning(Workbench.Workbench workbench) => Tab(workbench)?.StickyScan is { Done: false };

    private static void Hurry(Workbench.Workbench workbench) => Tab(workbench)!.StickyBudget = TimeSpan.FromSeconds(1);

    private static bool PinsKnown(Workbench.Workbench workbench) =>
        Tab(workbench) is { StickyScan.Done: true, PinnedLines.Count: Pinned };

    // One line a frame, so the pins aren't known until the test hurries the scan along.
    private static Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(Syntax), new StatusBarPart());
        workbench.Editor.Groups.EditorOpening += (_, tab) => tab.StickyBudget = TimeSpan.Zero;
        return workbench;
    }

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench, ISettingsService? settings = null)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            settings ?? new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
