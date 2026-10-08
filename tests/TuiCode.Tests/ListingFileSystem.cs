namespace TuiCode.Tests;

/// <summary>A <see cref="MockFileSystem"/> that records each folder it's asked to list.</summary>
internal sealed class ListingFileSystem : MockFileSystem
{
    public List<string> Listed { get; } = [];

    public override IDirectory Directory => new ListingDirectory(this);

    private sealed class ListingDirectory(ListingFileSystem fs) : MockDirectory(fs, "/")
    {
        public override IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, SearchOption searchOption) =>
            base.EnumerateFileSystemEntries(Listing(path), searchPattern, searchOption);

        public override IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption) =>
            base.EnumerateDirectories(Listing(path), searchPattern, searchOption);

        public override IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) =>
            base.EnumerateFiles(Listing(path), searchPattern, searchOption);

        private string Listing(string path)
        {
            fs.Listed.Add(fs.Path.GetFullPath(path));
            return path;
        }
    }
}
