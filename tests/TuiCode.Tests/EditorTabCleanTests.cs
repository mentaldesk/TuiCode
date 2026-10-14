using TuiCode.Abstractions;
using TuiCode.Editor;

namespace TuiCode.Tests;

// A tab is dirty only while what Save would write differs from the file (#550).
public class EditorTabCleanTests
{
    private const string Path = "/work/a.txt";

    private readonly MockFileSystem _fs = new();

    [Fact]
    public void Undoing_an_edit_leaves_the_tab_clean()
    {
        using var tab = Open("alpha\nbravo\n");

        Type(tab, Key.Z);
        Assert.True(tab.IsDirty);
        tab.Code.Undo();

        Assert.False(tab.IsDirty);
        Assert.Equal("a.txt", tab.Title);
    }

    [Fact]
    public void Redoing_away_from_the_saved_text_dirties_the_tab_and_undoing_again_cleans_it()
    {
        using var tab = Open("alpha\nbravo\n");
        Type(tab, Key.Z);
        tab.Code.Undo();

        tab.Code.Redo();
        var redone = (tab.IsDirty, tab.Title);
        tab.Code.Undo();

        Assert.Equal((true, "● a.txt"), redone);
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public void Each_edit_undone_but_the_last_leaves_the_tab_dirty()
    {
        using var tab = Open("alpha\nbravo\n");
        Type(tab, Key.X);
        Type(tab, Key.Enter);
        Type(tab, Key.Y);

        var dirty = new List<bool>();
        for (var i = 0; i < 3; i++)
        {
            tab.Code.Undo();
            dirty.Add(tab.IsDirty);
        }

        Assert.Equal([true, true, false], dirty);
    }

    [Fact]
    public void Deleting_a_character_and_typing_it_back_leaves_the_tab_clean()
    {
        using var tab = Open("alpha\nbravo\n");
        tab.MoveCursor(0, 1);

        Type(tab, Key.Backspace);
        var deleted = tab.IsDirty;
        Type(tab, Key.A);

        Assert.True(deleted);
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public void DirtyChanged_is_raised_when_the_tab_goes_clean_again()
    {
        using var tab = Open("alpha\n");
        var changes = 0;
        tab.DirtyChanged += (_, _) => changes++;

        Type(tab, Key.Z);
        tab.Code.Undo();

        Assert.Equal(2, changes);
    }

    [Fact]
    public void Editing_then_undoing_after_a_save_compares_with_what_was_saved()
    {
        using var tab = Open("alpha\n");
        Type(tab, Key.Z);
        tab.Save();

        Type(tab, Key.Y);
        tab.Code.Undo();
        var backToSaved = tab.IsDirty;
        tab.Code.Undo();

        Assert.False(backToSaved);
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public void Editing_then_undoing_after_a_reload_compares_with_the_reloaded_text()
    {
        using var tab = Open("alpha\n");
        _fs.File.WriteAllText(Path, "changed\n");
        _fs.File.SetLastWriteTimeUtc(Path, DateTime.UtcNow.AddMinutes(1));
        Assert.True(tab.Reload());

        Type(tab, Key.Z);
        tab.Code.Undo();

        Assert.False(tab.IsDirty);
    }

    [Fact]
    public void A_crlf_file_is_clean_once_edits_are_undone()
    {
        using var tab = Open("alpha\r\nbravo\r\n");

        Type(tab, Key.Z);
        tab.Code.Undo();

        Assert.False(tab.IsDirty);
    }

    [Fact]
    public void A_file_lacking_a_final_newline_stays_dirty_while_saving_would_add_one()
    {
        using var tab = Open("alpha");

        Type(tab, Key.Z);
        tab.Code.Undo();

        Assert.True(tab.IsDirty);
    }

    [Fact]
    public void A_file_lacking_a_final_newline_is_clean_once_undone_when_none_is_inserted()
    {
        using var tab = Open("alpha");
        tab.Settings = EditorSettings.Default with { InsertFinalNewline = false };

        Type(tab, Key.Z);
        tab.Code.Undo();

        Assert.False(tab.IsDirty);
    }

    [Fact]
    public void Removing_the_empty_last_line_is_clean_when_saving_puts_it_back()
    {
        using var tab = Open("alpha\n");
        tab.MoveCursor(1, 0);

        Type(tab, Key.Backspace);

        Assert.Equal(["alpha"], tab.Lines);
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public void A_restored_buffer_that_matches_the_file_opens_clean()
    {
        _fs.AddFile(Path, new MockFileData("alpha\n"));

        using var tab = EditorTab.Unsaved(_fs.FileInfo.New(Path), "alpha\n");

        Assert.False(tab.IsDirty);
        Assert.Equal("a.txt", tab.Title);
    }

    [Fact]
    public void A_restored_buffer_with_nothing_on_disk_is_dirty()
    {
        using var tab = EditorTab.Unsaved(_fs.FileInfo.New(Path), "alpha\n");

        Assert.True(tab.IsDirty);
    }

    [Fact]
    public void The_character_count_follows_every_kind_of_edit()
    {
        using var tab = Open("alpha\r\nbravo\r\ncharlie\r\n");
        tab.MoveCursor(1, 2);

        foreach (var key in new[] { Key.X, Key.Enter, Key.Backspace, Key.Backspace, Key.Delete.WithCtrl })
            Type(tab, key);
        tab.Code.Undo();
        tab.Content = "one\ntwo";
        tab.Replace(new TextMatch(0, 0, 3), "three\nfour");

        Assert.Equal(tab.Lines.Sum(l => (long)l.Length), tab.Code.LineChars);
    }

    private EditorTab Open(string content)
    {
        _fs.AddFile(Path, new MockFileData(content));
        return new EditorTab(_fs.FileInfo.New(Path));
    }

    private static void Type(EditorTab tab, Key key) => tab.Code.NewKeyDownEvent(key);
}
