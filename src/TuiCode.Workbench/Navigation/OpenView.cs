using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Navigation;

/// <summary>
/// Modal file/folder browser (VS Code's Ctrl+O). Lists the current directory — a
/// leading <c>../</c>, then sub-directories, then files. Up/Down select, Enter
/// drills into a directory or opens a file in the editor, Esc cancels. Tab moves to
/// the "Open this folder" button which switches the workspace to the directory
/// currently being browsed (closing the old workspace).
///
/// Owns its own <see cref="ICommandService"/> + <see cref="IKeybindingService"/>;
/// the <see cref="WorkbenchHost"/> pushes <see cref="Scope"/> on open and pops it on
/// <see cref="Cancelled"/> / a selection.
/// </summary>
public sealed class OpenView : Window
{
    private readonly Label _pathLabel;
    private readonly ListView _list;
    private readonly Button _openFolderButton;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    private IDirectoryInfo _currentDirectory;
    private List<OpenEntry> _entries = new();

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;
    public event EventHandler<IFileInfo>? FileSelected;
    public event EventHandler<IDirectoryInfo>? FolderSelected;

    public OpenView(IDirectoryInfo startDirectory)
    {
        ArgumentNullException.ThrowIfNull(startDirectory);
        _currentDirectory = startDirectory;

        Title = "Open File or Folder";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 70;
        Height = 22;
        // Required for descendant focus — same reason as the other modals.
        CanFocus = true;

        _pathLabel = new Label { X = 1, Y = 0, Width = Dim.Fill(1) };

        _list = new ListView
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
        };
        _list.MouseEvent += (_, _) => _list.SetFocus();

        _openFolderButton = new Button
        {
            X = 1,
            Y = Pos.AnchorEnd(2),
            Text = "Open this folder",
        };
        _openFolderButton.Accepting += (_, _) => OpenCurrentFolder();

        var hint = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(1),
            Text = "Up/Down select · Enter open · Tab -> Open folder · Esc cancel",
        };

        Add(_pathLabel, _list, _openFolderButton, hint);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();

        Navigate(startDirectory);
    }

    public bool FocusList() => _list.SetFocus();

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.OpenCancel, () => Cancelled?.Invoke(this, EventArgs.Empty));
        _scopeCommands.Register(CommandIds.OpenConfirm, OnConfirm);

        _scopeKeybindings.Bind("Esc", CommandIds.OpenCancel);
        _scopeKeybindings.Bind("Enter", CommandIds.OpenConfirm);
    }

    private void Navigate(IDirectoryInfo directory)
    {
        _currentDirectory = directory;
        _entries = DirectoryListing.Build(directory);
        _pathLabel.Text = Ellipsize(directory.FullName, 66);

        var lines = _entries.Select(e => e.Display).ToList();
        _list.Source = new ListWrapper<string>(new(lines));
        _list.SelectedItem = _entries.Count > 0 ? 0 : null;
    }

    // Enter is bound in the modal scope, so it never reaches the focused control.
    // Route it ourselves: a focused button opens the current folder; otherwise act
    // on the highlighted entry.
    private void OnConfirm()
    {
        if (_openFolderButton.HasFocus)
        {
            OpenCurrentFolder();
            return;
        }

        var i = _list.SelectedItem ?? -1;
        if (i < 0 || i >= _entries.Count) return;

        var entry = _entries[i];
        switch (entry.Kind)
        {
            case OpenEntryKind.Parent:
            case OpenEntryKind.Directory:
                Navigate((IDirectoryInfo)entry.Info);
                break;
            case OpenEntryKind.File:
                FileSelected?.Invoke(this, (IFileInfo)entry.Info);
                break;
        }
    }

    private void OpenCurrentFolder() => FolderSelected?.Invoke(this, _currentDirectory);

    // Keep the tail of a long path — the leaf directory is what the user cares about.
    private static string Ellipsize(string s, int max) =>
        s.Length <= max ? s : "…" + s[^(max - 1)..];
}

internal enum OpenEntryKind { Parent, Directory, File }

internal sealed record OpenEntry(OpenEntryKind Kind, string Display, IFileSystemInfo Info);

internal static class DirectoryListing
{
    /// <summary>
    /// Build the browse list for a directory: a leading <c>../</c> (when there's a parent),
    /// then sub-directories and files, each alphabetically. Pure — no TG dependency, so it's
    /// unit-tested directly against a <c>MockFileSystem</c>.
    /// </summary>
    public static List<OpenEntry> Build(IDirectoryInfo directory)
    {
        var entries = new List<OpenEntry>();
        if (directory.Parent is { } parent)
            entries.Add(new OpenEntry(OpenEntryKind.Parent, "../", parent));

        // Enumeration can throw on an unreadable directory (permissions, a delete mid-browse);
        // degrade to whatever we managed to list rather than tearing down the modal.
        try
        {
            foreach (var d in directory.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                entries.Add(new OpenEntry(OpenEntryKind.Directory, d.Name + "/", d));
            foreach (var f in directory.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                entries.Add(new OpenEntry(OpenEntryKind.File, f.Name, f));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return entries;
    }
}
