using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// Unsaved edits come back after TuiCode ends unexpectedly (#579). Each Workbench is a session; one left undisposed was killed.
public class UnsavedEditsTests
{
    private const string Recovery = "/home/.tui/recovery";

    private static readonly RecoveryOwner Killed = new(101, new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
    private static readonly RecoveryOwner Running = new(102, new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
    private static readonly RecoveryOwner Next = new(103, new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc));

    private readonly MockFileSystem _fs = new();
    private readonly WorkspaceStateStore _workspaces;
    private readonly List<Action> _scheduled = [];
    private long _now;

    public UnsavedEditsTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo\n"));
        _fs.AddFile("/work/b.txt", new MockFileData("bravo\n"));
        _fs.AddFile("/other/c.txt", new MockFileData("charlie\n"));
        _workspaces = new WorkspaceStateStore(_fs, "/state.json");
    }

    [Fact]
    public void A_copy_is_kept_only_once_typing_has_paused()
    {
        var session = Session(Killed);
        Edit(session, "/work/a.txt", "one\ntwo\nthree\n");

        _now += 1000;
        RunScheduled(session);
        var early = Copies().Length;
        _now += 1000;
        RunScheduled(session);

        Assert.Equal(0, early);
        Assert.Single(Copies());
    }

    [Fact]
    public void A_clean_tab_keeps_no_copy()
    {
        var session = Session(Killed);
        session.OpenFolder(Folder("/work"));
        session.OpenFile(File("/work/a.txt"));
        session.Editor.Group.ActiveTab!.MoveCursor(1, 1);

        Idle(session);

        Assert.Empty(Copies());
    }

    [Fact]
    public void After_a_kill_the_folder_brings_the_tab_back_with_its_edits_and_cursor()
    {
        var killed = Session(Killed);
        var tab = Edit(killed, "/work/a.txt", "one\ntwo\nthree\n");
        tab.MoveCursor(2, 3);
        Idle(killed);

        using var next = Session(Next);
        next.OpenFolder(Folder("/work"));

        var recovered = next.Editor.Group.ActiveTab!;
        Assert.Equal(["one", "two", "three", ""], recovered.Lines);
        Assert.True(recovered.IsDirty);
        Assert.Equal("● a.txt", recovered.Title);
        Assert.Equal((2, 3), (recovered.CursorRow, recovered.CursorColumn));
        Assert.Equal("one\ntwo\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public void A_recovered_file_opened_from_the_explorer_shows_its_edits()
    {
        var killed = Session(Killed);
        Edit(killed, "/work/a.txt", "mine\n");
        Idle(killed);

        using var next = Session(Next);
        next.OpenFile(File("/work/a.txt"));

        Assert.Equal(["mine", ""], next.Editor.Group.ActiveTab!.Lines);
    }

    [Fact]
    public void Undo_starts_at_the_recovered_text()
    {
        var killed = Session(Killed);
        Edit(killed, "/work/a.txt", "mine\n");
        Idle(killed);

        using var next = Session(Next);
        next.OpenFile(File("/work/a.txt"));
        var tab = next.Editor.Group.ActiveTab!;
        tab.Code.Undo();

        Assert.Equal(["mine", ""], tab.Lines);
    }

    [Fact]
    public void Saving_a_recovered_tab_writes_the_edits()
    {
        var killed = Session(Killed);
        Edit(killed, "/work/a.txt", "mine\n");
        Idle(killed);

        using var next = Session(Next);
        next.OpenFile(File("/work/a.txt"));
        next.Editor.Group.ActiveTab!.Save();

        Assert.Equal("mine\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public void A_file_changed_meanwhile_is_marked_and_saving_it_would_overwrite_theirs()
    {
        var killed = Session(Killed);
        Edit(killed, "/work/a.txt", "mine\n");
        Idle(killed);
        _fs.File.WriteAllText("/work/a.txt", "theirs\n");

        using var next = Session(Next);
        next.OpenFolder(Folder("/work"));

        var tab = next.Editor.Group.ActiveTab!;
        Assert.Equal(["mine", ""], tab.Lines);
        Assert.Equal(DiskState.Changed, tab.DiskMarker);
        Assert.Equal(DiskState.Changed, tab.DiskNow);
        Assert.Equal("● a.txt ⚠ ", tab.Title);
    }

    [Fact]
    public void A_file_deleted_meanwhile_comes_back_marked_and_saving_writes_it_back()
    {
        var killed = Session(Killed);
        Edit(killed, "/work/a.txt", "mine\n");
        Idle(killed);
        _fs.File.Delete("/work/a.txt");

        using var next = Session(Next);
        next.OpenFolder(Folder("/work"));

        var tab = next.Editor.Group.ActiveTab!;
        Assert.Equal("a.txt", tab.File.Name);
        Assert.Equal(DiskState.Gone, tab.DiskMarker);
        tab.Save();
        Assert.Equal("mine\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public void A_line_ending_style_is_kept_through_recovery()
    {
        _fs.File.WriteAllText("/work/a.txt", "one\r\ntwo\r\n");
        var killed = Session(Killed);
        Edit(killed, "/work/a.txt", "one\ntwo\nthree\n");
        Idle(killed);

        using var next = Session(Next);
        next.OpenFile(File("/work/a.txt"));

        Assert.Equal(LineEnding.CRLF, next.Editor.Group.ActiveTab!.LineEnding);
    }

    public enum Discarding { Save, UndoToSaved, Reload, CloseUnsaved, QuitUnsaved, SwitchFolder }

    [Theory]
    [InlineData(Discarding.Save)]
    [InlineData(Discarding.UndoToSaved)]
    [InlineData(Discarding.Reload)]
    [InlineData(Discarding.CloseUnsaved)]
    [InlineData(Discarding.QuitUnsaved)]
    [InlineData(Discarding.SwitchFolder)]
    public void Nothing_comes_back_once_the_edits_are_saved_or_thrown_away(Discarding how)
    {
        var killed = Session(Killed);
        var tab = Edit(killed, "/work/a.txt", "one\ntwo\nthree\n");
        Idle(killed);
        Assert.Single(Copies());

        switch (how)
        {
            case Discarding.Save: tab.Save(); break;
            case Discarding.UndoToSaved: tab.Content = "one\ntwo\n"; break;
            case Discarding.Reload: tab.Reload(evenIfUnchanged: true); break;
            case Discarding.CloseUnsaved: killed.Editor.Groups.First.CloseEditor(tab); break;
            case Discarding.QuitUnsaved: killed.UnsavedEdits!.DiscardAll(); break;
            case Discarding.SwitchFolder: killed.OpenFolder(Folder("/other")); break;
        }
        Idle(killed);

        Assert.Empty(Copies());
        using var next = Session(Next);
        next.OpenFile(File("/work/a.txt"));
        Assert.False(next.Editor.Group.ActiveTab!.IsRecovered);
    }

    [Fact]
    public void A_second_session_neither_picks_up_nor_deletes_a_running_ones_edits()
    {
        var running = Session(Running);
        Edit(running, "/work/a.txt", "mine\n");
        Idle(running);

        using (var second = Session(Next))
        {
            second.OpenFile(File("/work/a.txt"));
            var tab = second.Editor.Group.ActiveTab!;
            Assert.False(tab.IsDirty);
            Assert.Equal(["one", "two", ""], tab.Lines);
            second.Editor.Groups.First.CloseEditor(tab);
            Idle(second);
        }

        Assert.Single(Copies());
    }

    [Fact]
    public void Recovering_takes_over_the_copy()
    {
        var killed = Session(Killed);
        Edit(killed, "/work/a.txt", "mine\n");
        Idle(killed);

        using var next = Session(Next);
        next.OpenFile(File("/work/a.txt"));
        next.UnsavedEdits!.Wait();

        Assert.EndsWith("-103.json", Assert.Single(Copies()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_commit_message_is_recovered_too()
    {
        _fs.AddFile("/work/.git/COMMIT_EDITMSG", new MockFileData("\n"));
        var killed = Session(Killed);
        Edit(killed, "/work/.git/COMMIT_EDITMSG", "Fix the thing\n");
        Idle(killed);

        using var next = Session(Next);
        next.OpenFile(File("/work/.git/COMMIT_EDITMSG"));

        Assert.Equal(["Fix the thing", ""], next.Editor.Group.ActiveTab!.Lines);
    }

    [Fact]
    public void Flushing_keeps_edits_made_just_now()
    {
        var killed = Session(Killed);
        Edit(killed, "/work/a.txt", "mine\n");

        killed.UnsavedEdits!.Flush();

        Assert.Single(Copies());
        Assert.Equal("one\ntwo\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public void A_renamed_file_takes_its_copy_with_it()
    {
        var killed = Session(Killed);
        Edit(killed, "/work/a.txt", "mine\n");
        Idle(killed);

        killed.Move(File("/work/a.txt"), "renamed.txt");
        Idle(killed);

        using var next = Session(Next);
        next.OpenFile(File("/work/renamed.txt"));
        Assert.Equal(["mine", ""], next.Editor.Group.ActiveTab!.Lines);
        Assert.Single(Copies());
    }

    private Workbench.Workbench Session(RecoveryOwner owner)
    {
        var store = new RecoveryStore(_fs, Recovery, owner, o => o == Running);
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart(), _workspaces, recovery: store);
        workbench.UnsavedEdits!.Schedule = (_, action) => _scheduled.Add(action);
        workbench.UnsavedEdits.Clock = () => _now;
        return workbench;
    }

    private EditorTab Edit(Workbench.Workbench session, string path, string content)
    {
        session.OpenFolder(Folder("/work"));
        session.OpenFile(File(path));
        var tab = session.Editor.Group.ActiveTab!;
        tab.Content = content;
        return tab;
    }

    private void Idle(Workbench.Workbench session)
    {
        _now += 60_000;
        RunScheduled(session);
    }

    private void RunScheduled(Workbench.Workbench session)
    {
        var due = _scheduled.ToList();
        _scheduled.Clear();
        foreach (var action in due) action();
        session.UnsavedEdits!.Wait();
    }

    private string[] Copies() => _fs.Directory.Exists(Recovery) ? _fs.Directory.GetFiles(Recovery) : [];

    private IDirectoryInfo Folder(string path) => _fs.DirectoryInfo.New(path);

    private IFileInfo File(string path) => _fs.FileInfo.New(path);
}
