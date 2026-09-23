using TuiCode.Editor;

namespace TuiCode.Tests;

public class AlignedDiffTests
{
    private static DiffRow Both(int left, int right) => new(DiffRowKind.Both, left, right);
    private static DiffRow Mod(int left, int right) => new(DiffRowKind.Modified, left, right);
    private static DiffRow LeftOnly(int left) => new(DiffRowKind.LeftOnly, left, null);
    private static DiffRow RightOnly(int right) => new(DiffRowKind.RightOnly, null, right);

    [Fact]
    public void Compute_gives_only_both_rows_and_no_change_blocks_for_identical_input()
    {
        var diff = AlignedDiff.Compute(["a", "b", "c"], ["a", "b", "c"]);

        Assert.Equal([Both(0, 0), Both(1, 1), Both(2, 2)], diff.Rows);
        Assert.Empty(diff.ChangeBlocks);
    }

    [Fact]
    public void Compute_shows_an_insertion_as_right_only_rows()
    {
        var diff = AlignedDiff.Compute(["a", "c"], ["a", "x", "y", "c"]);

        Assert.Equal([Both(0, 0), RightOnly(1), RightOnly(2), Both(1, 3)], diff.Rows);
        Assert.Equal([1], diff.ChangeBlocks);
    }

    [Fact]
    public void Compute_shows_a_deletion_as_left_only_rows()
    {
        var diff = AlignedDiff.Compute(["a", "b", "c", "d"], ["a", "d"]);

        Assert.Equal([Both(0, 0), LeftOnly(1), LeftOnly(2), Both(3, 1)], diff.Rows);
        Assert.Equal([1], diff.ChangeBlocks);
    }

    [Fact]
    public void Compute_pairs_an_equal_length_change_as_modified_rows()
    {
        var diff = AlignedDiff.Compute(["a", "b", "c", "d"], ["a", "B", "C", "d"]);

        Assert.Equal([Both(0, 0), Mod(1, 1), Mod(2, 2), Both(3, 3)], diff.Rows);
        Assert.Equal([1], diff.ChangeBlocks);
    }

    [Fact]
    public void Compute_pairs_until_the_shorter_side_runs_out_when_the_right_side_is_longer()
    {
        var diff = AlignedDiff.Compute(["a", "old", "z"], ["a", "new1", "new2", "new3", "z"]);

        Assert.Equal([Both(0, 0), Mod(1, 1), RightOnly(2), RightOnly(3), Both(2, 4)], diff.Rows);
    }

    [Fact]
    public void Compute_pairs_until_the_shorter_side_runs_out_when_the_left_side_is_longer()
    {
        var diff = AlignedDiff.Compute(["a", "old1", "old2", "old3", "z"], ["a", "new", "z"]);

        Assert.Equal([Both(0, 0), Mod(1, 1), LeftOnly(2), LeftOnly(3), Both(4, 2)], diff.Rows);
    }

    [Fact]
    public void Compute_handles_changes_at_the_start_and_end_of_the_file()
    {
        var diff = AlignedDiff.Compute(["a", "b", "c"], ["A", "b", "c", "d"]);

        Assert.Equal([Mod(0, 0), Both(1, 1), Both(2, 2), RightOnly(3)], diff.Rows);
        Assert.Equal([0, 3], diff.ChangeBlocks);
    }

    [Fact]
    public void Compute_handles_an_empty_left_side()
    {
        var diff = AlignedDiff.Compute([], ["a", "b"]);

        Assert.Equal([RightOnly(0), RightOnly(1)], diff.Rows);
        Assert.Equal([0], diff.ChangeBlocks);
    }

    [Fact]
    public void Compute_handles_an_empty_right_side()
    {
        var diff = AlignedDiff.Compute(["a", "b"], []);

        Assert.Equal([LeftOnly(0), LeftOnly(1)], diff.Rows);
        Assert.Equal([0], diff.ChangeBlocks);
    }

    [Fact]
    public void Compute_lists_the_first_row_of_each_change_block_in_order()
    {
        var diff = AlignedDiff.Compute(["1", "2", "3", "4", "5", "6"], ["1", "two", "3", "4", "inserted", "5"]);

        Assert.Equal(
            [Both(0, 0), Mod(1, 1), Both(2, 2), Both(3, 3), RightOnly(4), Both(4, 5), LeftOnly(5)],
            diff.Rows);
        Assert.Equal([1, 4, 6], diff.ChangeBlocks);
    }

    [Fact]
    public void BufferLine_maps_rows_with_a_right_line_to_that_line()
    {
        var diff = AlignedDiff.Compute(["a", "b", "c"], ["a", "B", "x", "c"]);

        Assert.Equal([Both(0, 0), Mod(1, 1), RightOnly(2), Both(2, 3)], diff.Rows);
        Assert.Equal([0, 1, 2, 3], Enumerable.Range(0, diff.Rows.Count).Select(diff.BufferLine));
    }

    [Fact]
    public void BufferLine_maps_a_left_only_row_to_the_next_right_line()
    {
        var diff = AlignedDiff.Compute(["a", "b", "c", "d"], ["a", "d"]);

        Assert.Equal(1, diff.BufferLine(1));
        Assert.Equal(1, diff.BufferLine(2));
    }

    [Fact]
    public void BufferLine_clamps_a_left_only_row_at_the_end_to_the_last_line()
    {
        var diff = AlignedDiff.Compute(["a", "b", "c"], ["a", "b"]);

        Assert.Equal(1, diff.BufferLine(2));
    }

    [Fact]
    public void BufferLine_maps_to_line_zero_when_the_right_side_is_empty()
    {
        var diff = AlignedDiff.Compute(["a", "b"], []);

        Assert.Equal(0, diff.BufferLine(1));
    }

    [Fact]
    public void Compute_pairs_the_whole_differing_region_when_edits_exceed_the_limit()
    {
        var left = new[] { "same", "a1", "k1", "a2", "k2", "a3", "end" };
        var right = new[] { "same", "b1", "k1", "b2", "k2", "b3", "b4", "end" };

        var diff = AlignedDiff.Compute(left, right, maxEdits: 2);

        Assert.Equal(
            [Both(0, 0), Mod(1, 1), Mod(2, 2), Mod(3, 3), Mod(4, 4), Mod(5, 5), RightOnly(6), Both(6, 7)],
            diff.Rows);
        Assert.Equal([1], diff.ChangeBlocks);
    }

    [Fact]
    public void Compute_diffs_past_the_default_limit_when_given_a_higher_one()
    {
        var left = Enumerable.Range(0, 2 * LineDiff.MaxEdits).Select(i => $"line{i}").ToArray();
        var right = left.Select((line, i) => i % 2 == 0 ? line : $"edited{i}").ToArray();

        var diff = AlignedDiff.Compute(left, right, maxEdits: 5_000);

        Assert.Equal(LineDiff.MaxEdits, diff.ChangeBlocks.Count);
        Assert.All(diff.Rows, row => Assert.Contains(row.Kind, new[] { DiffRowKind.Both, DiffRowKind.Modified }));
    }

    [Fact]
    public void Block_covers_a_modified_run_on_both_sides()
    {
        var diff = AlignedDiff.Compute(["a", "b", "c", "d"], ["a", "B", "C", "d"]);

        Assert.Equal(new ChangeBlock(1, 2, 1, 2, 1, 2), diff.Block(diff.ChangeBlocks[0]));
    }

    [Fact]
    public void Block_of_an_insertion_has_no_left_lines()
    {
        var diff = AlignedDiff.Compute(["a", "c"], ["a", "x", "y", "c"]);

        Assert.Equal(new ChangeBlock(1, 2, 0, 0, 1, 2), diff.Block(diff.ChangeBlocks[0]));
    }

    [Fact]
    public void Block_of_a_deletion_points_at_the_buffer_line_that_took_its_place()
    {
        var diff = AlignedDiff.Compute(["a", "b", "c", "d"], ["a", "d"]);

        Assert.Equal(new ChangeBlock(1, 2, 1, 2, 1, 0), diff.Block(diff.ChangeBlocks[0]));
    }

    [Fact]
    public void Block_of_a_deletion_off_the_end_points_past_the_last_buffer_line()
    {
        var diff = AlignedDiff.Compute(["a", "b", "c"], ["a"]);

        Assert.Equal(new ChangeBlock(1, 2, 1, 2, 1, 0), diff.Block(diff.ChangeBlocks[0]));
    }

    [Fact]
    public void Block_stops_at_the_next_unchanged_row()
    {
        var diff = AlignedDiff.Compute(["a", "b", "c", "d", "e"], ["a", "B", "c", "D", "e"]);

        Assert.Equal([new ChangeBlock(1, 1, 1, 1, 1, 1), new ChangeBlock(3, 3, 3, 1, 3, 1)],
            diff.ChangeBlocks.Select(diff.Block));
    }
}
