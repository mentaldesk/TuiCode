using TuiCode.Abstractions;
using TuiCode.Icons;

namespace TuiCode.Explorer;

public sealed class FileExplorerView : TreeView<IFileSystemInfo>
{
    public event EventHandler<IFileInfo>? FileActivated;

    /// <summary>The directory the tree is currently rooted at, or null before the first <see cref="Open"/>.</summary>
    public IDirectoryInfo? Root { get; private set; }

    public FileExplorerView(FileIcons? icons = null)
    {
        TreeBuilder = new FileSystemTreeBuilder { IncludeFiles = true };
        AspectGetter = info => info.Name;
        if (icons is not null)
        {
            DrawLine += (_, e) =>
            {
                var icon = e.Model switch
                {
                    IDirectoryInfo dir => icons.ForDirectory(IsExpanded(dir)),
                    IFileInfo file => icons.ForFile(file.Name),
                    _ => null,
                };
                if (icon is { } i) IconDrawing.Prepend(e, i);
            };
            icons.Changed += (_, _) => SetNeedsDraw();
        }
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
            ? EntryPaths.Prefill(root, target)
            : string.Empty;

    /// <summary>
    /// Create a file or directory at <paramref name="relativePath"/> (relative to <see cref="Root"/>),
    /// creating intermediate directories as needed, then refresh the tree and select the new node.
    /// A trailing slash means a directory; anything else is a file (so extensionless files like
    /// <c>Makefile</c> work). Returns the created node. Throws <see cref="IOException"/> if the path
    /// already exists.
    /// </summary>
    public IFileSystemInfo Create(string relativePath)
    {
        if (Root is not { } root)
            throw new InvalidOperationException("Cannot create entries before the tree is rooted.");

        var directory = EntryPaths.IsDirectoryPath(relativePath);
        var fs = root.FileSystem;
        var fullPath = EntryPaths.Resolve(fs, root, relativePath);

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

    public bool IsRoot(IFileSystemInfo item) =>
        Root is { } root && FilePaths.IsSameOrUnder(root.FullName, item.FullName);

    public string RelativePath(IFileSystemInfo item) =>
        EntryPaths.Relative(RequireRoot(), item.FullName);

    /// <summary>Permanently delete <paramref name="item"/>; a selection at or under it moves to a sibling or the parent.</summary>
    public void Delete(IFileSystemInfo item)
    {
        var root = RequireRoot();
        if (IsRoot(item))
            throw new IOException("The workspace root can't be deleted.");

        var fs = root.FileSystem;
        var parent = fs.Path.GetDirectoryName(item.FullName)!;
        var selected = SelectedObject?.FullName;
        var reselect = selected is not null && FilePaths.IsSameOrUnder(selected, item.FullName)
            ? SelectionAfterRemoving(fs, parent, item.FullName)
            : selected;

        if (item is IDirectoryInfo)
            fs.Directory.Delete(item.FullName, recursive: true);
        else
            fs.File.Delete(item.FullName);

        if (Find(parent) is { } parentNode)
            RefreshKeepingExpansion(parentNode);
        if (reselect is not null)
            Reveal(root, reselect);
    }

    /// <summary>
    /// Rename or move <paramref name="item"/>, creating missing folders, and select it. Returns <paramref name="item"/>
    /// itself when the path doesn't change.
    /// </summary>
    public IFileSystemInfo Move(IFileSystemInfo item, string relativePath)
    {
        var root = RequireRoot();
        if (IsRoot(item))
            throw new IOException("The workspace root can't be renamed or moved.");

        var fs = root.FileSystem;
        var source = item.FullName;
        var target = EntryPaths.Resolve(fs, root, relativePath.Trim().TrimEnd('/', '\\'));
        var shown = relativePath.Trim();

        if (target == source)
            return item;
        if (!FilePaths.IsSameOrUnder(target, root.FullName) || FilePaths.IsSameOrUnder(root.FullName, target))
            throw new IOException($"'{shown}' is outside the workspace.");
        // A case-only rename finds the item itself on a case-insensitive file system; .NET's Move handles it.
        var caseOnly = string.Equals(source, target, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && item is IDirectoryInfo && FilePaths.IsSameOrUnder(target, source))
            throw new IOException($"Can't move '{item.Name}' into itself.");
        if (!caseOnly && (fs.File.Exists(target) || fs.Directory.Exists(target)))
            throw new IOException($"'{shown}' already exists.");

        var targetParent = fs.Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(targetParent))
            fs.Directory.CreateDirectory(targetParent);
        if (item is IDirectoryInfo)
            fs.Directory.Move(source, target);
        else
            fs.File.Move(source, target);

        if (Find(fs.Path.GetDirectoryName(source)!) is { } sourceParent)
            RefreshKeepingExpansion(sourceParent);
        return Reveal(root, target)
            ?? throw new IOException($"Moved to '{shown}' but could not locate it in the tree.");
    }

    private IDirectoryInfo RequireRoot() =>
        Root ?? throw new InvalidOperationException("The tree isn't rooted yet.");

    private string? SelectionAfterRemoving(IFileSystem fs, string parent, string removed)
    {
        var siblings = (TreeBuilder?.GetChildren(fs.DirectoryInfo.New(parent)) ?? []).Select(c => c.FullName).ToList();
        var index = siblings.IndexOf(removed);
        if (index < 0) return parent;
        if (index + 1 < siblings.Count) return siblings[index + 1];
        return index > 0 ? siblings[index - 1] : parent;
    }

    /// <summary>
    /// Walk the tree from the root to <paramref name="fullPath"/>, refreshing and expanding each
    /// directory along the way so a just-created entry surfaces, then select and return its node.
    /// </summary>
    private IFileSystemInfo? Reveal(IDirectoryInfo root, string fullPath)
    {
        IFileSystemInfo current = root;
        foreach (var segment in Segments(root, fullPath))
        {
            RefreshKeepingExpansion(current);
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

    /// <summary>The node for <paramref name="fullPath"/> if it's already in the tree, without expanding anything.</summary>
    private IFileSystemInfo? Find(string fullPath)
    {
        if (Root is not { } root) return null;
        IFileSystemInfo current = root;
        foreach (var segment in Segments(root, fullPath))
        {
            if (!IsExpanded(current)) return null;
            var match = GetChildren(current)
                .FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.Ordinal));
            if (match is null) return null;
            current = match;
        }
        return current;
    }

    private static string[] Segments(IDirectoryInfo root, string fullPath) =>
        EntryPaths.Relative(root, fullPath).Split('/', StringSplitOptions.RemoveEmptyEntries);

    // TG matches refreshed children by reference and the builder returns fresh entries, so RefreshObject alone collapses subfolders.
    private void RefreshKeepingExpansion(IFileSystemInfo node)
    {
        var expanded = new List<string>();
        CollectExpanded(node, expanded);
        RefreshObject(node);
        foreach (var path in expanded)
            if (Find(path) is { } again)
                Expand(again);
    }

    private void CollectExpanded(IFileSystemInfo node, List<string> expanded)
    {
        if (!IsExpanded(node)) return;
        foreach (var child in GetChildren(node))
        {
            if (!IsExpanded(child)) continue;
            expanded.Add(child.FullName);
            CollectExpanded(child, expanded);
        }
    }
}

/// <summary>
/// Pure path arithmetic for creating, renaming and moving entries, factored out of
/// <see cref="FileExplorerView"/> so the edge cases are unit-testable without Terminal.Gui.
/// </summary>
internal static class EntryPaths
{
    /// <summary>A trailing slash (either style) marks the path as a directory.</summary>
    public static bool IsDirectoryPath(string relativePath)
    {
        var trimmed = relativePath.TrimEnd();
        return trimmed.EndsWith('/') || trimmed.EndsWith('\\');
    }

    public static string Prefill(IDirectoryInfo root, IDirectoryInfo target)
    {
        var relative = Relative(root, target.FullName);
        return relative.Length == 0 ? string.Empty : relative + "/";
    }

    /// <summary>Forward-slashed, and empty for the root itself.</summary>
    public static string Relative(IDirectoryInfo root, string fullPath)
    {
        var relative = root.FileSystem.Path.GetRelativePath(root.FullName, fullPath);
        return relative is "." or "" ? string.Empty : relative.Replace('\\', '/').TrimEnd('/');
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
