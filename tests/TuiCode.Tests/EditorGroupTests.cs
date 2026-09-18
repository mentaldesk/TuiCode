using TuiCode.Editor;
using TuiCode.Syntax;

namespace TuiCode.Tests;

public class EditorGroupTests
{
    [Fact]
    public void OpenOrFocus_creates_a_tab_for_a_new_file_and_makes_it_active()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        using var group = new EditorGroup();

        var tab = group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));

        Assert.Single(group.Tabs);
        Assert.Same(tab, group.ActiveTab);
        Assert.Equal("a.txt", tab.File.Name);
    }

    [Fact]
    public void ActiveTabChanged_for_the_first_tab_sees_it_in_Tabs()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        using var group = new EditorGroup();
        var seen = new List<int>();
        group.ActiveTabChanged += (_, _) => seen.Add(group.Tabs.Count);

        group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));

        Assert.All(seen, count => Assert.Equal(1, count));
        Assert.NotEmpty(seen);
    }

    [Fact]
    public void OpenOrFocus_does_not_create_a_duplicate_when_the_same_file_is_reopened()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var group = new EditorGroup();

        var first = group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));
        group.OpenOrFocus(fs.FileInfo.New("/work/b.txt"));
        var firstAgain = group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));

        Assert.Equal(2, group.Tabs.Count);
        Assert.Same(first, firstAgain);
        Assert.Same(first, group.ActiveTab);
    }

    [Fact]
    public void CloseActive_removes_the_tab_and_activates_a_neighbour()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var group = new EditorGroup();
        group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));
        var b = group.OpenOrFocus(fs.FileInfo.New("/work/b.txt"));

        group.CloseActive();

        Assert.Single(group.Tabs);
        Assert.NotSame(b, group.ActiveTab);
        Assert.Equal("a.txt", group.ActiveTab!.File.Name);
    }

    [Fact]
    public void CloseActive_clears_the_active_tab_when_the_last_tab_is_closed()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        using var group = new EditorGroup();
        group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));

        group.CloseActive();

        Assert.Empty(group.Tabs);
        Assert.Null(group.ActiveTab);
    }

    [Fact]
    public void CloseAll_removes_every_tab_and_clears_the_active_tab()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var group = new EditorGroup();
        group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));
        group.OpenOrFocus(fs.FileInfo.New("/work/b.txt"));

        group.CloseAll();

        Assert.Empty(group.Tabs);
        Assert.Null(group.ActiveTab);
    }

    [Fact]
    public void NextTab_and_PreviousTab_cycle_through_open_tabs()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        fs.AddFile("/work/b.txt", new MockFileData("b"));
        fs.AddFile("/work/c.txt", new MockFileData("c"));
        using var group = new EditorGroup();
        var a = group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));
        var b = group.OpenOrFocus(fs.FileInfo.New("/work/b.txt"));
        var c = group.OpenOrFocus(fs.FileInfo.New("/work/c.txt"));

        // active is c after the third Open
        group.NextTab();
        Assert.Same(a, group.ActiveTab);
        group.NextTab();
        Assert.Same(b, group.ActiveTab);
        group.PreviousTab();
        Assert.Same(a, group.ActiveTab);
        group.PreviousTab();
        Assert.Same(c, group.ActiveTab);
    }

    [Fact]
    public void FocusByIndex_activates_the_nth_tab()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        fs.AddFile("/work/b.txt", new MockFileData("b"));
        fs.AddFile("/work/c.txt", new MockFileData("c"));
        using var group = new EditorGroup();
        var a = group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));
        group.OpenOrFocus(fs.FileInfo.New("/work/b.txt"));
        var c = group.OpenOrFocus(fs.FileInfo.New("/work/c.txt"));

        Assert.True(group.FocusByIndex(0));
        Assert.Same(a, group.ActiveTab);
        Assert.True(group.FocusByIndex(2));
        Assert.Same(c, group.ActiveTab);
    }

    [Fact]
    public void FocusByIndex_returns_false_when_index_out_of_range()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        using var group = new EditorGroup();
        var a = group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));

        Assert.False(group.FocusByIndex(5));
        Assert.False(group.FocusByIndex(-1));
        Assert.Same(a, group.ActiveTab);
    }

    [Fact]
    public void Editing_a_tab_marks_it_dirty_and_decorates_the_title()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("hello"));
        using var group = new EditorGroup();
        var tab = group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));
        Assert.False(tab.IsDirty);
        Assert.Equal("a.txt", tab.Title);

        tab.Content = "edited";

        Assert.True(tab.IsDirty);
        Assert.Equal("● a.txt", tab.Title);
    }

    [Fact]
    public void Save_clears_the_dirty_indicator()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("hello"));
        using var group = new EditorGroup();
        var tab = group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));
        tab.Content = "edited";
        Assert.True(tab.IsDirty);

        tab.Save();

        Assert.False(tab.IsDirty);
        Assert.Equal("a.txt", tab.Title);
        Assert.Equal("edited\n", fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public void FileSaved_event_fires_on_active_tab_save()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("hi"));
        var file = fs.FileInfo.New("/work/a.txt");
        using var group = new EditorGroup();
        group.OpenOrFocus(file);

        IFileInfo? saved = null;
        group.FileSaved += (_, f) => saved = f;
        group.SaveActive();

        Assert.NotNull(saved);
        Assert.Equal(file.FullName, saved!.FullName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Closing_the_last_tab_raises_ActiveTabChanged_with_null(bool closeAll)
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        using var group = new EditorGroup();
        group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));
        var raised = new List<EditorTab?>();
        group.ActiveTabChanged += (_, tab) => raised.Add(tab);

        if (closeAll) group.CloseAll();
        else group.CloseActive();

        Assert.Equal([null], raised);
    }

    [Fact]
    public void Relocate_points_a_tab_at_its_new_file_keeping_unsaved_changes()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        using var group = new EditorGroup();
        var tab = group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));
        tab.Content = "edited";
        fs.File.Move("/work/a.txt", "/work/b.txt");

        group.Relocate(fs.Path.GetFullPath("/work/a.txt"), fs.Path.GetFullPath("/work/b.txt"));

        Assert.Equal(fs.Path.GetFullPath("/work/b.txt"), tab.File.FullName);
        Assert.Equal("● b.txt", tab.Title);
        Assert.True(tab.IsDirty);
        Assert.Same(tab, group.OpenOrFocus(fs.FileInfo.New("/work/b.txt")));
        tab.Save();
        Assert.StartsWith("edited", fs.File.ReadAllText("/work/b.txt"));
    }

    [Fact]
    public void Relocate_moves_every_tab_under_a_folder_and_leaves_the_rest()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/src/a.txt", new MockFileData("a"));
        fs.AddFile("/work/src/lib/b.txt", new MockFileData("b"));
        fs.AddFile("/work/srcs/c.txt", new MockFileData("c"));
        using var group = new EditorGroup();
        foreach (var path in new[] { "/work/src/a.txt", "/work/src/lib/b.txt", "/work/srcs/c.txt" })
            group.OpenOrFocus(fs.FileInfo.New(path));

        group.Relocate(fs.Path.GetFullPath("/work/src"), fs.Path.GetFullPath("/work/code"));

        Assert.Equal(
            new[] { "/work/code/a.txt", "/work/code/lib/b.txt", "/work/srcs/c.txt" }.Select(fs.Path.GetFullPath),
            group.Tabs.Select(t => t.File.FullName));
    }

    [Fact]
    public void Relocate_infers_the_grammar_from_the_new_name()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("{}"));
        using var group = new EditorGroup(new SyntaxHighlighter(GrammarBundle.Load()));
        var tab = group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));

        group.Relocate(fs.Path.GetFullPath("/work/a.txt"), fs.Path.GetFullPath("/work/a.json"));

        Assert.Equal("json", tab.Grammar?.Id);
    }

    [Fact]
    public void CloseUnder_closes_the_tabs_under_a_folder_even_with_unsaved_changes()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/src/a.txt", new MockFileData("a"));
        fs.AddFile("/work/src/b.txt", new MockFileData("b"));
        fs.AddFile("/work/other.txt", new MockFileData("o"));
        using var group = new EditorGroup();
        group.OpenOrFocus(fs.FileInfo.New("/work/other.txt"));
        group.OpenOrFocus(fs.FileInfo.New("/work/src/a.txt")).Content = "edited";
        group.OpenOrFocus(fs.FileInfo.New("/work/src/b.txt"));

        group.CloseUnder(fs.Path.GetFullPath("/work/src"));

        var remaining = Assert.Single(group.Tabs);
        Assert.Equal(fs.Path.GetFullPath("/work/other.txt"), remaining.File.FullName);
        Assert.Same(remaining, group.ActiveTab);
    }

    [Fact]
    public void CloseUnder_keeps_the_active_tab_when_it_is_not_affected()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var group = new EditorGroup();
        group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));
        var b = group.OpenOrFocus(fs.FileInfo.New("/work/b.txt"));

        group.CloseUnder(fs.Path.GetFullPath("/work/a.txt"));

        Assert.Same(b, Assert.Single(group.Tabs));
        Assert.Same(b, group.ActiveTab);
    }
}
