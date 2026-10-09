using TuiCode.Workbench.Languages;
using TuiCode.Workbench.References;

namespace TuiCode.Tests;

public class UsageTreeTests
{
    private static readonly string Root = Path.GetFullPath("/work");

    private static readonly Dictionary<string, string[]> Text = new()
    {
        [Full("src/Editor.cs")] = [.. Enumerable.Range(1, 120).Select(i => i == 112 ? "\t_markers = LineDiff.Hunks(_baseline);  " : "")],
        [Full("src/History.cs")] = ["", "", "    var hunks = LineDiff.Hunks(a, b);", "", "", "    foreach (var h in LineDiff.Hunks(a, b))"],
        [Full("tests/History.cs")] = ["", "LineDiff.Hunks();"],
    };

    [Fact]
    public void Build_groups_by_file_in_path_order_and_lines_in_order_with_the_numbers_aligned()
    {
        SourceLocation[] usages =
        [
            new(Full("src/History.cs"), 5, 22),
            new(Full("src/Editor.cs"), 111, 24),
            new(Full("src/History.cs"), 2, 25),
        ];

        var files = UsageTree.Build(usages, path => Text[path], Root);

        Assert.Equal(["Editor.cs", "History.cs"], files.Select(f => f.Name));
        Assert.Equal([1, 2], files.Select(f => f.Count));
        Assert.Equal(
            ["112  _markers = LineDiff.Hunks(_baseline);", "  3  var hunks = LineDiff.Hunks(a, b);", "  6  foreach (var h in LineDiff.Hunks(a, b))"],
            files.SelectMany(f => f.Children).Select(u => u.ToString()));
        Assert.Equal(new SourceLocation(Full("src/History.cs"), 2, 25), ((UsageLineNode)files[1].Children[0]).Location);
    }

    [Fact]
    public void Files_that_share_a_name_are_told_apart_by_their_path()
    {
        SourceLocation[] usages = [new(Full("tests/History.cs"), 1, 9), new(Full("src/History.cs"), 2, 25), new(Full("src/Editor.cs"), 111, 24)];

        var files = UsageTree.Build(usages, path => Text[path], Root);

        Assert.Equal(["Editor.cs", Path.Combine("src", "History.cs"), Path.Combine("tests", "History.cs")], files.Select(f => f.Name));
    }

    [Fact]
    public void Each_file_is_read_once()
    {
        var reads = new List<string>();
        SourceLocation[] usages = [new(Full("src/History.cs"), 2, 25), new(Full("src/History.cs"), 5, 22)];

        UsageTree.Build(usages, path => { reads.Add(path); return Text[path]; }, Root);

        Assert.Equal([Full("src/History.cs")], reads);
    }

    [Fact]
    public void Count_counts_the_usages_and_files()
    {
        var files = UsageTree.Build([new(Full("src/History.cs"), 2, 25), new(Full("src/History.cs"), 5, 22), new(Full("src/Editor.cs"), 111, 24)], path => Text[path], Root);
        var one = UsageTree.Build([new(Full("src/History.cs"), 2, 25)], path => Text[path], Root);

        Assert.Equal("3 in 2 files", UsageTree.Count(files));
        Assert.Equal("1 in 1 file", UsageTree.Count(one));
    }

    private static string Full(string relative) => Path.GetFullPath(Path.Combine(Root, relative));
}
