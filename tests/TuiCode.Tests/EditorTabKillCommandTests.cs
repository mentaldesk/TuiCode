using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// TG 2.1.0's TextView kill commands edit the buffer without raising ContentsChanged. Boots a TG Application — serialised (#77).
public class EditorTabKillCommandTests : StaticConfigurationTest
{
    private static readonly string[] Original = ["alpha beta", "gamma delta", ""];

    private readonly MockFileSystem _fs = new();

    // Expected lines are '|'-separated; exact, so a command that ran twice would fail too.
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

    private async Task<(IReadOnlyList<string> Lines, bool Dirty, int Changes)> PressInEditor(string key, int row, int col)
    {
        _fs.AddFile("/work/a.txt", new MockFileData(string.Join("\n", Original)));
        using var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        var commands = new CommandService();
        using var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
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
}
