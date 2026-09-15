using TuiCode.Editor;
using static TuiCode.Editor.LineChange;

namespace TuiCode.Tests;

public class LineDiffTests
{
    [Fact]
    public void Compute_marks_nothing_when_the_buffer_matches_the_baseline()
    {
        Assert.Equal([None, None, None], LineDiff.Compute(["a", "b", "c"], ["a", "b", "c"]));
    }

    [Fact]
    public void Compute_marks_an_edited_line_modified()
    {
        Assert.Equal([None, Modified, None], LineDiff.Compute(["a", "b", "c"], ["a", "B", "c"]));
    }

    [Fact]
    public void Compute_marks_inserted_lines_added()
    {
        Assert.Equal([None, Added, Added, None], LineDiff.Compute(["a", "c"], ["a", "x", "y", "c"]));
    }

    [Fact]
    public void Compute_marks_the_line_below_a_deletion()
    {
        Assert.Equal([None, Deleted], LineDiff.Compute(["a", "b", "c"], ["a", "c"]));
    }

    [Fact]
    public void Compute_marks_a_deletion_at_the_end_on_the_last_line()
    {
        Assert.Equal([None, Deleted], LineDiff.Compute(["a", "b", "c"], ["a", "b"]));
    }

    [Fact]
    public void Compute_marks_a_replaced_block_modified_even_when_it_grows()
    {
        Assert.Equal([None, Modified, Modified, Modified, None],
            LineDiff.Compute(["a", "old", "z"], ["a", "new1", "new2", "new3", "z"]));
    }

    [Fact]
    public void Compute_keeps_separate_hunks_apart()
    {
        Assert.Equal([None, Modified, None, None, Added, None],
            LineDiff.Compute(["1", "2", "3", "4", "5"], ["1", "two", "3", "4", "inserted", "5"]));
    }

    [Fact]
    public void Compute_realigns_shifted_lines_instead_of_marking_them_all()
    {
        Assert.Equal([Added, None, None, Deleted], LineDiff.Compute(["a", "b", "c", "d"], ["x", "a", "b", "d"]));
    }

    [Fact]
    public void Compute_handles_an_empty_side()
    {
        Assert.Equal([Added, Added], LineDiff.Compute([], ["a", "b"]));
        Assert.Empty(LineDiff.Compute(["a", "b"], []));
    }

    [Fact]
    public void Compute_marks_the_whole_differing_region_when_edits_exceed_the_budget()
    {
        var baseline = Enumerable.Range(0, 2 * LineDiff.MaxEdits).Select(i => $"line{i}").ToArray();
        var current = baseline.Select((line, i) => i % 2 == 0 ? line : $"edited{i}").ToArray();

        var changes = LineDiff.Compute(baseline, current);

        Assert.Equal(None, changes[0]);
        Assert.All(changes[1..], c => Assert.Equal(Modified, c));
    }
}
