using System.Text;
using TuiCode.Editor;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Rename;

namespace TuiCode.Tests;

public class WorkspaceRenameTests
{
    private readonly MockFileSystem _fs = new();

    private string Full(string path) => _fs.Path.GetFullPath(path);

    [Fact]
    public void Plan_splits_the_files_into_open_and_closed_and_counts_the_changes()
    {
        _fs.AddFile("/work/A.cs", new MockFileData("int Sum;"));
        _fs.AddFile("/work/B.cs", new MockFileData("Sum(); Sum();"));
        _fs.AddFile("/work/C.cs", new MockFileData("Sum();"));

        var rename = WorkspaceRename.Plan(
        [
            new FileEdit(Full("/work/A.cs"), [new(0, 4, 0, 7, "Total")]),
            new FileEdit(Full("/work/B.cs"), [new(0, 0, 0, 3, "Total"), new(0, 7, 0, 10, "Total")]),
            new FileEdit(Full("/work/C.cs"), [new(0, 0, 0, 3, "Total")]),
            new FileEdit(Full("/work/Gone.cs"), [new(0, 0, 0, 3, "Total")]),
        ], new Dictionary<string, string> { [Full("/work/A.cs")] = "int Sum;" }, _fs);

        Assert.Equal([Full("/work/A.cs")], rename.Open.Select(f => f.Path));
        Assert.Equal([Full("/work/B.cs"), Full("/work/C.cs")], rename.Closed.Select(f => f.Path));
        Assert.Equal(4, rename.Changes);
        Assert.Equal(3, rename.Files);
    }

    [Fact]
    public void Apply_edits_an_open_tab_as_one_undo_step_and_leaves_it_unsaved()
    {
        _fs.AddFile("/work/A.cs", new MockFileData("class A\n{\n    int Sum() => 1;\n    int Twice() => Sum() + Sum();\n}\n"));
        using var tab = new EditorTab(_fs.FileInfo.New("/work/A.cs"));
        var rename = Plan(tab, [new(2, 8, 2, 11, "Total"), new(3, 19, 3, 22, "Total"), new(3, 27, 3, 30, "Total")]);

        var result = rename.Apply([tab], _fs);

        Assert.Equal(["class A", "{", "    int Total() => 1;", "    int Twice() => Total() + Total();", "}", ""], tab.Lines);
        Assert.True(tab.IsDirty);
        Assert.Equal("class A\n{\n    int Sum() => 1;\n    int Twice() => Sum() + Sum();\n}\n", _fs.File.ReadAllText("/work/A.cs"));
        Assert.Equal(3, result.Changes);
        Assert.Empty(result.Skipped);

        tab.SubViews.OfType<CodeEditor>().Single().Undo();

        Assert.Equal(["class A", "{", "    int Sum() => 1;", "    int Twice() => Sum() + Sum();", "}", ""], tab.Lines);
    }

    [Fact]
    public void Apply_leaves_a_tab_edited_since_the_server_was_asked_alone()
    {
        _fs.AddFile("/work/A.cs", new MockFileData("int Sum;"));
        using var tab = new EditorTab(_fs.FileInfo.New("/work/A.cs"));
        var rename = Plan(tab, [new(0, 4, 0, 7, "Total")]);
        tab.ReplaceLines(0, 1, ["int  Sum;"]);

        var result = rename.Apply([tab], _fs);

        Assert.Equal(["int  Sum;"], tab.Lines);
        Assert.Equal([Full("/work/A.cs")], result.Skipped);
        Assert.Empty(result.Applied);
    }

    [Fact]
    public void Apply_writes_a_closed_file_keeping_its_BOM_and_line_endings()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        _fs.AddFile("/work/B.cs", new MockFileData([.. bom, .. Encoding.UTF8.GetBytes("int Sum;\r\nSum();\r\n")]));
        var rename = WorkspaceRename.Plan([new FileEdit(Full("/work/B.cs"), [new(0, 4, 0, 7, "Total"), new(1, 0, 1, 3, "Total")])],
            new Dictionary<string, string>(), _fs);

        var result = rename.Apply([], _fs);

        Assert.Equal([.. bom, .. Encoding.UTF8.GetBytes("int Total;\r\nTotal();\r\n")], _fs.File.ReadAllBytes("/work/B.cs"));
        Assert.Equal(2, result.Changes);
    }

    [Fact]
    public void Apply_leaves_a_closed_file_that_changed_on_disk_since_the_server_answered_as_it_is()
    {
        _fs.AddFile("/work/B.cs", new MockFileData("int Sum;"));
        var rename = WorkspaceRename.Plan([new FileEdit(Full("/work/B.cs"), [new(0, 4, 0, 7, "Total")])], new Dictionary<string, string>(), _fs);
        _fs.File.WriteAllText("/work/B.cs", "// moved\nint Sum;");

        var result = rename.Apply([], _fs);

        Assert.Equal("// moved\nint Sum;", _fs.File.ReadAllText("/work/B.cs"));
        Assert.Equal([Full("/work/B.cs")], result.Skipped);
    }

    [Fact]
    public void Apply_leaves_a_closed_file_whose_edit_falls_outside_it_as_it_is()
    {
        _fs.AddFile("/work/B.cs", new MockFileData("int Sum;"));
        var rename = WorkspaceRename.Plan([new FileEdit(Full("/work/B.cs"), [new(3, 0, 3, 3, "Total")])], new Dictionary<string, string>(), _fs);

        var result = rename.Apply([], _fs);

        Assert.Equal("int Sum;", _fs.File.ReadAllText("/work/B.cs"));
        Assert.Equal([Full("/work/B.cs")], result.Skipped);
    }

    [Fact]
    public void ChangedLines_has_one_place_per_changed_line()
    {
        var result = new RenameResult([new FileEdit("/work/A.cs", [new(3, 19, 3, 22, "Total"), new(3, 27, 3, 30, "Total"), new(2, 8, 2, 11, "Total")])], []);

        Assert.Equal([new SourceLocation("/work/A.cs", 3, 19), new SourceLocation("/work/A.cs", 2, 8)], result.ChangedLines());
        Assert.Equal(3, result.Changes);
    }

    private WorkspaceRename Plan(EditorTab tab, IReadOnlyList<TextEdit> edits) =>
        WorkspaceRename.Plan([new FileEdit(tab.File.FullName, edits)],
            new Dictionary<string, string> { [tab.File.FullName] = string.Join('\n', tab.Lines) }, _fs);
}
