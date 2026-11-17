using TuiCode.Editor;

namespace TuiCode.Tests;

public class MergeConflictsTests
{
    [Fact]
    public void Find_reads_a_two_way_block_with_the_names_on_its_markers()
    {
        var conflicts = MergeConflicts.Find(["a", "<<<<<<< HEAD", "mine", "=======", "theirs", ">>>>>>> origin/main", "b"]);

        Assert.Equal([new MergeConflict(1, null, 3, 5, "HEAD", "origin/main")], conflicts);
    }

    [Fact]
    public void Find_reads_a_diff3_block_with_its_base_section()
    {
        var conflicts = MergeConflicts.Find(["<<<<<<< HEAD", "mine", "||||||| merged common ancestors", "base", "=======", "theirs", ">>>>>>> main"]);

        Assert.Equal([new MergeConflict(0, 2, 4, 6, "HEAD", "main")], conflicts);
    }

    [Fact]
    public void Find_reads_several_blocks_in_order_and_names_with_spaces()
    {
        string[] lines =
        [
            "<<<<<<< HEAD", "1", "=======", "2", ">>>>>>> my branch",
            "between",
            "<<<<<<< Updated upstream", "=======", "3", ">>>>>>> Stashed changes",
        ];

        var conflicts = MergeConflicts.Find(lines);

        Assert.Equal(
        [
            new MergeConflict(0, null, 2, 4, "HEAD", "my branch"),
            new MergeConflict(6, null, 7, 9, "Updated upstream", "Stashed changes"),
        ], conflicts);
    }

    [Theory]
    [InlineData("<<<<<<< HEAD|mine|=======|theirs")]
    [InlineData("<<<<<<< HEAD|mine|>>>>>>> main")]
    [InlineData("mine|=======|theirs|>>>>>>> main")]
    [InlineData(">>>>>>> main|=======|<<<<<<< HEAD")]
    [InlineData("=======|<<<<<<< HEAD|mine|>>>>>>> main")]
    public void A_block_missing_a_marker_or_out_of_order_is_not_a_conflict(string lines)
    {
        Assert.Empty(MergeConflicts.Find(lines.Split('|')));
    }

    [Fact]
    public void A_restarted_block_drops_the_unterminated_one_before_it()
    {
        var conflicts = MergeConflicts.Find(["<<<<<<< HEAD", "stray", "<<<<<<< HEAD", "mine", "=======", "theirs", ">>>>>>> main"]);

        Assert.Equal([new MergeConflict(2, null, 4, 6, "HEAD", "main")], conflicts);
    }

    [Theory]
    [InlineData(" <<<<<<< HEAD")]
    [InlineData("x<<<<<<< HEAD")]
    [InlineData("<<<<<<<< HEAD")]
    [InlineData("<<<<<<<HEAD")]
    public void Markers_not_at_the_start_of_a_line_or_of_another_length_are_ignored(string start)
    {
        Assert.Empty(MergeConflicts.Find([start, "mine", "=======", "theirs", ">>>>>>> main"]));
    }

    [Fact]
    public void Count_reads_a_CRLF_file_and_one_with_a_marker_at_its_last_line()
    {
        Assert.Equal(2, MergeConflicts.Count(
            "<<<<<<< HEAD\r\na\r\n=======\r\nb\r\n>>>>>>> main\r\nmid\r\n<<<<<<< HEAD\r\n=======\r\n>>>>>>> main"));
    }

    [Theory]
    [InlineData(ConflictKeep.Current, "mine 1|mine 2")]
    [InlineData(ConflictKeep.Incoming, "theirs")]
    [InlineData(ConflictKeep.Both, "mine 1|mine 2|theirs")]
    public void Kept_leaves_the_side_or_sides_chosen_and_never_the_base(ConflictKeep keep, string expected)
    {
        string[] lines = ["<<<<<<< HEAD", "mine 1", "mine 2", "||||||| base", "base", "=======", "theirs", ">>>>>>> main"];
        var conflict = MergeConflicts.Find(lines).Single();

        Assert.Equal(expected.Split('|'), conflict.Kept(lines, keep));
    }
}
