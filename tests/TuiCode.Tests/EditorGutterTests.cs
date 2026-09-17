using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using static TuiCode.Editor.LineChange;

namespace TuiCode.Tests;

public class EditorGutterTests
{
    [Fact]
    public void A_freshly_opened_tab_has_no_change_markers()
    {
        using var tab = OpenTab("alpha\nbravo\n");

        Assert.All(tab.LineChanges, c => Assert.Equal(None, c));
    }

    [Fact]
    public void Editing_marks_the_changed_lines_and_saving_clears_them()
    {
        using var tab = OpenTab("alpha\nbravo\ncharlie\n");

        tab.Replace(new TextMatch(1, 0, 5), "BRAVO");
        var afterEdit = tab.LineChanges.ToArray();
        tab.Save();

        Assert.Equal([None, Modified, None, None], afterEdit);
        Assert.All(tab.LineChanges, c => Assert.Equal(None, c));
    }

    [Fact]
    public void Markers_follow_successive_edits()
    {
        using var tab = OpenTab("alpha\nbravo\ncharlie\n");

        tab.Replace(new TextMatch(1, 0, 5), "BRAVO");
        var modified = tab.LineChanges.ToArray();
        tab.Replace(new TextMatch(2, 7, 0), "\nnew");
        var added = tab.LineChanges.ToArray();
        tab.Replace(new TextMatch(1, 0, 5), "bravo");

        Assert.Equal([None, Modified, None, None], modified);
        Assert.Equal([None, Modified, None, Added, None], added);
        Assert.Equal([None, None, None, Added, None], tab.LineChanges);
    }

    [Fact]
    public void Setting_content_is_diffed_against_the_loaded_text()
    {
        using var tab = OpenTab("alpha\nbravo\n");

        tab.Content = "alpha\nnew\nbravo\n";

        Assert.Equal([None, Added, None, None], tab.LineChanges);
    }

    [Fact]
    public void Restoring_the_saved_text_clears_the_markers()
    {
        using var tab = OpenTab("alpha\nbravo\n");

        tab.Content = "changed\nbravo\n";
        tab.Content = "alpha\nbravo\n";

        Assert.All(tab.LineChanges, c => Assert.Equal(None, c));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(999, 5)]
    [InlineData(1000, 6)]
    public void Gutter_width_fits_the_largest_line_number_plus_the_marker(int lineCount, int width)
    {
        Assert.Equal(width, EditorGutter.WidthFor(lineCount));
    }

    [Fact]
    public void Toggling_the_group_gutter_applies_to_open_and_newly_opened_tabs()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var group = new EditorGroup();
        var first = group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));

        group.GutterVisible = false;
        var second = group.OpenOrFocus(fs.FileInfo.New("/work/b.txt"));

        Assert.False(first.GutterVisible);
        Assert.False(second.GutterVisible);
    }

    private static EditorTab OpenTab(string content)
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/file.txt", new MockFileData(content));
        return new EditorTab(fs.FileInfo.New("/work/file.txt"));
    }
}

// Renders through a TG Application — serialised (#77).
public class EditorGutterHostTests : StaticConfigurationTest
{
    [Fact]
    public async Task Gutter_draws_line_numbers_with_a_change_marker_and_tg_hides_it()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("alpha\nbravo\n"));
        using var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        var commands = new CommandService();
        using var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
        EditorTab? tab = null;
        var withGutter = "";
        var withoutGutter = "";

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(fs.FileInfo.New("/work/a.txt"));
                tab.Replace(new TextMatch(1, 0, 5), "BRAVO");
            },
            () => { withGutter = host.App.Driver!.ToString(); },
            () => { commands.TryExecute(CommandIds.ToggleGutter); },
            () => { withoutGutter = host.App.Driver!.ToString(); });

        Assert.Contains("  1  alpha", withGutter);
        Assert.Contains("  2 ▎BRAVO", withGutter);
        Assert.DoesNotContain("  2 ▎", withoutGutter);
        Assert.Contains("BRAVO", withoutGutter);
        Assert.False(tab!.GutterVisible);
    }
}
