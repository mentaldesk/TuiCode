using TuiCode.Editor;

namespace TuiCode.Tests;

// Undo close tab brings the last closed file tab back where it was, with its undo (#578).
public sealed class UndoCloseTabTests : IDisposable
{
    private readonly MockFileSystem _fs = new();
    private readonly EditorGroups _groups = new(new EditorGroup(), new EditorGroup());

    public void Dispose()
    {
        _groups.First.Dispose();
        _groups.Second.Dispose();
    }

    [Fact]
    public void Reopening_puts_the_tab_back_in_its_place_and_makes_it_active()
    {
        Open("a.txt");
        var b = Open("b.txt");
        Open("c.txt");
        _groups.First.CloseEditor(b);

        var reopened = _groups.ReopenClosed();

        Assert.Equal(["a.txt", "b.txt", "c.txt"], Strip(_groups.First));
        Assert.Same(reopened, _groups.First.ActiveTab);
    }

    [Fact]
    public void Reopening_puts_the_cursor_back()
    {
        var a = Open("a.txt", "one\ntwo\nthree\n");
        a.MoveCursor(2, 3);
        _groups.First.CloseEditor(a);

        var reopened = _groups.ReopenClosed()!;

        Assert.Equal((2, 3), (reopened.CursorRow, reopened.CursorColumn));
    }

    [Fact]
    public void Undo_and_redo_carry_on_while_the_file_on_disk_is_what_the_tab_held()
    {
        var a = Open("a.txt", "one\n");
        a.Code.NewKeyDownEvent(Key.X);
        a.Save();
        _groups.First.CloseEditor(a);

        var reopened = _groups.ReopenClosed()!;
        reopened.Code.Undo();
        var undone = reopened.Lines;
        reopened.Code.Redo();

        Assert.Equal(["one", ""], undone);
        Assert.Equal(["xone", ""], reopened.Lines);
    }

    [Fact]
    public void A_file_changed_while_closed_comes_back_as_it_is_on_disk_with_nothing_to_undo()
    {
        var a = Open("a.txt", "one\n");
        a.Code.NewKeyDownEvent(Key.X);
        a.Save();
        _groups.First.CloseEditor(a);
        _fs.File.WriteAllText("/work/a.txt", "theirs\n");

        var reopened = _groups.ReopenClosed()!;
        reopened.Code.Undo();

        Assert.Equal(["theirs", ""], reopened.Lines);
    }

    [Fact]
    public void A_tab_closed_without_saving_comes_back_as_it_is_on_disk_with_nothing_to_undo()
    {
        var a = Open("a.txt", "one\n");
        a.Code.NewKeyDownEvent(Key.X);
        _groups.First.CloseEditor(a);

        var reopened = _groups.ReopenClosed()!;
        reopened.Code.Undo();

        Assert.Equal(["one", ""], reopened.Lines);
        Assert.False(reopened.IsDirty);
    }

    [Fact]
    public void Close_other_tabs_brings_each_back_newest_first_in_its_place()
    {
        Open("a.txt");
        Open("b.txt");
        Open("c.txt");
        Open("d.txt");
        _groups.First.FocusByIndex(2);
        _groups.First.Close(_groups.First.Others);

        var order = new[] { _groups.ReopenClosed()!, _groups.ReopenClosed()!, _groups.ReopenClosed()! }.Select(t => t.File.Name);

        Assert.Equal(["d.txt", "b.txt", "a.txt"], order);
        Assert.Equal(["a.txt", "b.txt", "c.txt", "d.txt"], Strip(_groups.First));
    }

    [Fact]
    public void Reopening_works_with_no_tab_open()
    {
        var a = Open("a.txt");
        _groups.First.CloseEditor(a);
        Assert.Null(_groups.First.Value);

        Assert.Equal("a.txt", _groups.ReopenClosed()!.File.Name);
    }

    [Fact]
    public void A_file_open_again_or_deleted_is_skipped_for_the_next()
    {
        var a = Open("a.txt");
        var b = Open("b.txt");
        var c = Open("c.txt");
        Open("d.txt");
        _groups.First.CloseEditor(a);
        _groups.First.CloseEditor(b);
        _groups.First.CloseEditor(c);
        Open("c.txt");
        _fs.File.Delete("/work/b.txt");

        Assert.Equal("a.txt", _groups.ReopenClosed()!.File.Name);
        Assert.False(_groups.CanReopen);
        Assert.Null(_groups.ReopenClosed());
    }

    [Fact]
    public void Nothing_is_reopenable_until_a_file_tab_closes()
    {
        var a = Open("a.txt", "one\n");
        a.Content = "two\n";
        Assert.False(_groups.CanReopen);

        var diff = _groups.First.CompareToSaved(a)!;
        _groups.First.CloseDiff(diff);
        Assert.False(_groups.CanReopen);

        _groups.First.CloseEditor(a);
        Assert.True(_groups.CanReopen);
    }

    [Fact]
    public void A_tab_reopens_in_the_second_group_while_it_is_there()
    {
        Open("a.txt");
        Open("b.txt");
        Open("c.txt");
        _groups.MoveActiveToOther();
        _groups.Focused = _groups.First;
        _groups.MoveActiveToOther();
        var b = _groups.Second.Tabs.Single(t => t.File.Name == "b.txt");
        _groups.Second.CloseEditor(b);

        _groups.Focused = _groups.First;
        _groups.ReopenClosed();

        Assert.Same(_groups.Second, _groups.Focused);
        Assert.Equal(["c.txt", "b.txt"], Strip(_groups.Second));
    }

    [Fact]
    public void A_tab_from_a_second_group_that_has_gone_reopens_in_the_first()
    {
        Open("a.txt");
        var b = Open("b.txt");
        _groups.MoveActiveToOther();
        _groups.Second.CloseEditor(b);
        Assert.False(_groups.IsSplit);

        _groups.ReopenClosed();

        Assert.False(_groups.IsSplit);
        Assert.Equal(["a.txt", "b.txt"], Strip(_groups.First));
    }

    [Fact]
    public void Switching_folders_forgets_the_closed_tabs()
    {
        var a = Open("a.txt");
        _groups.First.CloseEditor(a);

        _groups.CloseAll();

        Assert.False(_groups.CanReopen);
    }

    private EditorTab Open(string name, string content = "one\n")
    {
        var path = $"/work/{name}";
        if (!_fs.File.Exists(path)) _fs.AddFile(path, new MockFileData(content));
        return _groups.Open(_fs.FileInfo.New(path));
    }

    private static string[] Strip(EditorGroup group) =>
        [.. group.TabCollection.OfType<EditorTab>().Select(t => t.File.Name)];
}
