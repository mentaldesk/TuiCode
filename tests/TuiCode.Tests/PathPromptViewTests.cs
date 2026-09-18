using TuiCode.Workbench.Files;
using static TuiCode.Workbench.Files.PathPromptView;

namespace TuiCode.Tests;

public class PathPromptViewTests
{
    [Theory]
    [InlineData("src/widget.test.cs", false, NamePart.Stem, "widget.test")]
    [InlineData("src/widget.test.cs", false, NamePart.Name, "widget.test.cs")]
    [InlineData("src/widget.test.cs", false, NamePart.Extension, "cs")]
    [InlineData("src/.gitignore", false, NamePart.Stem, ".gitignore")]
    [InlineData("Makefile", false, NamePart.Stem, "Makefile")]
    [InlineData("src/v1.2", true, NamePart.Stem, "v1.2")]
    [InlineData(@"src\lib\a.cs", false, NamePart.Stem, "a")]
    public void NameSpan_covers_the_requested_part_of_the_last_segment(string path, bool isDirectory, NamePart part, string expected)
    {
        var (start, length) = NameSpan(path, isDirectory, part);

        Assert.Equal(expected, path.Substring(start, length));
    }

    [Fact]
    public void SelectName_selects_the_name_without_its_extension()
    {
        using var view = new PathPromptView("Move or Rename", "Path", "src/widget.cs");

        view.SelectName(isDirectory: false);

        Assert.Equal("widget", view.SelectedText);
    }

    [Fact]
    public void F2_cycles_the_selection_through_the_name_the_extension_and_back()
    {
        using var view = new PathPromptView("Move or Rename", "Path", "src/widget.cs");
        view.SelectName(isDirectory: false);
        var seen = new List<string>();

        for (var i = 0; i < 3; i++)
        {
            view.Scope.Handle(Key.F2);
            seen.Add(view.SelectedText);
        }

        Assert.Equal(["widget.cs", "cs", "widget"], seen);
    }
}
