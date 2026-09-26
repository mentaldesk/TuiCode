using TuiCode.Workbench.Configuration;

namespace TuiCode.Tests;

// `tuicode <path>` (#263), decided before anything is drawn.
public class StartupArgumentsTests
{
    private readonly MockFileSystem _fs = new();

    public StartupArgumentsTests()
    {
        _fs.AddDirectory("/work");
        _fs.AddDirectory("/work/src");
        _fs.AddFile("/work/src/a.cs", new MockFileData("class A;"));
        _fs.AddFile("/elsewhere/README.md", new MockFileData("# hi"));
    }

    [Fact]
    public void No_arguments_open_the_current_directory()
    {
        var target = Resolve();

        Assert.Equal(Full("/work"), target.Workspace!.FullName);
        Assert.Null(target.File);
        Assert.Null(target.Error);
    }

    [Theory]
    [InlineData("src/a.cs")]
    [InlineData("/work/src/a.cs")]
    public void A_file_inside_the_current_directory_leaves_it_as_the_workspace(string path)
    {
        var target = Resolve(path);

        Assert.Equal(Full("/work"), target.Workspace!.FullName);
        Assert.Equal(Full("/work/src/a.cs"), target.File!.FullName);
    }

    [Fact]
    public void A_file_outside_the_current_directory_re_roots_at_its_folder()
    {
        var target = Resolve("/elsewhere/README.md");

        Assert.Equal(Full("/elsewhere"), target.Workspace!.FullName);
        Assert.Equal(Full("/elsewhere/README.md"), target.File!.FullName);
    }

    [Theory]
    [InlineData("src")]
    [InlineData("src/")]
    [InlineData("/work/src")]
    public void An_existing_folder_becomes_the_workspace_with_nothing_open(string path)
    {
        var target = Resolve(path);

        Assert.Equal(Full("/work/src"), target.Workspace!.FullName);
        Assert.Null(target.File);
    }

    [Theory]
    [InlineData("today.md")]
    [InlineData("Makefile")]
    public void A_missing_file_is_created_and_opened_once_you_agree(string path)
    {
        var target = Resolve(path);

        Assert.True(_fs.File.Exists($"/work/{path}"));
        Assert.Equal(Full("/work"), target.Workspace!.FullName);
        Assert.Equal(Full($"/work/{path}"), target.File!.FullName);
    }

    [Theory]
    [InlineData("today.md")]
    [InlineData("scratch/")]
    public void Declining_creates_nothing_and_starts_nothing(string path)
    {
        var target = StartupArguments.Resolve([path], _fs, "/work", Decline);

        Assert.True(target.Declined);
        Assert.Null(target.Workspace);
        Assert.Null(target.File);
        Assert.Null(target.Error);
        Assert.False(_fs.File.Exists("/work/today.md"));
        Assert.False(_fs.Directory.Exists("/work/scratch"));
    }

    [Fact]
    public void An_existing_path_is_opened_without_asking()
    {
        var target = StartupArguments.Resolve(["src/a.cs"], _fs, "/work", Decline);

        Assert.Equal(Full("/work/src/a.cs"), target.File!.FullName);
        Assert.False(target.Declined);
    }

    [Fact]
    public void What_you_are_asked_to_create_is_the_full_path_and_its_kind()
    {
        (string Path, bool Directory)? asked = null;

        StartupArguments.Resolve(["notes/today.md"], _fs, "/work", (path, directory) =>
        {
            asked = (path, directory);
            return false;
        });

        Assert.Equal((Full("/work/notes/today.md"), false), asked);

        StartupArguments.Resolve(["notes/"], _fs, "/work", (path, directory) =>
        {
            asked = (path, directory);
            return false;
        });

        Assert.Equal((Full("/work/notes/"), true), asked);
    }

    [Fact]
    public void A_missing_file_brings_its_missing_folders_with_it()
    {
        var target = Resolve("notes/2026/today.md");

        Assert.True(_fs.Directory.Exists("/work/notes/2026"));
        Assert.True(_fs.File.Exists("/work/notes/2026/today.md"));
        Assert.Equal(Full("/work/notes/2026/today.md"), target.File!.FullName);
    }

    [Fact]
    public void A_missing_path_with_a_trailing_slash_is_created_as_the_workspace_folder()
    {
        var target = Resolve("scratch/");

        Assert.True(_fs.Directory.Exists("/work/scratch"));
        Assert.Equal(Full("/work/scratch"), target.Workspace!.FullName);
        Assert.Null(target.File);
    }

    [Fact]
    public void A_path_that_cannot_be_created_is_an_error_instead_of_a_target()
    {
        var fs = new DeniedFileSystem();
        fs.AddDirectory("/work");

        var target = StartupArguments.Resolve(["hosts.new"], fs, "/work", Agree);

        Assert.Equal($"tuicode: permission denied: {fs.Path.GetFullPath("/work/hosts.new")}", target.Error);
        Assert.Null(target.Workspace);
        Assert.Null(target.File);
    }

    [Theory]
    [InlineData("--smoke")]
    [InlineData("--smoke-syntax")]
    [InlineData("--list-terminal-integrations")]
    [InlineData("--not-a-flag-we-know")]
    public void A_flag_is_never_a_path(string flag)
    {
        var target = Resolve(flag);

        Assert.Equal(Full("/work"), target.Workspace!.FullName);
        Assert.Null(target.File);
        Assert.False(_fs.File.Exists($"/work/{flag}"));
    }

    [Fact]
    public void The_value_of_driver_is_not_a_path()
    {
        var target = StartupArguments.Resolve(["--driver", "ansi"], _fs, "/work", Agree);

        Assert.Equal(Full("/work"), target.Workspace!.FullName);
        Assert.Null(target.File);
        Assert.False(_fs.File.Exists("/work/ansi"));
    }

    [Fact]
    public void A_path_alongside_flags_is_still_found()
    {
        var target = StartupArguments.Resolve(["--driver=ansi", "src/a.cs", "--smoke"], _fs, "/work", Agree);

        Assert.Equal(Full("/work/src/a.cs"), target.File!.FullName);
    }

    [Theory]
    [InlineData("src/a.cs:42", 42, 1)]
    [InlineData("src/a.cs:42:9", 42, 9)]
    public void A_position_on_the_path_rides_along_with_the_file(string path, int line, int column)
    {
        var target = Resolve(path);

        Assert.Equal(Full("/work/src/a.cs"), target.File!.FullName);
        Assert.Equal(new FilePosition(line, column), target.Position);
    }

    [Fact]
    public void A_position_on_a_folder_is_dropped()
    {
        var target = Resolve("src:42");

        Assert.Equal(Full("/work/src"), target.Workspace!.FullName);
        Assert.Null(target.File);
        Assert.Null(target.Position);
    }

    [Fact]
    public void A_path_that_has_to_be_created_is_created_as_typed()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A colon can't be in a Windows file name, so the rule can't apply there");

        var target = Resolve("src/new.cs:42");

        Assert.Equal(Full("/work/src/new.cs:42"), target.File!.FullName);
        Assert.False(_fs.File.Exists(Full("/work/src/new.cs")));
        Assert.Null(target.Position);
    }

    private static bool Agree(string path, bool directory) => true;

    private static bool Decline(string path, bool directory) => false;

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private StartupTarget Resolve(params string[] args) => StartupArguments.Resolve(args, _fs, "/work", Agree);
}
