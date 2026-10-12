using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// Reopening a file puts the cursor back where it was (#502).
public class WorkbenchFilePositionTests
{
    private const string PositionsPath = "/positions.json";

    private readonly MockFileSystem _fs = new();
    private readonly WorkspaceStateStore _workspaces;
    private readonly FilePositionStore _positions;

    public WorkbenchFilePositionTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData(Lines(100)));
        _fs.AddFile("/work/b.txt", new MockFileData(Lines(100)));
        _fs.AddFile("/work/sub/c.txt", new MockFileData(Lines(100)));
        _fs.AddFile("/other/d.txt", new MockFileData(Lines(100)));
        _workspaces = new WorkspaceStateStore(_fs, "/state.json");
        _positions = new FilePositionStore(_fs, PositionsPath);
    }

    [Fact]
    public void Reopening_a_closed_file_puts_the_cursor_back()
    {
        using var workbench = Build();
        workbench.OpenFolder(Folder("/work"));
        workbench.OpenFile(File("/work/a.txt"));
        workbench.Editor.Group.ActiveTab!.MoveCursor(42, 3);
        workbench.Editor.CloseActive();

        workbench.OpenFile(File("/work/a.txt"));

        Assert.Equal((42, 3), Cursor(workbench));
    }

    [Fact]
    public void Moving_the_cursor_records_nothing_until_the_tab_closes()
    {
        using var workbench = Build();
        workbench.OpenFolder(Folder("/work"));
        workbench.OpenFile(File("/work/a.txt"));

        workbench.Editor.Group.ActiveTab!.MoveCursor(42, 3);

        Assert.False(_fs.File.Exists(PositionsPath));
        workbench.Editor.CloseActive();
        Assert.Equal(42, _positions.Load(Full("/work/a.txt"))!.Line);
    }

    [Fact]
    public void Quitting_and_reopening_the_folder_puts_every_tab_back_at_its_own_position()
    {
        using (var first = Build())
        {
            first.OpenFolder(Folder("/work"));
            first.OpenFile(File("/work/a.txt"));
            first.Editor.Group.ActiveTab!.MoveCursor(42, 3);
            first.OpenFile(File("/work/b.txt"));
            first.Editor.Group.ActiveTab!.MoveCursor(17, 1);
        }

        using var workbench = Build();
        workbench.OpenFolder(Folder("/work"));

        Assert.Equal((17, 1), Cursor(workbench));
        workbench.Editor.Group.Focus(Full("/work/a.txt"));
        Assert.Equal((42, 3), Cursor(workbench));
    }

    [Fact]
    public void Switching_folders_and_back_puts_each_tab_back_at_its_position()
    {
        using var workbench = Build();
        workbench.OpenFolder(Folder("/work"));
        workbench.OpenFile(File("/work/a.txt"));
        workbench.Editor.Group.ActiveTab!.MoveCursor(42, 3);

        workbench.OpenFolder(Folder("/other"));
        workbench.OpenFolder(Folder("/work"));

        Assert.Equal((42, 3), Cursor(workbench));
    }

    [Fact]
    public void A_file_reached_from_another_folder_opens_where_it_was()
    {
        using var workbench = Build();
        workbench.OpenFolder(Folder("/work"));
        workbench.OpenFile(File("/work/sub/c.txt"));
        workbench.Editor.Group.ActiveTab!.MoveCursor(42, 3);
        workbench.Editor.CloseActive();

        workbench.OpenFolder(Folder("/work/sub"));
        workbench.OpenFile(File("/work/sub/c.txt"));

        Assert.Equal((42, 3), Cursor(workbench));
    }

    [Fact]
    public void Opening_a_find_result_goes_to_the_match_not_the_remembered_place()
    {
        using var workbench = Build();
        workbench.OpenFolder(Folder("/work"));
        workbench.OpenFile(File("/work/a.txt"));
        workbench.Editor.Group.ActiveTab!.MoveCursor(42, 3);
        workbench.Editor.CloseActive();

        workbench.OpenMatch(File("/work/a.txt"), new TextMatch(7, 0, 4));

        Assert.Equal((7, 4), Cursor(workbench));
    }

    [Fact]
    public void A_file_inside_git_always_opens_at_the_top()
    {
        _fs.AddFile("/work/.git/COMMIT_EDITMSG", new MockFileData(Lines(20)));
        using var workbench = Build();
        workbench.OpenFile(File("/work/.git/COMMIT_EDITMSG"));
        workbench.Editor.Group.ActiveTab!.MoveCursor(10, 2);
        workbench.Editor.CloseActive();

        workbench.OpenFile(File("/work/.git/COMMIT_EDITMSG"));

        Assert.Equal((0, 0), Cursor(workbench));
    }

    [Fact]
    public void A_file_that_got_shorter_opens_on_its_last_line()
    {
        using var workbench = Build();
        workbench.OpenFolder(Folder("/work"));
        workbench.OpenFile(File("/work/a.txt"));
        workbench.Editor.Group.ActiveTab!.MoveCursor(90, 3);
        workbench.Editor.CloseActive();
        _fs.File.WriteAllText("/work/a.txt", Lines(10));

        workbench.OpenFile(File("/work/a.txt"));

        Assert.Equal((9, 3), Cursor(workbench));
    }

    private static (int, int) Cursor(Workbench.Workbench workbench) =>
        (workbench.Editor.Group.ActiveTab!.CursorRow, workbench.Editor.Group.ActiveTab.CursorColumn);

    private IDirectoryInfo Folder(string path) => _fs.DirectoryInfo.New(path);

    private IFileInfo File(string path) => _fs.FileInfo.New(path);

    private static string Full(string path) => new MockFileSystem().Path.GetFullPath(path);

    private static string Lines(int count) => string.Join('\n', Enumerable.Range(1, count).Select(i => $"line {i}"));

    private Workbench.Workbench Build() =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart(), _workspaces, _positions);
}
