using System.Text.Json.Nodes;
using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

public class LspRenameTests
{
    private static readonly string A = Path.GetFullPath("/work/A.cs");
    private static readonly string B = Path.GetFullPath("/work/B.cs");

    [Fact]
    public void ParseWorkspaceEdit_reads_changes_by_file()
    {
        var edits = LspRename.ParseWorkspaceEdit(new JsonObject
        {
            ["changes"] = new JsonObject
            {
                [LspLocations.ToUri(A)] = new JsonArray(Edit(1, 4, 1, 9, "Total"), Edit(3, 0, 3, 5, "Total")),
                [LspLocations.ToUri(B)] = new JsonArray(Edit(0, 2, 0, 7, "Total")),
            },
        });

        Assert.Equal(
        [
            (A, new TextEdit[] { new(1, 4, 1, 9, "Total"), new(3, 0, 3, 5, "Total") }),
            (B, new TextEdit[] { new(0, 2, 0, 7, "Total") }),
        ], edits.Select(f => (f.Path, f.Edits.ToArray())));
    }

    [Fact]
    public void ParseWorkspaceEdit_prefers_documentChanges_and_leaves_out_file_operations()
    {
        var edits = LspRename.ParseWorkspaceEdit(new JsonObject
        {
            ["changes"] = new JsonObject { [LspLocations.ToUri(B)] = new JsonArray(Edit(0, 0, 0, 1, "x")) },
            ["documentChanges"] = new JsonArray(
                new JsonObject { ["kind"] = "rename", ["oldUri"] = LspLocations.ToUri(A), ["newUri"] = LspLocations.ToUri(B) },
                new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = LspLocations.ToUri(A), ["version"] = 3 },
                    ["edits"] = new JsonArray(Edit(2, 1, 2, 4, "Sum")),
                },
                new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = LspLocations.ToUri(A), ["version"] = 3 },
                    ["edits"] = new JsonArray(Edit(5, 1, 5, 4, "Sum")),
                },
                new JsonObject { ["kind"] = "delete", ["uri"] = LspLocations.ToUri(B) }),
        });

        var file = Assert.Single(edits);
        Assert.Equal(A, file.Path);
        Assert.Equal([new TextEdit(2, 1, 2, 4, "Sum"), new TextEdit(5, 1, 5, 4, "Sum")], file.Edits);
    }

    [Fact]
    public void ParseWorkspaceEdit_of_null_is_no_edits() => Assert.Empty(LspRename.ParseWorkspaceEdit(null));

    [Fact]
    public void ParsePrepare_reads_a_range_with_or_without_a_placeholder_and_the_default_behaviour()
    {
        Assert.Equal(new RenameTarget(new TextEdit(1, 2, 1, 6, ""), null), LspRename.ParsePrepare(Range(1, 2, 1, 6)));
        Assert.Equal(new RenameTarget(new TextEdit(1, 2, 1, 6, ""), "Hunk"),
            LspRename.ParsePrepare(new JsonObject { ["range"] = Range(1, 2, 1, 6), ["placeholder"] = "Hunk" }));
        Assert.Equal(new RenameTarget(null, null), LspRename.ParsePrepare(new JsonObject { ["defaultBehavior"] = true }));
    }

    [Fact]
    public void ParsePrepare_of_null_means_nothing_here_can_be_renamed() => Assert.Null(LspRename.ParsePrepare(null));

    private static JsonObject Edit(int startLine, int startCharacter, int endLine, int endCharacter, string text) =>
        new() { ["range"] = Range(startLine, startCharacter, endLine, endCharacter), ["newText"] = text };

    private static JsonObject Range(int startLine, int startCharacter, int endLine, int endCharacter) => new()
    {
        ["start"] = new JsonObject { ["line"] = startLine, ["character"] = startCharacter },
        ["end"] = new JsonObject { ["line"] = endLine, ["character"] = endCharacter },
    };
}
