using TuiCode.Workbench.Configuration;

namespace TuiCode.Tests;

// The terminal y/N asked before `tuicode <path>` creates anything (#263).
public class CreatePromptTests
{
    [Theory]
    [InlineData("y")]
    [InlineData("Y")]
    [InlineData("yes")]
    [InlineData(" yes ")]
    public void Yes_agrees(string answer) => Assert.True(Ask(answer).Agreed);

    [Theory]
    [InlineData("n")]
    [InlineData("")]
    [InlineData("yep")]
    [InlineData("ye")]
    public void Anything_else_declines(string answer) => Assert.False(Ask(answer).Agreed);

    [Fact]
    public void A_redirected_stdin_at_end_of_input_declines()
    {
        using var output = new StringWriter();

        Assert.False(CreatePrompt.Ask(TextReader.Null, output, "/work/today.md", directory: false));
    }

    [Fact]
    public void The_question_names_the_full_path_and_what_would_be_created()
    {
        Assert.Contains("/work/today.md does not exist.", Ask("n").Output);
        Assert.Contains("Create file?", Ask("n").Output);
        Assert.Contains("Create folder?", Ask("n", directory: true).Output);
    }

    private static (bool Agreed, string Output) Ask(string answer, bool directory = false)
    {
        using var input = new StringReader(answer);
        using var output = new StringWriter();
        var agreed = CreatePrompt.Ask(input, output, "/work/today.md", directory);
        return (agreed, output.ToString());
    }
}
