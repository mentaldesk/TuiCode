using TuiCode.Editor;

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
}
