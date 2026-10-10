using TuiCode.Editor;

namespace TuiCode.Tests;

// Two editor groups side by side (#460).
public sealed class EditorGroupsTests : IDisposable
{
    private readonly MockFileSystem _fs = new();
    private readonly EditorGroups _groups = new(new EditorGroup(), new EditorGroup());

    public void Dispose()
    {
        _groups.First.Dispose();
        _groups.Second.Dispose();
    }

    [Fact]
    public void Moving_the_active_tab_splits_into_a_second_group_and_focuses_it()
    {
        Open("a.txt");
        var b = Open("b.txt");

        Assert.True(_groups.MoveActiveToOther());

        Assert.True(_groups.IsSplit);
        Assert.Same(_groups.Second, _groups.Focused);
        Assert.Same(b, _groups.Second.ActiveTab);
        Assert.Equal(["a.txt"], Names(_groups.First));
    }

    [Fact]
    public void Moving_it_back_unsplits_once_the_second_group_is_empty()
    {
        Open("a.txt");
        var b = Open("b.txt");
        _groups.MoveActiveToOther();

        Assert.True(_groups.MoveActiveToOther());

        Assert.False(_groups.IsSplit);
        Assert.Same(_groups.First, _groups.Focused);
        Assert.Same(b, _groups.First.ActiveTab);
        Assert.Equal(2, _groups.First.Tabs.Count);
    }

    [Fact]
    public void Moving_the_first_groups_only_tab_out_leaves_one_group_with_everything()
    {
        var a = Open("a.txt");
        Open("b.txt");
        _groups.MoveActiveToOther();
        _groups.Focused = _groups.First;

        Assert.True(_groups.MoveActiveToOther());

        Assert.False(_groups.IsSplit);
        Assert.Same(_groups.First, _groups.Focused);
        Assert.Same(a, _groups.First.ActiveTab);
        Assert.Equal(2, _groups.First.Tabs.Count);
    }

    [Fact]
    public void The_only_tab_with_no_split_stays_put()
    {
        var a = Open("a.txt");

        Assert.False(_groups.MoveActiveToOther());

        Assert.False(_groups.IsSplit);
        Assert.Same(a, _groups.First.ActiveTab);
    }

    [Fact]
    public void Closing_a_groups_last_tab_closes_the_group()
    {
        Open("a.txt");
        Open("b.txt");
        _groups.MoveActiveToOther();

        _groups.Focused.CloseActive();

        Assert.False(_groups.IsSplit);
        Assert.Same(_groups.First, _groups.Focused);
        Assert.Equal(["a.txt"], Names(_groups.First));
    }

    [Fact]
    public void Closing_the_first_groups_last_tab_hands_it_the_second_groups_tabs()
    {
        Open("a.txt");
        var b = Open("b.txt");
        _groups.MoveActiveToOther();
        _groups.Focused = _groups.First;

        _groups.First.CloseActive();

        Assert.False(_groups.IsSplit);
        Assert.Same(b, _groups.First.ActiveTab);
        Assert.Empty(_groups.Second.TabCollection);
    }

    [Fact]
    public void Opening_a_file_open_in_the_other_group_focuses_it_there()
    {
        var a = Open("a.txt");
        Open("b.txt");
        _groups.MoveActiveToOther();

        var opened = _groups.Open(_fs.FileInfo.New("/work/a.txt"));

        Assert.Same(a, opened);
        Assert.Same(_groups.First, _groups.Focused);
        Assert.Single(_groups.Tabs, t => t.File.Name == "a.txt");
    }

    [Fact]
    public void A_new_file_opens_in_the_focused_group()
    {
        Open("a.txt");
        Open("b.txt");
        _groups.MoveActiveToOther();

        Open("c.txt");

        Assert.Equal(["b.txt", "c.txt"], Names(_groups.Second));
    }

    [Fact]
    public void Join_moves_every_tab_back_and_keeps_the_focused_tab_active()
    {
        Open("a.txt");
        Open("b.txt");
        _groups.MoveActiveToOther();
        var c = Open("c.txt");

        _groups.Join();

        Assert.False(_groups.IsSplit);
        Assert.Same(c, _groups.First.ActiveTab);
        Assert.Equal(3, _groups.First.Tabs.Count);
    }

    [Fact]
    public void A_moved_tab_raises_its_events_once_from_its_new_group()
    {
        Open("a.txt");
        var b = Open("b.txt");
        _groups.MoveActiveToOther();
        var saved = 0;
        _groups.FileSaved += (_, _) => saved++;

        b.Content = "changed\n";
        b.Save();

        Assert.Equal(1, saved);
    }

    [Fact]
    public void Closing_a_file_closes_its_diff_in_the_other_group()
    {
        var a = Open("a.txt");
        Open("b.txt");
        a.Content = "changed\n";
        _groups.First.Focus(a.File.FullName);
        var diff = _groups.First.CompareToSaved(a)!;
        _groups.MoveActiveToOther();
        Assert.Same(diff, _groups.Second.ActiveDiffTab);

        _groups.First.CloseEditor(a);

        Assert.Empty(_groups.DiffTabs);
    }

    [Fact]
    public void Tabs_and_settings_cover_both_groups()
    {
        Open("a.txt");
        Open("b.txt");
        _groups.MoveActiveToOther();

        _groups.GutterVisible = false;
        _groups.Relocate("/work/b.txt", "/work/renamed.txt");

        Assert.Equal(["a.txt", "renamed.txt"], _groups.Tabs.Select(t => t.File.Name).Order());
        Assert.All(_groups.Tabs, t => Assert.False(t.GutterVisible));

        _groups.CloseUnder("/work/renamed.txt");

        Assert.False(_groups.IsSplit);
        Assert.Equal(["a.txt"], Names(_groups.First));
    }

    private EditorTab Open(string name)
    {
        var path = $"/work/{name}";
        if (!_fs.File.Exists(path)) _fs.AddFile(path, new MockFileData(name + "\n"));
        return _groups.Open(_fs.FileInfo.New(path));
    }

    private static string[] Names(EditorGroup group) => [.. group.Tabs.Select(t => t.File.Name)];
}
