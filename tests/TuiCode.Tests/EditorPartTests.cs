using TuiCode.Workbench.Parts;

namespace TuiCode.Tests;

public class EditorPartTests
{
    [Fact]
    public void Open_loads_file_content()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/readme.md", new MockFileData("# hello"));

        using var editor = new EditorPart();
        editor.Open(fs.FileInfo.New("/work/readme.md"));

        Assert.Equal("# hello", editor.Content);
        Assert.NotNull(editor.CurrentFile);
        Assert.Equal("/work/readme.md", editor.CurrentFile!.FullName);
    }

    [Fact]
    public void Save_writes_current_content_back_to_disk_and_fires_FileSaved()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/notes.txt", new MockFileData("original"));

        using var editor = new EditorPart();
        editor.Open(fs.FileInfo.New("/work/notes.txt"));
        editor.Content = "edited";

        IFileInfo? savedFile = null;
        editor.FileSaved += (_, f) => savedFile = f;

        editor.Save();

        Assert.Equal("edited", fs.File.ReadAllText("/work/notes.txt"));
        Assert.NotNull(savedFile);
        Assert.Equal("/work/notes.txt", savedFile!.FullName);
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
