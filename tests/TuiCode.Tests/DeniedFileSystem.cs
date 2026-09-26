namespace TuiCode.Tests;

/// <summary>A <see cref="MockFileSystem"/> whose file creation is refused, as an unwritable path's would be.</summary>
internal sealed class DeniedFileSystem : MockFileSystem
{
    public override IFile File => new DeniedFile(this);

    private sealed class DeniedFile(MockFileSystem fileSystem) : MockFile(fileSystem)
    {
        public override FileSystemStream Create(string path) =>
            throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.");
    }
}
