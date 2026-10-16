using TuiCode.Workbench.Assistant;

namespace TuiCode.Tests;

public class AskContextTests
{
    private static readonly string[] Lines = [.. Enumerable.Range(1, 200).Select(i => $"line {i}")];

    [Fact]
    public void The_selection_is_the_context_with_its_path_relative_to_the_folder()
    {
        var context = AskContext.Of("/work/src/A.cs", "/work", Lines, (40, 57), 0);

        Assert.Equal((40, 57), (context.First, context.Last));
        Assert.Equal("lines 41–58 of src/A.cs", context.Label);
        Assert.Equal("line 41", context.Lines[0]);
        Assert.Equal(18, context.Lines.Count);
    }

    [Fact]
    public void With_no_selection_it_is_forty_lines_either_side_of_the_cursor()
    {
        var context = AskContext.Of("/work/A.cs", "/work", Lines, null, 100);

        Assert.Equal((60, 140), (context.First, context.Last));
    }

    [Theory]
    [InlineData(5, 0, 45)]
    [InlineData(190, 150, 199)]
    public void The_cursor_window_is_clamped_at_the_files_ends(int cursor, int first, int last)
    {
        var context = AskContext.Of("/work/A.cs", "/work", Lines, null, cursor);

        Assert.Equal((first, last), (context.First, context.Last));
    }

    [Fact]
    public void One_line_reads_as_a_line()
    {
        Assert.Equal("line 3 of A.cs", AskContext.Of("/work/A.cs", "/work", Lines, (2, 2), 0).Label);
    }

    [Fact]
    public void A_file_outside_the_folder_keeps_its_full_path()
    {
        Assert.Equal("line 1 of /elsewhere/A.cs", AskContext.Of("/elsewhere/A.cs", "/work", Lines, (0, 0), 0).Label);
    }

    [Fact]
    public void An_empty_file_is_one_empty_line()
    {
        var context = AskContext.Of("/work/A.cs", "/work", [], null, 0);

        Assert.Equal((0, 0, 0), (context.First, context.Last, context.Lines.Count));
    }
}
