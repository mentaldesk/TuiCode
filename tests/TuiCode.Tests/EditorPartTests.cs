using TuiCode.Workbench.Parts;

namespace TuiCode.Tests;

public class EditorPartTests
{
    [Fact]
    public void Open_loads_file_content()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/readme.md", new MockFileData("# hello"));

        var file = fs.FileInfo.New("/work/readme.md");
        using var editor = new EditorPart();
        editor.Open(file);

        Assert.Equal("# hello", editor.Content);
        Assert.NotNull(editor.CurrentFile);
        Assert.Equal(file.FullName, editor.CurrentFile!.FullName);
    }

    [Fact]
    public void Save_writes_current_content_back_to_disk_and_fires_FileSaved()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/notes.txt", new MockFileData("original"));

        var file = fs.FileInfo.New("/work/notes.txt");
        using var editor = new EditorPart();
        editor.Open(file);
        editor.Content = "edited";

        IFileInfo? savedFile = null;
        editor.FileSaved += (_, f) => savedFile = f;

        editor.Save();

        Assert.Equal("edited\n", fs.File.ReadAllText("/work/notes.txt"));
        Assert.NotNull(savedFile);
        Assert.Equal(file.FullName, savedFile!.FullName);
    }

    [Fact]
    public void Open_then_Save_round_trips_a_trailing_newline_unchanged()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/x.txt", new MockFileData("hello\n"));

        using var editor = new EditorPart();
        editor.Open(fs.FileInfo.New("/work/x.txt"));
        editor.Save();

        Assert.Equal("hello\n", fs.File.ReadAllText("/work/x.txt"));
    }

    [Fact]
    public void Open_then_Save_preserves_CRLF_line_endings_on_every_os()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/x.txt", new MockFileData("line1\r\nline2\r\n"));

        using var editor = new EditorPart();
        editor.Open(fs.FileInfo.New("/work/x.txt"));
        editor.Save();

        // A CRLF file round-trips as CRLF regardless of the host OS (TextView re-joins
        // with Environment.NewLine, so this would be "line1\nline2\n" on Linux without
        // the EOL-preserving Save).
        Assert.Equal("line1\r\nline2\r\n", fs.File.ReadAllText("/work/x.txt"));
    }

    [Fact]
    public void Save_appends_a_final_newline_when_the_buffer_does_not_end_with_one()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/x.txt", new MockFileData("hello"));

        using var editor = new EditorPart();
        editor.Open(fs.FileInfo.New("/work/x.txt"));
        editor.Save();

        Assert.Equal("hello\n", fs.File.ReadAllText("/work/x.txt"));
    }

    [Fact]
    public void Save_does_not_add_a_newline_to_an_empty_buffer()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/x.txt", new MockFileData(""));

        using var editor = new EditorPart();
        editor.Open(fs.FileInfo.New("/work/x.txt"));
        editor.Save();

        Assert.Equal("", fs.File.ReadAllText("/work/x.txt"));
    }

    [Fact]
    public void Save_uses_the_OS_default_newline_for_a_new_empty_file()
    {
        // A freshly created file (e.g. via the New File command) is empty on open, so it has
        // no line-ending style to preserve and takes the OS default — CRLF on Windows, LF
        // elsewhere — matching VS Code's files.eol=auto for new files.
        var fs = new MockFileSystem();
        fs.AddFile("/work/new.txt", new MockFileData(""));

        using var editor = new EditorPart();
        editor.Open(fs.FileInfo.New("/work/new.txt"));
        editor.Content = "hello";
        editor.Save();

        Assert.Equal("hello" + Environment.NewLine, fs.File.ReadAllText("/work/new.txt"));
    }

    [Fact]
    public void Save_is_a_no_op_when_no_file_is_open()
    {
        using var editor = new EditorPart();
        var fired = false;
        editor.FileSaved += (_, _) => fired = true;

        editor.Save();

        Assert.False(fired);
    }
}
