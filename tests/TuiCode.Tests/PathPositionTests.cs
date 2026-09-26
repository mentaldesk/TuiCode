using TuiCode.Workbench.Configuration;

namespace TuiCode.Tests;

// Telling `path:line[:column]` apart from a name that happens to contain a colon (#265).
public class PathPositionTests
{
    private readonly MockFileSystem _fs = new();

    public PathPositionTests()
    {
        _fs.AddDirectory("/work");
        _fs.AddDirectory("/work/src");
        _fs.AddFile("/work/src/a.cs", new MockFileData("class A;"));
    }

    [Theory]
    [InlineData("src/a.cs:42", "src/a.cs", 42, 1)]
    [InlineData("src/a.cs:42:9", "src/a.cs", 42, 9)]
    [InlineData("src/a.cs:42:", "src/a.cs", 42, 1)] // what `grep -n` prints, pasted whole
    [InlineData("missing.cs:7", "missing.cs", 7, 1)]
    public void A_numeric_suffix_is_a_position(string argument, string path, int line, int column)
    {
        var (parsed, position) = Split(argument);

        Assert.Equal(path, parsed);
        Assert.Equal(new FilePosition(line, column), position);
    }

    [Theory]
    [InlineData("src/a.cs")]
    [InlineData("missing.cs:abc")]
    [InlineData("missing.cs:-1")]
    [InlineData(@"C:\src\Foo.cs")]
    public void Anything_that_is_not_a_number_stays_part_of_the_name(string argument)
    {
        var (parsed, position) = Split(argument);

        Assert.Equal(argument, parsed);
        Assert.Null(position);
    }

    [Fact]
    public void A_line_of_zero_is_no_position_rather_than_an_error()
    {
        var (parsed, position) = Split("missing.cs:0");

        Assert.Equal("missing.cs", parsed);
        Assert.Null(position);
    }

    [Fact]
    public void A_file_whose_name_really_ends_in_a_number_after_a_colon_wins()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A colon can't be in a Windows file name, so the rule can't apply there");
        _fs.AddFile("/work/weird:42", new MockFileData("odd"));

        var (parsed, position) = Split("weird:42");

        Assert.Equal("weird:42", parsed);
        Assert.Null(position);
    }

    [Fact]
    public void A_file_whose_name_contains_a_colon_opens_as_itself()
    {
        _fs.AddFile("/work/weird:name.cs", new MockFileData("odd"));

        var (parsed, position) = Split("weird:name.cs");

        Assert.Equal("weird:name.cs", parsed);
        Assert.Null(position);
    }

    [Fact]
    public void A_windows_drive_letter_keeps_its_colon_even_with_a_position_after_it()
    {
        var (parsed, position) = Split(@"C:\src\Foo.cs:42");

        Assert.Equal(@"C:\src\Foo.cs", parsed);
        Assert.Equal(new FilePosition(42, 1), position);
    }

    [Fact]
    public void A_position_on_a_folder_is_read_but_the_folder_is_what_opens()
    {
        var (parsed, position) = Split("src:42");

        Assert.Equal("src", parsed);
        Assert.Equal(new FilePosition(42, 1), position);
        Assert.True(_fs.Directory.Exists(_fs.Path.GetFullPath("/work/src")));
    }

    private (string Path, FilePosition? Position) Split(string argument) =>
        PathPosition.Split(argument, _fs, "/work");
}
