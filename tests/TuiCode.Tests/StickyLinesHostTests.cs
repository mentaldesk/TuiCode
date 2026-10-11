using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// Sticky lines (#474) through the host. Boots a TG Application — serialised (#77).
public class StickyLinesHostTests : StaticConfigurationTest
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private static readonly string Code = string.Join('\n',
    [
        "public sealed class Widget",
        "{",
        "    public void Run()",
        "    {",
        .. Enumerable.Range(0, 60).Select(i => $"        Step({i});"),
        "    }",
        "}",
    ]);

    private readonly MockFileSystem _fs = new();

    public StickyLinesHostTests() => _fs.AddFile("/work/Widget.cs", new MockFileData(Code));

    [Fact]
    public async Task Clicking_a_pinned_line_goes_there_and_Back_returns()
    {
        using var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(Syntax), new StatusBarPart());
        var commands = new CommandService();
        using var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
        EditorTab Tab() => workbench.Editor.Group.ActiveTab!;
        var landed = -1;

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 100, 40),
            () => { workbench.OpenFile(_fs.FileInfo.New("/work/Widget.cs")); },
            () =>
            {
                for (var i = 0; i < 40; i++) host.App.InjectKey(Key.CursorDown);
            },
            () => Tab().PinnedLines.Count == 2,
            () =>
            {
                foreach (var flags in new[] { MouseFlags.LeftButtonPressed, MouseFlags.LeftButtonReleased, MouseFlags.LeftButtonClicked })
                    Tab().TextView.NewMouseEvent(new Mouse { Flags = flags, Position = new Point(5, 1) });
                landed = Tab().CursorRow;
            },
            () => { commands.TryExecute(CommandIds.NavigateBack); });

        Assert.Equal(2, landed);
        Assert.Equal(40, Tab().CursorRow);
    }
}
