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

        Assert.Equal("/work", target.Workspace!.FullName);
        Assert.Null(target.File);
        Assert.Null(target.Error);
    }

    [Theory]
    [InlineData("src/a.cs")]
    [InlineData("/work/src/a.cs")]
    public void A_file_inside_the_current_directory_leaves_it_as_the_workspace(string path)
    {
        var target = Resolve(path);

        Assert.Equal("/work", target.Workspace!.FullName);
        Assert.Equal("/work/src/a.cs", target.File!.FullName);
    }

    [Fact]
    public void A_file_outside_the_current_directory_re_roots_at_its_folder()
    {
        var target = Resolve("/elsewhere/README.md");

        Assert.Equal("/elsewhere", target.Workspace!.FullName);
        Assert.Equal("/elsewhere/README.md", target.File!.FullName);
    }

    [Theory]
    [InlineData("src")]
    [InlineData("src/")]
    [InlineData("/work/src")]
    public void An_existing_folder_becomes_the_workspace_with_nothing_open(string path)
    {
        var target = Resolve(path);

        Assert.Equal("/work/src", target.Workspace!.FullName);
        Assert.Null(target.File);
    }

    [Theory]
    [InlineData("today.md")]
    [InlineData("Makefile")]
    public void A_missing_file_is_created_and_opened(string path)
    {
        var target = Resolve(path);

        Assert.True(_fs.File.Exists($"/work/{path}"));
        Assert.Equal("/work", target.Workspace!.FullName);
        Assert.Equal($"/work/{path}", target.File!.FullName);
    }

    [Fact]
    public void A_missing_file_brings_its_missing_folders_with_it()
    {
        var target = Resolve("notes/2026/today.md");

        Assert.True(_fs.Directory.Exists("/work/notes/2026"));
        Assert.True(_fs.File.Exists("/work/notes/2026/today.md"));
        Assert.Equal("/work/notes/2026/today.md", target.File!.FullName);
    }

    [Fact]
    public void A_missing_path_with_a_trailing_slash_is_created_as_the_workspace_folder()
    {
        var target = Resolve("scratch/");

        Assert.True(_fs.Directory.Exists("/work/scratch"));
        Assert.Equal("/work/scratch", target.Workspace!.FullName);
        Assert.Null(target.File);
    }

    [Fact]
    public void A_path_that_cannot_be_created_is_an_error_instead_of_a_target()
    {
        var fs = new DeniedFileSystem();
        fs.AddDirectory("/work");

        var target = StartupArguments.Resolve(["hosts.new"], fs, "/work");

        Assert.Equal("tuicode: permission denied: /work/hosts.new", target.Error);
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

        Assert.Equal("/work", target.Workspace!.FullName);
        Assert.Null(target.File);
        Assert.False(_fs.File.Exists($"/work/{flag}"));
    }

    [Fact]
    public void The_value_of_driver_is_not_a_path()
    {
        var target = StartupArguments.Resolve(["--driver", "ansi"], _fs, "/work");

        Assert.Equal("/work", target.Workspace!.FullName);
        Assert.Null(target.File);
        Assert.False(_fs.File.Exists("/work/ansi"));
    }

    [Fact]
    public void A_path_alongside_flags_is_still_found()
    {
        var target = StartupArguments.Resolve(["--driver=ansi", "src/a.cs", "--smoke"], _fs, "/work");

        Assert.Equal("/work/src/a.cs", target.File!.FullName);
    }

    private StartupTarget Resolve(params string[] args) => StartupArguments.Resolve(args, _fs, "/work");
}
