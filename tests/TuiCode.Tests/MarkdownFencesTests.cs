using TuiCode.Editor;

namespace TuiCode.Tests;

public class MarkdownFencesTests
{
    [Fact]
    public void CodeLines_marks_each_fenced_block_with_its_fences()
    {
        string[] lines = ["text", "```sh", "code", "```", "text", "~~~", "code", "~~~", "text"];

        Assert.Equal([false, true, true, true, false, true, true, true, false], MarkdownFences.CodeLines(lines));
    }

    [Fact]
    public void CodeLines_keeps_a_block_open_past_a_shorter_or_different_fence()
    {
        string[] lines = ["````", "```", "~~~~", "````", "text"];

        Assert.Equal([true, true, true, true, false], MarkdownFences.CodeLines(lines));
    }

    [Fact]
    public void CodeLines_takes_fences_indented_in_a_list_or_quoted()
    {
        string[] lines = ["1. Step", "    ```", "    code", "    ```", "> ```", "> code", "> ```", "text"];

        Assert.Equal([false, true, true, true, true, true, true, false], MarkdownFences.CodeLines(lines));
    }

    [Fact]
    public void CodeLines_doesnt_open_a_block_on_backticks_with_a_backtick_after()
    {
        string[] lines = ["```a`b", "text"];

        Assert.Equal([false, false], MarkdownFences.CodeLines(lines));
    }

    [Fact]
    public void CodeLines_leaves_an_unclosed_block_open_to_the_end()
    {
        string[] lines = ["text", "```", "code"];

        Assert.Equal([false, true, true], MarkdownFences.CodeLines(lines));
    }
}
