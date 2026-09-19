using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.DocumentInfo;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives the `di` mnemonic through the leader (#152). Boots a TG Application — serialised (#77).
public class DocumentInfoHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();

    [Fact]
    public async Task Di_shows_the_document_and_selection_counts_and_Esc_returns_to_the_editor()
    {
        _fs.AddFile("/work/src/a.txt", new MockFileData("alpha bravo\ncharlie delta\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        EditorTab? tab = null;
        DocumentInfoView? shown = null;
        List<string> labels = [];

        await HostSteps.Run(host,
            () =>
            {
                workbench.OpenFile(_fs.FileInfo.New("/work/src/a.txt"));
                tab = workbench.Editor.Group.ActiveTab;
                tab!.Select(new TextMatch(0, 6, 5));
            },
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.D),
            () => host.App.InjectKey(Key.I),
            () => (shown = Info(workbench)) is not null,
            () => { labels = Labels(shown!); host.App.InjectKey(Key.Esc); },
            () => Info(workbench) is null);

        Assert.Equal(["Document", "Selection"], shown!.Columns.Select(c => c.Header));
        Assert.Equal(["3", "4", "24", "22"], shown.Columns[0].Values);
        Assert.Equal(["1", "1", "5", "5"], shown.Columns[1].Values);
        Assert.Contains("src/a.txt", labels);
        Assert.Contains("Plain Text  •  LF  •  26 B on disk", labels);
        Assert.True(tab!.ContentHasFocus, "Focus should return to the editor after closing");
    }

    [Fact]
    public async Task Di_without_a_selection_has_no_selection_column_and_Enter_closes_it()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        DocumentInfoView? shown = null;
        List<string> labels = [];

        await HostSteps.Run(host,
            () =>
            {
                workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
                workbench.Editor.Group.ActiveTab!.Content = "alpha bravo\n";
            },
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.D),
            () => host.App.InjectKey(Key.I),
            () => (shown = Info(workbench)) is not null,
            () => { labels = Labels(shown!); host.App.InjectKey(Key.Enter); },
            () => Info(workbench) is null);

        Assert.Equal(["Document"], shown!.Columns.Select(c => c.Header));
        Assert.Contains(labels, l => l.EndsWith("•  unsaved changes"));
    }

    [Fact]
    public async Task Di_with_no_file_open_says_so()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.D),
            () => host.App.InjectKey(Key.I),
            () => { });

        Assert.Null(Info(workbench));
        Assert.Equal("No file is open.", workbench.StatusBar.DisplayedText);
    }

    private static List<string> Labels(DocumentInfoView view) => view.SubViews.OfType<Label>().Select(l => l.Text).ToList();

    private static DocumentInfoView? Info(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<DocumentInfoView>().SingleOrDefault();

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

public class DocumentInfoViewTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(14_540, "14.2 KB")]
    [InlineData(3 * 1024 * 1024, "3.0 MB")]
    public void FormatSize_uses_the_largest_whole_unit(long bytes, string expected)
    {
        using var _ = new CultureScope("en-US");

        Assert.Equal(expected, DocumentInfoView.FormatSize(bytes));
    }

    [Fact]
    public void Facts_lists_size_and_unsaved_changes_only_when_known()
    {
        Assert.Equal("C#  •  CRLF  •  unsaved changes", DocumentInfoView.Facts("C#", LineEnding.CRLF, null, dirty: true));
        Assert.Equal("Plain Text  •  LF  •  10 B on disk", DocumentInfoView.Facts("Plain Text", LineEnding.LF, 10, dirty: false));
    }

    [Fact]
    public void Counts_have_thousands_separators()
    {
        using var _ = new CultureScope("en-US");
        using var view = new DocumentInfoView("a.txt", "", new DocumentStats(1_234, 5, 12_345, 1_000_000), null);

        Assert.Equal(["1,234", "5", "12,345", "1,000,000"], view.Columns[0].Values);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo _previous = System.Globalization.CultureInfo.CurrentCulture;

        public CultureScope(string name) => System.Globalization.CultureInfo.CurrentCulture = new(name);

        public void Dispose() => System.Globalization.CultureInfo.CurrentCulture = _previous;
    }
}
