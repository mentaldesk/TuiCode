using System.IO.Abstractions;
using TuiCode.Workbench.Configuration;

namespace TuiCode.Tests;

// `tuicode <path>...` (#263, #266), decided before anything is drawn.
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
        Assert.Empty(target.Files);
        Assert.Null(target.Error);
    }

    [Theory]
    [InlineData("src/a.cs")]
    [InlineData("/work/src/a.cs")]
    public void A_file_inside_the_current_directory_leaves_it_as_the_workspace(string path)
    {
        var target = Resolve(path);

        Assert.Equal(Full("/work"), target.Workspace!.FullName);
        Assert.Equal(Full("/work/src/a.cs"), Single(target).FullName);
    }

    [Fact]
    public void A_file_outside_the_current_directory_re_roots_at_its_folder()
    {
        var target = Resolve("/elsewhere/README.md");

        Assert.Equal(Full("/elsewhere"), target.Workspace!.FullName);
        Assert.Equal(Full("/elsewhere/README.md"), Single(target).FullName);
    }

    [Theory]
    [InlineData("src")]
    [InlineData("src/")]
    [InlineData("/work/src")]
    public void An_existing_folder_becomes_the_workspace_with_nothing_open(string path)
    {
        var target = Resolve(path);

        Assert.Equal(Full("/work/src"), target.Workspace!.FullName);
        Assert.Empty(target.Files);
    }

    [Theory]
    [InlineData("today.md")]
    [InlineData("Makefile")]
    public void A_missing_file_is_created_and_opened_once_you_agree(string path)
    {
        var target = Resolve(path);

        Assert.True(_fs.File.Exists($"/work/{path}"));
        Assert.Equal(Full("/work"), target.Workspace!.FullName);
        Assert.Equal(Full($"/work/{path}"), Single(target).FullName);
    }

    [Theory]
    [InlineData("today.md")]
    [InlineData("scratch/")]
    public void Declining_creates_nothing_and_starts_nothing(string path)
    {
        var target = StartupArguments.Resolve([path], _fs, "/work", Decline);

        Assert.True(target.Declined);
        Assert.Null(target.Workspace);
        Assert.Empty(target.Files);
        Assert.Null(target.Error);
        Assert.False(_fs.File.Exists("/work/today.md"));
        Assert.False(_fs.Directory.Exists("/work/scratch"));
    }

    [Fact]
    public void An_existing_path_is_opened_without_asking()
    {
        var target = StartupArguments.Resolve(["src/a.cs"], _fs, "/work", Decline);

        Assert.Equal(Full("/work/src/a.cs"), Single(target).FullName);
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
        Assert.Equal(Full("/work/notes/2026/today.md"), Single(target).FullName);
    }

    [Fact]
    public void A_missing_path_with_a_trailing_slash_is_created_as_the_workspace_folder()
    {
        var target = Resolve("scratch/");

        Assert.True(_fs.Directory.Exists("/work/scratch"));
        Assert.Equal(Full("/work/scratch"), target.Workspace!.FullName);
        Assert.Empty(target.Files);
    }

    [Fact]
    public void A_path_that_cannot_be_created_is_an_error_instead_of_a_target()
    {
        var fs = new DeniedFileSystem();
        fs.AddDirectory("/work");

        var target = StartupArguments.Resolve(["hosts.new"], fs, "/work", Agree);

        Assert.Equal($"tuicode: permission denied: {fs.Path.GetFullPath("/work/hosts.new")}", target.Error);
        Assert.Null(target.Workspace);
        Assert.Empty(target.Files);
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
        Assert.Empty(target.Files);
        Assert.False(_fs.File.Exists($"/work/{flag}"));
    }

    [Fact]
    public void The_value_of_driver_is_not_a_path()
    {
        var target = StartupArguments.Resolve(["--driver", "ansi"], _fs, "/work", Agree);

        Assert.Equal(Full("/work"), target.Workspace!.FullName);
        Assert.Empty(target.Files);
        Assert.False(_fs.File.Exists("/work/ansi"));
    }

    [Fact]
    public void A_path_alongside_flags_is_still_found()
    {
        var target = StartupArguments.Resolve(["--driver=ansi", "src/a.cs", "--smoke"], _fs, "/work", Agree);

        Assert.Equal(Full("/work/src/a.cs"), Single(target).FullName);
    }

    [Theory]
    [InlineData("src/a.cs:42", 42, 1)]
    [InlineData("src/a.cs:42:9", 42, 9)]
    public void A_position_on_the_path_rides_along_with_the_file(string path, int line, int column)
    {
        var target = Resolve(path);

        Assert.Equal(Full("/work/src/a.cs"), Single(target).FullName);
        Assert.Equal(new FilePosition(line, column), Assert.Single(target.Files).Position);
    }

    [Fact]
    public void A_position_on_a_folder_is_dropped()
    {
        var target = Resolve("src:42");

        Assert.Equal(Full("/work/src"), target.Workspace!.FullName);
        Assert.Empty(target.Files);
    }

    [Fact]
    public void A_path_that_has_to_be_created_is_created_as_typed()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A colon can't be in a Windows file name, so the rule can't apply there");

        var target = Resolve("src/new.cs:42");

        Assert.Equal(Full("/work/src/new.cs:42"), Single(target).FullName);
        Assert.False(_fs.File.Exists(Full("/work/src/new.cs")));
        Assert.Null(Assert.Single(target.Files).Position);
    }

    [Fact]
    public void Every_file_on_the_command_line_opens_in_the_order_given()
    {
        _fs.AddFile("/work/src/b.cs", new MockFileData("class B;"));
        _fs.AddFile("/work/notes.md", new MockFileData("# hi"));

        var target = Resolve("src/b.cs", "notes.md", "src/a.cs");

        Assert.Equal(
            [Full("/work/src/b.cs"), Full("/work/notes.md"), Full("/work/src/a.cs")],
            Paths(target));
    }

    [Fact]
    public void The_first_path_roots_the_workspace_and_the_rest_may_live_outside_it()
    {
        var target = Resolve("src/a.cs", "/elsewhere/README.md");

        Assert.Equal(Full("/work"), target.Workspace!.FullName);
        Assert.Equal([Full("/work/src/a.cs"), Full("/elsewhere/README.md")], Paths(target));
    }

    [Fact]
    public void A_folder_first_roots_the_workspace_and_the_files_after_it_open()
    {
        var target = Resolve("src", "/elsewhere/README.md");

        Assert.Equal(Full("/work/src"), target.Workspace!.FullName);
        Assert.Equal([Full("/elsewhere/README.md")], Paths(target));
    }

    [Fact]
    public void A_folder_after_the_first_path_opens_nothing_and_does_not_re_root()
    {
        var target = Resolve("/elsewhere/README.md", "src");

        Assert.Equal(Full("/elsewhere"), target.Workspace!.FullName);
        Assert.Equal([Full("/elsewhere/README.md")], Paths(target));
    }

    [Fact]
    public void The_same_path_twice_opens_one_tab()
    {
        var target = Resolve("src/a.cs", "/work/src/a.cs");

        Assert.Equal([Full("/work/src/a.cs")], Paths(target));
    }

    [Fact]
    public void Each_path_keeps_its_own_position()
    {
        _fs.AddFile("/work/src/b.cs", new MockFileData("class B;"));

        var target = Resolve("src/a.cs:10", "src/b.cs:20:3");

        Assert.Equal(
            [new FilePosition(10, 1), new FilePosition(20, 3)],
            target.Files.Select(file => file.Position));
    }

    [Fact]
    public void A_missing_path_anywhere_in_the_list_is_created()
    {
        var target = Resolve("src/a.cs", "notes/today.md");

        Assert.True(_fs.File.Exists("/work/notes/today.md"));
        Assert.Equal([Full("/work/src/a.cs"), Full("/work/notes/today.md")], Paths(target));
    }

    [Fact]
    public void One_path_that_cannot_be_created_opens_none_of_them()
    {
        var fs = new DeniedFileSystem();
        fs.AddFile("/work/src/a.cs", new MockFileData("class A;"));

        var target = StartupArguments.Resolve(["src/a.cs", "hosts.new"], fs, "/work", Agree);

        Assert.Equal($"tuicode: permission denied: {fs.Path.GetFullPath("/work/hosts.new")}", target.Error);
        Assert.Null(target.Workspace);
        Assert.Empty(target.Files);
    }

    [Fact]
    public void Declining_one_path_starts_nothing_and_creates_none_of_them()
    {
        var target = StartupArguments.Resolve(["first.md", "second.md"], _fs, "/work", Decline);

        Assert.True(target.Declined);
        Assert.False(_fs.File.Exists("/work/first.md"));
        Assert.False(_fs.File.Exists("/work/second.md"));
    }

    private static IEnumerable<string> Paths(StartupTarget target) =>
        target.Files.Select(file => file.File.FullName);

    private static IFileInfo Single(StartupTarget target) => Assert.Single(target.Files).File;

    private static bool Agree(string path, bool directory) => true;

    private static bool Decline(string path, bool directory) => false;

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private StartupTarget Resolve(params string[] args) => StartupArguments.Resolve(args, _fs, "/work", Agree);
}
