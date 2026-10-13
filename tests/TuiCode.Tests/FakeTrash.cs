using TuiCode.Abstractions;

namespace TuiCode.Tests;

internal sealed class FakeTrash(IFileSystem fs) : ITrash
{
    public string Name { get; init; } = "Trash";

    public List<string> Moved { get; } = [];

    public Exception? Refusal { get; set; }

    public void Move(string path)
    {
        if (Refusal is not null) throw Refusal;
        Moved.Add(path);
        if (fs.Directory.Exists(path))
            fs.Directory.Delete(path, recursive: true);
        else
            fs.File.Delete(path);
    }
}
