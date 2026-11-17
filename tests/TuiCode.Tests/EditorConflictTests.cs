using TuiCode.Editor;

namespace TuiCode.Tests;

// Keeping a side of a merge conflict, and stepping between them, in an editor tab (#693).
public class EditorConflictTests
{
    private static readonly string[] TwoWay = ["<<<<<<< HEAD", "mine", "=======", "theirs", ">>>>>>> main"];
    private static readonly string[] Diff3 = ["<<<<<<< HEAD", "mine", "||||||| base", "base", "=======", "theirs", ">>>>>>> main"];

    private readonly MockFileSystem _fs = new();

    public static TheoryData<ConflictKeep, string[]> Keeps => new()
    {
        { ConflictKeep.Current, ["mine"] },
        { ConflictKeep.Incoming, ["theirs"] },
        { ConflictKeep.Both, ["mine", "theirs"] },
    };

    [Theory]
    [MemberData(nameof(Keeps))]
    public void Keeping_a_side_replaces_a_two_way_block_markers_and_all(ConflictKeep keep, string[] kept)
    {
        var tab = Tab(["above", .. TwoWay, "below"]);
        tab.MoveCursor(3, 0);

        Assert.True(tab.KeepConflict(keep));

        Assert.Equal(["above", .. kept, "below"], tab.Lines);
        Assert.Equal((1, 0), (tab.CursorRow, tab.CursorColumn));
    }

    [Theory]
    [MemberData(nameof(Keeps))]
    public void Keeping_a_side_of_a_diff3_block_drops_the_base(ConflictKeep keep, string[] kept)
    {
        var tab = Tab(["above", .. Diff3, "below"]);
        tab.MoveCursor(1, 0);

        tab.KeepConflict(keep);

        Assert.Equal(["above", .. kept, "below"], tab.Lines);
    }

    [Theory]
    [InlineData(0, "mine|a|C|b|C")]
    [InlineData(6, "C|a|mine|b|C")]
    [InlineData(16, "C|a|C|b|mine")]
    public void Keeping_acts_on_the_first_middle_or_last_conflict_alone_at_the_files_start_and_end(int row, string expected)
    {
        var tab = Tab([.. TwoWay, "a", .. TwoWay, "b", .. TwoWay]);
        tab.MoveCursor(row, 0);

        tab.KeepConflict(ConflictKeep.Current);

        Assert.Equal(expected.Split('|').SelectMany(part => part == "C" ? TwoWay : [part]), tab.Lines);
        Assert.Equal(2, tab.Conflicts.Count);
    }

    [Fact]
    public void Keeping_an_empty_side_at_the_end_of_the_file_leaves_what_was_above()
    {
        var tab = Tab(["above", "<<<<<<< HEAD", "=======", "theirs", ">>>>>>> main"]);
        tab.MoveCursor(3, 0);

        tab.KeepConflict(ConflictKeep.Current);

        Assert.Equal(["above"], tab.Lines);
        Assert.Empty(tab.Conflicts);
    }

    [Theory]
    [MemberData(nameof(Keeps))]
    public void One_undo_puts_the_whole_block_back(ConflictKeep keep, string[] _)
    {
        string[] lines = ["above", .. Diff3, "below"];
        var tab = Tab(lines);
        tab.MoveCursor(4, 2);

        tab.KeepConflict(keep);
        tab.Code.Undo();

        Assert.Equal(lines, tab.Lines);
        Assert.Equal((4, 2), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void Outside_a_conflict_keeping_does_nothing()
    {
        string[] lines = ["above", .. TwoWay];
        var tab = Tab(lines);

        Assert.False(tab.KeepConflict(ConflictKeep.Both));
        Assert.Null(tab.ConflictAtCursor);
        Assert.Equal(lines, tab.Lines);
    }

    [Fact]
    public void Next_and_previous_go_to_the_marker_line_of_the_conflict_after_or_before_the_cursor()
    {
        var tab = Tab(["top", .. TwoWay, "middle", .. TwoWay, "end"]);

        Assert.True(tab.MoveToConflict(1));
        Assert.Equal(1, tab.CursorRow);
        tab.MoveCursor(3, 2);
        Assert.True(tab.MoveToConflict(1));
        Assert.Equal(7, tab.CursorRow);
        Assert.Equal(0, tab.CursorColumn);
        Assert.False(tab.MoveToConflict(1));
        Assert.Equal(7, tab.CursorRow);

        tab.MoveCursor(9, 0);
        Assert.True(tab.MoveToConflict(-1));
        Assert.Equal(7, tab.CursorRow);
        Assert.True(tab.MoveToConflict(-1));
        Assert.Equal(1, tab.CursorRow);
        Assert.False(tab.MoveToConflict(-1));
        Assert.Equal(1, tab.CursorRow);
    }

    [Fact]
    public void The_status_says_which_conflict_the_cursor_is_in_or_how_many_there_are()
    {
        var tab = Tab(["top", .. TwoWay, "middle", .. TwoWay, "end"]);

        Assert.Equal("2 conflicts", tab.ConflictStatus);
        tab.MoveCursor(10, 0);
        Assert.Equal("Conflict 2 of 2", tab.ConflictStatus);
        tab.MoveCursor(1, 0);
        Assert.Equal("Conflict 1 of 2", tab.ConflictStatus);

        tab.KeepConflict(ConflictKeep.Incoming);
        Assert.Equal("1 conflict", tab.ConflictStatus);
        tab.MoveToConflict(1);
        tab.KeepConflict(ConflictKeep.Incoming);
        Assert.Null(tab.ConflictStatus);
    }

    [Fact]
    public void A_file_without_markers_has_no_conflicts_and_no_status()
    {
        var tab = Tab(["one", "two"]);

        Assert.Empty(tab.Conflicts);
        Assert.Null(tab.ConflictStatus);
    }

    [Fact]
    public void Typing_a_marker_away_or_back_is_seen_at_once()
    {
        var tab = Tab(["a", .. TwoWay]);

        tab.ReplaceLines(3, 1, ["====== "]);
        Assert.Empty(tab.Conflicts);

        tab.ReplaceLines(3, 1, ["======="]);
        Assert.Single(tab.Conflicts);
    }

    private EditorTab Tab(string[] lines)
    {
        _fs.AddFile("/work/a.txt", new MockFileData(string.Join('\n', lines)));
        return new EditorTab(_fs.FileInfo.New("/work/a.txt"));
    }
}
