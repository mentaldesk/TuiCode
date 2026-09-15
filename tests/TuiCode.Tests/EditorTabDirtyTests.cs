using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Boots a TG Application — serialised (#77).
public class EditorTabDirtyTests : StaticConfigurationTest
{
    private static readonly string[] Original = ["alpha beta", "gamma delta", ""];

    private readonly MockFileSystem _fs = new();

    // TG 2.1.0's kill commands edit without raising ContentsChanged. Expected lines are '|'-separated;
    // exact, so a command that ran twice would fail too.
    [Theory]
    [InlineData("Ctrl+K", 0, 6, "alpha |gamma delta|")]                          // CutToEndOfLine
    [InlineData("Ctrl+K", 0, 10, "alpha betagamma delta|")]                      // CutToEndOfLine, joining
    [InlineData("Ctrl+Delete", 0, 0, "beta|gamma delta|")]                       // KillWordRight
    [InlineData("Ctrl+Delete", 0, 10, "alpha betagamma delta|")]                 // KillWordRight, joining
    [InlineData("Ctrl+Backspace", 0, 10, "alpha |gamma delta|")]                 // KillWordLeft
    [InlineData("Ctrl+Backspace", 1, 0, "alpha betagamma delta|")]               // KillWordLeft, joining
    [InlineData("Ctrl+Shift+Backspace", 0, 6, "beta|gamma delta|")]              // CutToStartOfLine
    [InlineData("Ctrl+Shift+Backspace", 1, 0, "alpha betagamma delta|")]         // CutToStartOfLine, joining
    [InlineData("Ctrl+Shift+Delete", 0, 0, "")]                                  // DeleteAll
    [InlineData("Backspace", 0, 1, "lpha beta|gamma delta|")]
    [InlineData("Delete", 0, 0, "lpha beta|gamma delta|")]
    public async Task Deleting_keys_mark_the_tab_dirty_and_raise_ContentChanged(string key, int row, int col, string expected)
    {
        var (lines, dirty, changes) = await PressInEditor(key, row, col);

        Assert.Equal(expected.Split('|'), lines);
        Assert.True(dirty, $"{key} changed the buffer but left the tab clean");
        Assert.True(changes > 0, $"{key} changed the buffer but raised no ContentChanged");
    }

    [Theory]
    [InlineData("Ctrl+K", 2, 0)]
    [InlineData("Ctrl+Delete", 2, 0)]
    [InlineData("Ctrl+Backspace", 0, 0)]
    [InlineData("Ctrl+Shift+Backspace", 0, 0)]
    public async Task Deleting_keys_that_change_nothing_leave_the_tab_clean(string key, int row, int col)
    {
        var (lines, dirty, changes) = await PressInEditor(key, row, col);

        Assert.Equal(Original, lines);
        Assert.False(dirty);
        Assert.Equal(0, changes);
    }

    // The edit is on a line narrower than the file's widest, so it doesn't change the content size and force a layout.
    [Fact]
    public async Task Dirty_marker_is_drawn_in_the_tab_header_as_soon_as_the_buffer_changes_and_cleared_on_save()
    {
        const string text = "x\nthis line is wider than the one being edited\n";
        _fs.AddFile("/work/a.txt", new MockFileData(text));
        _fs.AddFile("/work/b.txt", new MockFileData(text));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        EditorTab? tab = null;
        var dirtyHeader = "";
        var savedHeader = "";

        await HostSteps.Run(host,
            () =>
            {
                workbench.Editor.Open(_fs.FileInfo.New("/work/a.txt"));
                workbench.Editor.Open(_fs.FileInfo.New("/work/b.txt"));
            },
            () =>
            {
                tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.txt"));
                tab.FocusContent();
                tab.MoveCursor(0, 1);
            },
            () => host.App.InjectKey(new Key('y')),
            () =>
            {
                dirtyHeader = TabHeaderRow(host);
                host.App.InjectKey(Key.S.WithCtrl);
            },
            () => { savedHeader = TabHeaderRow(host); });

        Assert.Equal("xy", tab!.Lines[0]);
        Assert.Contains("│● a.txt│b.txt│", dirtyHeader);
        Assert.Contains("│a.txt│b.txt│", savedHeader);
    }

    private static string TabHeaderRow(WorkbenchHost host) =>
        host.App.Driver!.ToString()!.Split('\n').FirstOrDefault(l => l.Contains("b.txt")) ?? "";

    private async Task<(IReadOnlyList<string> Lines, bool Dirty, int Changes)> PressInEditor(string key, int row, int col)
    {
        _fs.AddFile("/work/a.txt", new MockFileData(string.Join("\n", Original)));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        EditorTab? tab = null;
        var changes = 0;

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.txt"));
                tab.FocusContent();
                tab.MoveCursor(row, col);
                tab.ContentChanged += (_, _) => changes++;
            },
            () => host.App.InjectKey(TestKeys.Chord(key).Single()));

        return (tab!.Lines, tab.IsDirty, changes);
    }

    private static Workbench.Workbench BuildWorkbench() =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
