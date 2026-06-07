namespace TuiCode.Explorer;

public sealed class FileExplorerView : TreeView<IFileSystemInfo>
{
    public event EventHandler<IFileInfo>? FileActivated;

    /// <summary>The directory the tree is currently rooted at, or null before the first <see cref="Open"/>.</summary>
    public IDirectoryInfo? Root { get; private set; }

    public FileExplorerView()
    {
        TreeBuilder = new FileSystemTreeBuilder { IncludeFiles = true };
        AspectGetter = info => info.Name;
        Activated += (_, _) => ActivateSelected();

        // TG TreeView's default Enter binding maps to Command.Activate but does NOT raise the
        // Activated event (Space does). Intercept Enter at the KeyDown level so users can open
        // files with either key — confirmed by FileExplorerViewTests.Enter_key_activates_the_selected_file.
        KeyDown += (_, key) =>
        {
            if (key == Key.Enter)
            {
                ActivateSelected();
                key.Handled = true;
            }
        };
    }

    public void Open(IDirectoryInfo root)
    {
        Root = root;
        ClearObjects();
        AddObject(root);
        Expand(root);
    }

    public void ActivateSelected()
    {
        if (SelectedObject is IFileInfo file)
            FileActivated?.Invoke(this, file);
    }

    /// <summary>
    /// The directory a new file or folder should be created in, given the current selection
    /// (VS Code's rules): a selected folder hosts the new item as a child, a selected file's
    /// parent hosts it as a sibling, and with nothing selected it lands at the tree root.
    /// Returns null before the first <see cref="Open"/>.
    /// </summary>
    public IDirectoryInfo? NewEntryTarget() => SelectedObject switch
    {
        IDirectoryInfo dir => dir,
        IFileInfo file => file.Directory ?? Root,
        _ => Root,
    };

    /// <summary>
    /// Text to pre-fill the new-file/folder dialog with: the target directory relative to
    /// <see cref="Root"/>, with a trailing slash, or empty when the target is the root itself.
    /// </summary>
    public string NewEntryPrefill() =>
        Root is { } root && NewEntryTarget() is { } target
            ? NewEntryPaths.Prefill(root, target)
            : string.Empty;

    /// <summary>
    /// Create a file or directory at <paramref name="relativePath"/> (relative to <see cref="Root"/>),
    /// creating intermediate directories as needed, then refresh the tree and select the new node.
    /// Returns the created node. Throws <see cref="IOException"/> if the path already exists.
    /// </summary>
    public IFileSystemInfo Create(string relativePath, bool directory)
    {
        if (Root is not { } root)
            throw new InvalidOperationException("Cannot create entries before the tree is rooted.");

        var fs = root.FileSystem;
        var fullPath = NewEntryPaths.Resolve(fs, root, relativePath);

        if (fs.File.Exists(fullPath) || fs.Directory.Exists(fullPath))
            throw new IOException($"'{relativePath}' already exists.");

        if (directory)
        {
            fs.Directory.CreateDirectory(fullPath);
        }
        else
        {
            var parent = fs.Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent))
                fs.Directory.CreateDirectory(parent);
            fs.File.Create(fullPath).Dispose();
        }

        return Reveal(root, fullPath)
            ?? throw new IOException($"Created '{relativePath}' but could not locate it in the tree.");
    }

    /// <summary>
    /// Walk the tree from the root to <paramref name="fullPath"/>, refreshing and expanding each
    /// directory along the way so a just-created entry surfaces, then select and return its node.
    /// </summary>
    private IFileSystemInfo? Reveal(IDirectoryInfo root, string fullPath)
    {
        var fs = root.FileSystem;
        var relative = fs.Path.GetRelativePath(root.FullName, fullPath);
        var segments = relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

        IFileSystemInfo current = root;
        foreach (var segment in segments)
        {
            // RefreshObject re-reads an already-expanded branch (so a new sibling shows up);
            // Expand builds children fresh the first time a never-opened directory is visited.
            RefreshObject(current);
            Expand(current);
            var match = GetChildren(current)
                .FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.Ordinal));
            if (match is null)
                return null;
            current = match;
        }

        SelectedObject = current;
        return current;
    }
}

/// <summary>
/// Pure path arithmetic for the new-file/folder flow, factored out of <see cref="FileExplorerView"/>
/// so the edge cases are unit-testable without Terminal.Gui.
/// </summary>
internal static class NewEntryPaths
{
    public static string Prefill(IDirectoryInfo root, IDirectoryInfo target)
    {
        var relative = root.FileSystem.Path.GetRelativePath(root.FullName, target.FullName);
        if (relative is "." or "")
            return string.Empty;
        return relative.Replace('\\', '/').TrimEnd('/') + "/";
    }

    public static string Resolve(IFileSystem fs, IDirectoryInfo root, string relativePath)
    {
        var normalized = relativePath
            .Trim()
            .Replace('\\', fs.Path.DirectorySeparatorChar)
            .Replace('/', fs.Path.DirectorySeparatorChar);
        return fs.Path.GetFullPath(fs.Path.Combine(root.FullName, normalized));
    }
}
