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

    [Fact]
    public void An_answers_tool_calls_fold_into_its_newest_with_a_count_of_the_rest()
    {
        var chat = New();
        chat.Apply(new ToolCallUpdate("a", "Read src/Services/RecipeBook.cs", "completed"));
        chat.Apply(new ToolCallUpdate("b", "Read src/Models/Recipe.cs", "completed"));
        chat.Apply(new ToolCallUpdate("c", "Read src/Program.cs", "completed"));
        chat.Apply(new ToolCallUpdate("d", "grep -rn \"Search(\" src", "pending"));
        chat.Apply(new MessageChunk(ChatRole.Agent, "Yes."));

        Assert.Equal(
        [
            (ChatLineKind.Heading, "Claude Code"),
            (ChatLineKind.Tool, "▸ grep -rn \"Search(\" src (+3)"),
            (ChatLineKind.Text, "Yes."),
        ], Rows(chat));
    }

    [Fact]
    public void A_new_call_updates_the_folded_row_instead_of_adding_one()
    {
        var chat = New();
        chat.Apply(new ToolCallUpdate("a", "Read a.cs", "pending"));
        var before = chat.Render(44).Lines.Count;
        chat.Apply(new ToolCallUpdate("b", "Read b.cs", "pending"));
        chat.Apply(new ToolCallUpdate("c", "Read c.cs", "pending"));

        Assert.Equal(before, chat.Render(44).Lines.Count);
        Assert.Equal("▸ Read c.cs (+2)", Rows(chat).Single(r => r.Item1 == ChatLineKind.Tool).Item2);
    }

    [Fact]
    public void Calls_before_text_fold_into_the_newest_after_it_and_a_failure_keeps_its_row_out_of_the_count()
    {
        var chat = New();
        chat.Apply(new ToolCallUpdate("a", "Read Program.cs", "completed"));
        chat.Apply(new ToolCallUpdate("b", "dotnet test", "failed"));
        chat.Apply(new MessageChunk(ChatRole.Agent, "The list command throws."));
        chat.Apply(new ToolCallUpdate("c", "Edit Program.cs", "completed"));
        chat.Apply(new ToolCallUpdate("d", "dotnet build", "completed"));
        chat.Apply(new ToolCallUpdate("e", "dotnet run -- list", "completed"));
        chat.Apply(new MessageChunk(ChatRole.Agent, "\n\nFixed."));

        Assert.Equal(
        [
            (ChatLineKind.Heading, "Claude Code"),
            (ChatLineKind.Tool, "▸ dotnet test (failed)"),
            (ChatLineKind.Text, "The list command throws."),
            (ChatLineKind.Tool, "▸ dotnet run -- list (+3)"),
            (ChatLineKind.Text, "Fixed."),
        ], Rows(chat));
    }

    [Fact]
    public void Each_answer_folds_its_own_calls()
    {
        var chat = New();
        chat.Apply(new ToolCallUpdate("a", "Read a.cs", "completed"));
        chat.Apply(new ToolCallUpdate("b", "Read b.cs", "completed"));
        chat.EndTurn();
        chat.AddQuestion("And?", null);
        chat.Apply(new ToolCallUpdate("c", "Read c.cs", "completed"));

        Assert.Equal(["▸ Read b.cs (+1)", "▸ Read c.cs"], Rows(chat).Where(r => r.Item1 == ChatLineKind.Tool).Select(r => r.Item2));
    }

    [Theory]
    [InlineData("Read src/Services/RecipeBook.cs", "▸ Read src/Services/Re… (+1)")]
    [InlineData("Read src/a.cs", "▸ Read src/a.cs (+1)")]
    public void A_folded_row_is_clipped_to_the_width_keeping_its_count(string title, string row)
    {
        var chat = New();
        chat.Apply(new ToolCallUpdate("a", "Read x.cs", "completed"));
        chat.Apply(new ToolCallUpdate("b", title, "completed"));

        var tool = chat.Render(28 + Conversation.Gutter).Lines.Single(l => l.Kind == ChatLineKind.Tool).Text;

        Assert.Equal(row, tool);
        Assert.True(tool.Length <= 28);
    }

    [Fact]
    public void A_failed_row_is_clipped_keeping_failed()
    {
        var chat = New();
        chat.Apply(new ToolCallUpdate("a", "dotnet test tests/TuiCode.Tests --filter Conversation", "failed"));

        Assert.Equal("▸ dotnet test tests/… (failed)", chat.Render(30 + Conversation.Gutter).Lines.Single(l => l.Kind == ChatLineKind.Tool).Text);
    }

    [Fact]
    public void In_full_every_call_has_its_row()
    {
        var chat = New();
        chat.Apply(new ToolCallUpdate("a", "Read a.cs", "completed"));
        chat.Apply(new ToolCallUpdate("b", "Read b.cs", "failed"));
        chat.Apply(new ToolCallUpdate("c", "Read c.cs", "completed"));
        var version = chat.Version;

        chat.ToolCallsInFull = true;

        Assert.NotEqual(version, chat.Version);
        Assert.Equal(["▸ Read a.cs", "▸ Read b.cs (failed)", "▸ Read c.cs"], Rows(chat).Where(r => r.Item1 == ChatLineKind.Tool).Select(r => r.Item2));
    }

    [Fact]
    public void Clearing_starts_the_next_conversation_the_way_the_setting_says()
    {
        var chat = New();
        chat.StartsInFull = true;
        chat.Clear();
        Assert.True(chat.ToolCallsInFull);

        chat.ToolCallsInFull = false;
        chat.Clear();
        Assert.True(chat.ToolCallsInFull);
    }

    [Fact]
    public void A_reloaded_answer_folds_its_calls_as_a_live_one_does()
    {
        var chat = New();
        string[] replay =
        [
            """{"sessionUpdate":"user_message_chunk","content":{"type":"text","text":"Why?"}}""",
            """{"sessionUpdate":"tool_call","toolCallId":"a","title":"Read a.cs","status":"completed"}""",
            """{"sessionUpdate":"tool_call","toolCallId":"b","title":"Read b.cs","status":"completed"}""",
            """{"sessionUpdate":"tool_call","toolCallId":"c","title":"dotnet test","status":"failed"}""",
            """{"sessionUpdate":"agent_thought_chunk","content":{"type":"text","text":"Hmm"}}""",
            """{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Because."}}""",
        ];
        foreach (var update in replay)
            if (SessionUpdate.Parse(System.Text.Json.Nodes.JsonNode.Parse(update)) is { } parsed) chat.Apply(parsed);

        Assert.Equal(
        [
            (ChatLineKind.Heading, "You"), (ChatLineKind.Text, "Why?"), (ChatLineKind.Blank, ""),
            (ChatLineKind.Heading, "Claude Code"),
            (ChatLineKind.Tool, "▸ Read b.cs (+1)"),
            (ChatLineKind.Tool, "▸ dotnet test (failed)"),
            (ChatLineKind.Text, "Because."),
        ], Rows(chat));
    }

    [Fact]
    public void The_changed_files_note_is_never_folded()
    {
        var chat = New();
        chat.Apply(new ToolCallUpdate("a", "Edit a.cs", "completed", "edit"));
        chat.Apply(new ToolCallUpdate("b", "Edit b.cs", "completed", "edit"));
        chat.AddNote("Changed 2 files · Ctrl+R to review");

        Assert.Equal((ChatLineKind.Note, "Changed 2 files · Ctrl+R to review"), Rows(chat).Last());
    }
}
