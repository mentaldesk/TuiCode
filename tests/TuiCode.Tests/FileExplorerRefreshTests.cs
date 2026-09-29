using System.Drawing;
using Terminal.Gui.Views;
using TuiCode.Explorer;

namespace TuiCode.Tests;

// Refresh explorer (#332).
public class FileExplorerRefreshTests
{
    private readonly MockFileSystem _fs = new();

    private FileExplorerView Open(params string[] entries)
    {
        _fs.AddDirectory("/work");
        foreach (var entry in entries)
        {
            if (entry.EndsWith('/')) _fs.AddDirectory(entry);
            else _fs.AddFile(entry, new MockFileData(entry));
        }
        var explorer = new FileExplorerView();
        explorer.Open(_fs.DirectoryInfo.New("/work"));
        return explorer;
    }

    private static IFileSystemInfo Node(FileExplorerView explorer, string relativePath)
    {
        IFileSystemInfo current = explorer.Root!;
        foreach (var segment in relativePath.Split('/'))
        {
            explorer.Expand(current);
            current = explorer.GetChildren(current).Single(c => c.Name == segment);
        }
        return current;
    }

    private static string[] Names(FileExplorerView explorer, string relativePath = "") =>
        explorer.GetChildren(relativePath.Length == 0 ? explorer.Root! : Node(explorer, relativePath))
            .Select(c => c.Name).ToArray();

    private string Relative(IFileSystemInfo? item) =>
        item is null ? "" : _fs.Path.GetRelativePath(_fs.Path.GetFullPath("/work"), item.FullName).Replace('\\', '/');

    [Fact]
    public void Refresh_shows_added_removed_and_renamed_entries_at_the_root()
    {
        using var explorer = Open("/work/a.txt", "/work/b.txt", "/work/c.txt");

        _fs.File.Delete("/work/a.txt");
        _fs.File.Move("/work/b.txt", "/work/d.txt");
        _fs.AddFile("/work/e.txt", new MockFileData(""));
        explorer.Refresh();

        Assert.Equal(["c.txt", "d.txt", "e.txt"], Names(explorer));
    }

    [Fact]
    public void Refresh_shows_changes_in_a_nested_expanded_folder()
    {
        using var explorer = Open("/work/src/lib/a.cs", "/work/src/lib/b.cs");
        Node(explorer, "src/lib/a.cs");

        _fs.File.Delete("/work/src/lib/a.cs");
        _fs.File.Move("/work/src/lib/b.cs", "/work/src/lib/c.cs");
        _fs.AddFile("/work/src/lib/d.cs", new MockFileData(""));
        explorer.Refresh();

        Assert.Equal(["c.cs", "d.cs"], Names(explorer, "src/lib"));
    }

    [Fact]
    public void Refresh_keeps_expanded_folders_expanded_and_collapsed_ones_collapsed()
    {
        using var explorer = Open("/work/open/inner/a.cs", "/work/shut/b.cs");
        Node(explorer, "open/inner/a.cs");

        explorer.Refresh();

        var children = explorer.GetChildren(explorer.Root!).ToDictionary(c => c.Name);
        Assert.True(explorer.IsExpanded(children["open"]));
        Assert.True(explorer.IsExpanded(explorer.GetChildren(children["open"]).Single()));
        Assert.False(explorer.IsExpanded(children["shut"]));
    }

    [Fact]
    public void Refresh_reads_only_expanded_folders()
    {
        using var explorer = Open("/work/open/a.cs", "/work/shut/b.cs");
        Node(explorer, "open/a.cs");
        var builder = new CountingTreeBuilder(explorer.TreeBuilder!);
        explorer.TreeBuilder = builder;

        explorer.Refresh();

        Assert.Equal([_fs.Path.GetFullPath("/work"), _fs.Path.GetFullPath("/work/open")], builder.Read.Order());
    }

    [Fact]
    public void Refresh_keeps_the_selection_on_the_same_path()
    {
        using var explorer = Open("/work/src/b.cs", "/work/src/c.cs");
        explorer.SelectedObject = Node(explorer, "src/c.cs");

        _fs.AddFile("/work/src/a.cs", new MockFileData(""));
        explorer.Refresh();

        Assert.Equal("src/c.cs", Relative(explorer.SelectedObject));
    }

    [Theory]
    [InlineData("b.cs", "c.cs")]
    [InlineData("c.cs", "b.cs")]
    public void Refresh_moves_a_deleted_selection_to_its_next_or_else_previous_neighbour(string deleted, string expected)
    {
        using var explorer = Open("/work/src/b.cs", "/work/src/c.cs");
        explorer.SelectedObject = Node(explorer, "src/" + deleted);

        _fs.File.Delete("/work/src/" + deleted);
        explorer.Refresh();

        Assert.Equal("src/" + expected, Relative(explorer.SelectedObject));
    }

    [Fact]
    public void Refresh_moves_a_deleted_only_child_to_its_parent()
    {
        using var explorer = Open("/work/src/a.cs");
        explorer.SelectedObject = Node(explorer, "src/a.cs");

        _fs.File.Delete("/work/src/a.cs");
        explorer.Refresh();

        Assert.Equal("src", Relative(explorer.SelectedObject));
    }

    [Fact]
    public void Refresh_moves_the_selection_past_a_deleted_expanded_ancestor()
    {
        using var explorer = Open("/work/a/x.cs", "/work/b/inner/y.cs", "/work/c.cs");
        explorer.SelectedObject = Node(explorer, "b/inner/y.cs");

        _fs.Directory.Delete("/work/b", recursive: true);
        explorer.Refresh();

        Assert.Equal("c.cs", Relative(explorer.SelectedObject));
        Assert.Equal(["a", "c.cs"], Names(explorer));
    }

    [Fact]
    public void Refresh_moves_a_deleted_selected_folder_to_its_neighbour()
    {
        using var explorer = Open("/work/a/x.cs", "/work/b/y.cs");
        explorer.SelectedObject = Node(explorer, "b");
        explorer.Expand(explorer.SelectedObject);

        _fs.Directory.Delete("/work/b", recursive: true);
        explorer.Refresh();

        Assert.Equal("a", Relative(explorer.SelectedObject));
    }

    [Fact]
    public void Refresh_drops_a_deleted_expanded_folder_that_was_not_selected()
    {
        using var explorer = Open("/work/gone/inner/a.cs", "/work/kept.cs");
        Node(explorer, "gone/inner/a.cs");
        explorer.SelectedObject = Node(explorer, "kept.cs");

        _fs.Directory.Delete("/work/gone", recursive: true);
        explorer.Refresh();

        Assert.Equal(["kept.cs"], Names(explorer));
        Assert.Equal("kept.cs", Relative(explorer.SelectedObject));
    }

    [Fact]
    public void Refresh_before_a_folder_is_open_does_nothing()
    {
        using var explorer = new FileExplorerView();

        explorer.Refresh();

        Assert.Null(explorer.Root);
    }

    [Fact]
    public void Refresh_leaves_the_scroll_position_alone_while_the_selection_is_in_view()
    {
        using var explorer = Open([.. Enumerable.Range(0, 20).Select(i => $"/work/f{i:00}.txt")]);
        explorer.Frame = new Rectangle(0, 0, 20, 5);
        explorer.SetContentSize(new Size(20, 21));
        explorer.ScrollOffsetVertical = 6;
        explorer.SelectedObject = Node(explorer, "f07.txt");

        _fs.AddFile("/work/f07a.txt", new MockFileData(""));
        explorer.Refresh();

        Assert.Equal(6, explorer.ScrollOffsetVertical);
        Assert.Equal("f07.txt", Relative(explorer.SelectedObject));
    }

    [Fact]
    public void Refresh_scrolls_only_as_far_as_keeping_the_selection_in_view_needs()
    {
        using var explorer = Open([.. Enumerable.Range(0, 20).Select(i => $"/work/f{i:00}.txt")]);
        explorer.Frame = new Rectangle(0, 0, 20, 5);
        explorer.SetContentSize(new Size(20, 21));
        explorer.ScrollOffsetVertical = 6;
        explorer.SelectedObject = Node(explorer, "f10.txt");

        _fs.AddFile("/work/f07a.txt", new MockFileData(""));
        _fs.AddFile("/work/f07b.txt", new MockFileData(""));
        explorer.Refresh();

        // f10 is now line 13 (root + 12 above it), so the least scroll that shows it puts it on the bottom row.
        Assert.Equal(9, explorer.ScrollOffsetVertical);
    }

    [Fact]
    public void ExpandedFolders_lists_the_root_then_folders_in_the_order_they_were_expanded()
    {
        using var explorer = Open("/work/b/x.cs", "/work/a/y.cs");
        var raised = 0;
        explorer.ExpandedFoldersChanged += (_, _) => raised++;

        explorer.Expand(Node(explorer, "b"));
        explorer.Refresh();
        explorer.Expand(Node(explorer, "a"));
        explorer.Refresh();

        Assert.Equal([".", "b", "a"], explorer.ExpandedFolders.Select(Full).Select(Relative));
        Assert.Equal(2, raised);
    }

    [Fact]
    public void ExpandedFolders_drops_a_collapsed_folder_and_everything_under_it()
    {
        using var explorer = Open("/work/a/inner/x.cs", "/work/b.cs");
        Node(explorer, "a/inner/x.cs");
        explorer.Refresh();

        explorer.Collapse(Node(explorer, "a"));
        explorer.Refresh();

        Assert.Equal(["."], explorer.ExpandedFolders.Select(Full).Select(Relative));
    }

    [Fact]
    public void Refresh_of_some_folders_reads_only_those_that_are_expanded()
    {
        using var explorer = Open("/work/open/a.cs", "/work/other/b.cs", "/work/shut/c.cs");
        Node(explorer, "open/a.cs");
        Node(explorer, "other/b.cs");
        var builder = new CountingTreeBuilder(explorer.TreeBuilder!);
        explorer.TreeBuilder = builder;

        _fs.AddFile("/work/open/d.cs", new MockFileData(""));
        explorer.Refresh([_fs.Path.GetFullPath("/work/open"), _fs.Path.GetFullPath("/work/shut")]);

        Assert.Equal([_fs.Path.GetFullPath("/work/open")], builder.Read);
        Assert.Equal(["a.cs", "d.cs"], Names(explorer, "open"));
    }

    [Fact]
    public void Refresh_of_some_folders_moves_a_deleted_selection_to_its_neighbour()
    {
        using var explorer = Open("/work/src/b.cs", "/work/src/c.cs");
        explorer.SelectedObject = Node(explorer, "src/b.cs");

        _fs.File.Delete("/work/src/b.cs");
        explorer.Refresh([_fs.Path.GetFullPath("/work/src")]);

        Assert.Equal("src/c.cs", Relative(explorer.SelectedObject));
    }

    [Fact]
    public void Refresh_of_some_folders_leaves_the_scroll_alone_when_the_selection_is_out_of_view()
    {
        using var explorer = Open([.. Enumerable.Range(0, 20).Select(i => $"/work/f{i:00}.txt")]);
        explorer.Frame = new Rectangle(0, 0, 20, 5);
        explorer.SetContentSize(new Size(20, 21));
        explorer.SelectedObject = Node(explorer, "f18.txt");
        explorer.ScrollOffsetVertical = 0;

        _fs.AddFile("/work/f00a.txt", new MockFileData(""));
        explorer.Refresh([_fs.Path.GetFullPath("/work")]);

        Assert.Equal(0, explorer.ScrollOffsetVertical);
        Assert.Equal("f18.txt", Relative(explorer.SelectedObject));
    }

    private IFileSystemInfo Full(string path) => _fs.DirectoryInfo.New(path);

    private sealed class CountingTreeBuilder(ITreeBuilder<IFileSystemInfo> inner) : ITreeBuilder<IFileSystemInfo>
    {
        public List<string> Read { get; } = [];

        public bool SupportsCanExpand => inner.SupportsCanExpand;

        public bool CanExpand(IFileSystemInfo toExpand) => inner.CanExpand(toExpand);

        public IEnumerable<IFileSystemInfo> GetChildren(IFileSystemInfo forObject)
        {
            Read.Add(forObject.FullName);
            return inner.GetChildren(forObject);
        }
    }
}
