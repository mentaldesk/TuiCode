using System.Text.Json.Nodes;
using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

public class LspCompletionTests
{
    [Fact]
    public void Parse_reads_a_list_its_items_and_whether_it_is_incomplete()
    {
        var list = LspCompletion.Parse(JsonNode.Parse("""
            { "isIncomplete": true, "items": [
              { "label": "Append", "kind": 2, "detail": "StringBuilder Append(string?)", "sortText": "0001", "filterText": "Append" },
              { "label": "Length", "kind": 10 }
            ] }
            """));

        Assert.True(list.IsIncomplete);
        Assert.Equal(
            [new CompletionItem("Append", 2, "StringBuilder Append(string?)", "Append", "0001"), new CompletionItem("Length", 10)],
            list.Items);
    }

    [Fact]
    public void Parse_reads_a_bare_array_as_complete_and_skips_items_without_a_label()
    {
        var list = LspCompletion.Parse(JsonNode.Parse("""[{ "label": "Go" }, { "kind": 2 }]"""));

        Assert.False(list.IsIncomplete);
        Assert.Equal(["Go"], list.Items.Select(i => i.Label));
    }

    [Fact]
    public void Parse_takes_a_text_edits_range_or_an_insert_replace_edits_insert_range()
    {
        var list = LspCompletion.Parse(JsonNode.Parse("""
            [
              { "label": "Append", "textEdit": { "range": { "start": { "line": 3, "character": 7 }, "end": { "line": 3, "character": 9 } }, "newText": "Append" } },
              { "label": "Length", "textEdit": {
                  "insert": { "start": { "line": 3, "character": 7 }, "end": { "line": 3, "character": 9 } },
                  "replace": { "start": { "line": 3, "character": 7 }, "end": { "line": 3, "character": 12 } }, "newText": "Length" } },
              { "label": "Lines", "textEdit": { "range": { "start": { "line": 3, "character": 7 }, "end": { "line": 4, "character": 0 } }, "newText": "Lines" } }
            ]
            """));

        Assert.Equal(new CompletionEdit(3, 7, 9, "Append"), list.Items[0].Edit);
        Assert.Equal(new CompletionEdit(3, 7, 9, "Length"), list.Items[1].Edit);
        Assert.Null(list.Items[2].Edit);
    }

    [Fact]
    public void Parse_falls_back_to_the_lists_default_edit_range()
    {
        var list = LspCompletion.Parse(JsonNode.Parse("""
            { "itemDefaults": { "editRange": { "start": { "line": 0, "character": 4 }, "end": { "line": 0, "character": 6 } } },
              "items": [{ "label": "print", "textEditText": "print" }] }
            """));

        Assert.Equal(new CompletionEdit(0, 4, 6, "print"), list.Items[0].Edit);
    }

    [Fact]
    public void Parse_turns_a_snippet_into_plain_text()
    {
        var list = LspCompletion.Parse(JsonNode.Parse("""
            [{ "label": "Add", "insertTextFormat": 2, "insertText": "Add(${1:item})$0" }]
            """));

        Assert.Equal("Add(item)", list.Items[0].Text);
    }

    [Fact]
    public void Parse_of_null_is_empty() => Assert.Empty(LspCompletion.Parse(null).Items);

    [Fact]
    public void Trigger_characters_come_from_the_completion_provider()
    {
        var capabilities = JsonNode.Parse("""{ "completionProvider": { "triggerCharacters": [".", "::", ""] } }""");

        Assert.Equal(new HashSet<string> { ".", "::" }, LspCompletion.TriggerCharacters(capabilities));
        Assert.Empty(LspCompletion.TriggerCharacters(JsonNode.Parse("{}")));
    }
}
