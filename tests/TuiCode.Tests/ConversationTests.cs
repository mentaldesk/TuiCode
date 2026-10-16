using TuiCode.Workbench.Assistant;

namespace TuiCode.Tests;

public class ConversationTests
{
    private static Conversation New() => new() { AgentName = "Claude Code", Root = "/work" };

    private static IEnumerable<(ChatLineKind, string)> Rows(Conversation conversation, int width = 40 + Conversation.Gutter) =>
        conversation.Render(width).Lines.Select(l => (l.Kind, l.Text));

    [Fact]
    public void A_question_and_its_streamed_answer_read_under_their_headings()
    {
        var chat = New();
        chat.AddQuestion("Why?", "lines 41–58 of a.cs");
        chat.Apply(new MessageChunk(ChatRole.Agent, "Because "));
        chat.Apply(new MessageChunk(ChatRole.Agent, "it does."));

        Assert.Equal(
        [
            (ChatLineKind.Heading, "You · lines 41–58 of a.cs"), (ChatLineKind.Text, "Why?"), (ChatLineKind.Blank, ""),
            (ChatLineKind.Heading, "Claude Code"), (ChatLineKind.Text, "Because it does."),
        ], Rows(chat));
    }

    [Fact]
    public void A_tool_call_is_one_line_updated_in_place()
    {
        var chat = New();
        chat.Apply(new ToolCallUpdate("t", "Read", "pending"));
        chat.Apply(new ToolCallUpdate("t", "Read EditorTextView.cs", "completed"));
        chat.Apply(new ToolCallUpdate("u", "Searched \"ContentsChanged\"", "failed"));

        Assert.Equal(
        [
            (ChatLineKind.Heading, "Claude Code"),
            (ChatLineKind.Tool, "▸ Read EditorTextView.cs"),
            (ChatLineKind.Tool, "▸ Searched \"ContentsChanged\" (failed)"),
        ], Rows(chat));
    }

    [Fact]
    public void Long_text_wraps_at_the_width()
    {
        var chat = New();
        chat.Apply(new MessageChunk(ChatRole.Agent, "one two three four five"));

        Assert.Equal(["Claude Code", "one two", "three four", "five"], Rows(chat, 12 + Conversation.Gutter).Select(r => r.Item2));
    }

    [Fact]
    public void A_fenced_block_is_code_rows_of_its_own_block_and_language()
    {
        var chat = New();
        chat.Apply(new MessageChunk(ChatRole.Agent, "See:\n```csharp\nvar x = 1;\n\tReturn();\n```\nDone."));

        var (lines, blocks) = chat.Render(40);

        Assert.Equal([new CodeBlock("csharp", ["var x = 1;", "    Return();"])], blocks, (a, b) => a.Language == b.Language && a.Lines.SequenceEqual(b.Lines));
        Assert.Equal(
        [
            (ChatLineKind.Heading, "Claude Code"), (ChatLineKind.Text, "See:"),
            (ChatLineKind.Code, "var x = 1;"), (ChatLineKind.Code, "    Return();"), (ChatLineKind.Text, "Done."),
        ], lines.Select(l => (l.Kind, l.Text)));
        Assert.Equal((0, 1), (lines[3].Block, lines[3].Line));
    }

    [Fact]
    public void A_wrapped_code_line_carries_the_offset_each_row_starts_at()
    {
        var chat = New();
        chat.Apply(new MessageChunk(ChatRole.Agent, "```\nabcdefghij\n```"));

        var code = chat.Render(4 + Conversation.Gutter).Lines.Where(l => l.Kind == ChatLineKind.Code).Select(l => (l.Text, l.Offset));

        Assert.Equal([("abcd", 0), ("efgh", 4), ("ij", 8)], code);
    }

    [Fact]
    public void Each_side_leaves_a_gutter_and_says_whose_rows_they_are()
    {
        var chat = New();
        chat.AddQuestion("aaaaa bbbbb", null);
        chat.Apply(new MessageChunk(ChatRole.Agent, "ccccc ddddd"));
        chat.AddNote("Stopped.");

        var lines = chat.Render(11 + Conversation.Gutter).Lines;

        Assert.Equal(
        [
            ("You", ChatRole.User), ("aaaaa bbbbb", ChatRole.User), ("", null),
            ("Claude Code", ChatRole.Agent), ("ccccc ddddd", ChatRole.Agent), ("", null),
            ("Stopped.", null),
        ], lines.Select(l => (l.Text, l.Role)));
        Assert.Equal(["aaaaa", "bbbbb"], chat.Render(10 + Conversation.Gutter).Lines.Where(l => l.Role == ChatRole.User).Skip(1).Select(l => l.Text));
    }

    [Fact]
    public void A_reloaded_question_takes_its_context_label_from_the_attached_lines()
    {
        var chat = New();
        chat.Apply(new MessageChunk(ChatRole.User, "Why?"));
        chat.Apply(new ContextChunk(ChatRole.User, "file:///work/src/a.cs#L41-58"));

        Assert.Equal("You · lines 41–58 of src/a.cs", Rows(chat).First().Item2);
    }

    [Fact]
    public void A_note_stands_apart_and_an_answer_after_it_gets_its_own_heading()
    {
        var chat = New();
        chat.Apply(new MessageChunk(ChatRole.Agent, "Partial"));
        chat.AddNote("Stopped.");
        chat.Apply(new MessageChunk(ChatRole.Agent, "More"));

        Assert.Equal(["Claude Code", "Partial", "", "Stopped.", "", "Claude Code", "More"], Rows(chat).Select(r => r.Item2));
    }
}
