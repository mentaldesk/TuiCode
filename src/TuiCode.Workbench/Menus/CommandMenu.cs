using TuiCode.Abstractions;

namespace TuiCode.Workbench.Menus;

/// <summary>
/// The menu bar's eight menus (#340). Every item runs a registered command by id through the same path as the
/// palette, so the menu, the key, the mnemonic and the palette are four doors into one list.
/// </summary>
public sealed class CommandMenu
{
    public const string Separator = "-";

    /// <summary>Commands with a mnemonic but no menu item: opening the menu moves focus itself.</summary>
    public static readonly string[] Unlisted =
    [
        CommandIds.FocusSidebar, CommandIds.FocusEditorBody, CommandIds.FocusEditorTabStrip,
        .. Enumerable.Range(1, 9).Select(CommandIds.FocusEditorByIndex),
    ];

    /// <summary>Items titled for their menu rather than with the command's label.</summary>
    public static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>
    {
        [CommandIds.ShowExplorer] = "Explorer",
        [CommandIds.FindGlobally] = "Find",
        [CommandIds.FocusUsages] = "Usages",
        [CommandIds.FocusReview] = "Review",
    };

    /// <summary>Each item's letter, unique within its menu, which runs it while the menu is open (#516).</summary>
    public static readonly IReadOnlyDictionary<string, char> Letters = new Dictionary<string, char>
    {
        [CommandIds.New] = 'n', [CommandIds.Open] = 'o', [CommandIds.OpenRecentFolder] = 'r', [CommandIds.OpenWorktree] = 'w',
        [CommandIds.OpenFilePath] = 'f', [CommandIds.SaveActiveEditor] = 's', [CommandIds.SaveAll] = 'a',
        [CommandIds.ReloadFromDisk] = 'l', [CommandIds.CloseActiveEditor] = 'c', [CommandIds.CloseOtherTabs] = 'e', [CommandIds.CloseAllTabs] = 'b', [CommandIds.ReopenClosedEditor] = 'u',
        [CommandIds.RenameFile] = 'm', [CommandIds.CutFile] = 't', [CommandIds.PasteFile] = 'p', [CommandIds.DeleteFile] = 'd',
        [CommandIds.DeletePermanently] = 'y',
        [CommandIds.ShowDocumentInfo] = 'i', [CommandIds.OpenSettings] = 'g', [CommandIds.SuspendToShell] = 'h',
        [CommandIds.Quit] = 'q',

        [CommandIds.MoveLinesUp] = 'm', [CommandIds.MoveLinesDown] = 'd', [CommandIds.DuplicateLinesUp] = 'u',
        [CommandIds.DuplicateLinesDown] = 'l', [CommandIds.IndentLines] = 'i', [CommandIds.OutdentLines] = 'o',
        [CommandIds.ToggleLineComment] = 't', [CommandIds.FindInFile] = 'f', [CommandIds.ReplaceInFile] = 'r',
        [CommandIds.ReplaceGlobally] = 'g', [CommandIds.ToggleMatchCase] = 's', [CommandIds.ToggleWholeWord] = 'w',
        [CommandIds.ToggleIgnoredFiles] = 'n', [CommandIds.ChangeGrammar] = 'c',

        [CommandIds.AddCursorAbove] = 'a', [CommandIds.AddCursorBelow] = 'b', [CommandIds.RemoveSecondaryCursors] = 'r',
        [CommandIds.ClearSelection] = 'c', [CommandIds.ToggleColumnSelect] = 't', [CommandIds.SelectNextOccurrence] = 's',
        [CommandIds.SelectPreviousOccurrence] = 'p', [CommandIds.SelectAllOccurrences] = 'o',

        [CommandIds.ShowExplorer] = 'e', [CommandIds.FindGlobally] = 'f', [CommandIds.FocusUsages] = 'u',
        [CommandIds.FocusReview] = 'r', [CommandIds.ToggleSidebar] = 's', [CommandIds.WidenSidebar] = 'w',
        [CommandIds.NarrowSidebar] = 'n', [CommandIds.RefreshExplorer] = 'h', [CommandIds.ToggleGutter] = 'g',
        [CommandIds.ToggleWordWrap] = 'd', [CommandIds.MoveToOtherGroup] = 'm', [CommandIds.FocusOtherGroup] = 'o',
        [CommandIds.JoinGroups] = 'j', [CommandIds.ToggleFileHistory] = 'i',
        [CommandIds.AiAssistance] = 'a',

        [CommandIds.GoToLine] = 'g', [CommandIds.GoToSymbol] = 's', [CommandIds.GoToMatchingBracket] = 'm', [CommandIds.GoToDefinition] = 'd',
        [CommandIds.FindUsages] = 'f', [CommandIds.FollowLink] = 'l', [CommandIds.NavigateBack] = 'p',
        [CommandIds.NavigateForward] = 'n', [CommandIds.NextEditor] = 't', [CommandIds.PreviousEditor] = 'r',

        [CommandIds.CompareToSaved] = 'c', [CommandIds.CompareToRevision] = 'r', [CommandIds.CompareToOtherFile] = 'o',
        [CommandIds.NextChange] = 'n', [CommandIds.PreviousChange] = 'p', [CommandIds.GoToChangeLine] = 'g',
        [CommandIds.RevertChange] = 'e', [CommandIds.RevertAllChanges] = 'a', [CommandIds.GitBlame] = 'b',

        [CommandIds.OpenPullRequest] = 'o', [CommandIds.PullRequestOverview] = 'p', [CommandIds.CreateComment] = 'c',
        [CommandIds.ToggleViewed] = 'v', [CommandIds.SubmitReview] = 's',

        [CommandIds.ShowHelp] = 'k', [CommandIds.ShowActions] = 's', [CommandIds.ShowMnemonics] = 'm',
        [CommandIds.WatchTutorials] = 't', [CommandIds.ShowDiagnostics] = 'd', [CommandIds.ShowLog] = 'l', [CommandIds.ShowMessages] = 'e', [CommandIds.ShowAbout] = 'a',
    };

    public static readonly (string Title, string[] Ids)[] Layout =
    [
        ("_File",
        [
            CommandIds.New, CommandIds.Open, CommandIds.OpenRecentFolder, CommandIds.OpenWorktree, CommandIds.OpenFilePath, CommandIds.SaveActiveEditor, CommandIds.SaveAll, CommandIds.ReloadFromDisk, CommandIds.CloseActiveEditor, CommandIds.CloseOtherTabs, CommandIds.CloseAllTabs, CommandIds.ReopenClosedEditor,
            Separator,
            CommandIds.RenameFile, CommandIds.CutFile, CommandIds.PasteFile, CommandIds.DeleteFile, CommandIds.DeletePermanently,
            Separator,
            CommandIds.ShowDocumentInfo,
            Separator,
            CommandIds.OpenSettings,
            Separator,
            CommandIds.SuspendToShell, CommandIds.Quit,
        ]),
        ("_Edit",
        [
            CommandIds.MoveLinesUp, CommandIds.MoveLinesDown, CommandIds.DuplicateLinesUp, CommandIds.DuplicateLinesDown,
            CommandIds.IndentLines, CommandIds.OutdentLines, CommandIds.ToggleLineComment,
            Separator,
            CommandIds.FindInFile, CommandIds.ReplaceInFile, CommandIds.ReplaceGlobally,
            CommandIds.ToggleMatchCase, CommandIds.ToggleWholeWord, CommandIds.ToggleIgnoredFiles,
            Separator,
            CommandIds.ChangeGrammar,
        ]),
        ("_Selection",
        [
            CommandIds.AddCursorAbove, CommandIds.AddCursorBelow, CommandIds.RemoveSecondaryCursors, CommandIds.ClearSelection,
            Separator,
            CommandIds.ToggleColumnSelect,
            Separator,
            CommandIds.SelectNextOccurrence, CommandIds.SelectPreviousOccurrence, CommandIds.SelectAllOccurrences,
        ]),
        ("_View",
        [
            CommandIds.ShowExplorer, CommandIds.FindGlobally, CommandIds.FocusUsages, CommandIds.FocusReview,
            Separator,
            CommandIds.ToggleSidebar, CommandIds.WidenSidebar, CommandIds.NarrowSidebar,
            Separator,
            CommandIds.ToggleFileHistory, CommandIds.AiAssistance,
            Separator,
            CommandIds.RefreshExplorer,
            Separator,
            CommandIds.ToggleGutter, CommandIds.ToggleWordWrap,
            Separator,
            CommandIds.MoveToOtherGroup, CommandIds.FocusOtherGroup, CommandIds.JoinGroups,
        ]),
        ("_Go",
        [
            CommandIds.GoToLine, CommandIds.GoToSymbol, CommandIds.GoToMatchingBracket, CommandIds.GoToDefinition, CommandIds.FindUsages, CommandIds.FollowLink,
            Separator,
            CommandIds.NavigateBack, CommandIds.NavigateForward,
            Separator,
            CommandIds.NextEditor, CommandIds.PreviousEditor,
        ]),
        ("_Diff",
        [
            CommandIds.CompareToSaved, CommandIds.CompareToRevision, CommandIds.CompareToOtherFile,
            Separator,
            CommandIds.NextChange, CommandIds.PreviousChange, CommandIds.GoToChangeLine,
            Separator,
            CommandIds.RevertChange, CommandIds.RevertAllChanges,
            Separator,
            CommandIds.GitBlame,
        ]),
        ("_Review",
        [
            CommandIds.OpenPullRequest, CommandIds.PullRequestOverview,
            Separator,
            CommandIds.CreateComment, CommandIds.ToggleViewed, CommandIds.SubmitReview,
        ]),
        ("_Help",
        [
            CommandIds.ShowHelp, CommandIds.ShowActions, CommandIds.ShowMnemonics,
            Separator,
            CommandIds.WatchTutorials, CommandIds.ShowDiagnostics, CommandIds.ShowLog, CommandIds.ShowMessages, CommandIds.ShowAbout,
        ]),
    ];

    private readonly MenuBar _bar;
    private readonly ICommandService _commands;
    private readonly IKeybindingService _keybindings;
    private readonly Func<bool> _canOpen;
    private readonly Func<string, bool> _isAvailable;
    private readonly List<(string Id, CommandMenuItem Item)> _items = [];
    private readonly List<(MenuBarItem Menu, (string Id, CommandMenuItem Item)[] Items)> _menus = [];
    private string? _picked;

    public CommandMenu(MenuBar bar, ICommandService commands, IKeybindingService keybindings, Func<bool> canOpen,
        Func<string, bool> isAvailable)
    {
        _bar = bar;
        _commands = commands;
        _keybindings = keybindings;
        _canOpen = canOpen;
        _isAvailable = isAvailable;
        // Our own keybinding service opens it, so the key can be rebound.
        _bar.HotKeyBindings.Remove(_bar.Key);
        _bar.Menus = [.. Layout.Select(Build)];
        Refresh();
    }

    public bool IsOpen { get; private set; }

    public IReadOnlyList<(string Id, CommandMenuItem Item)> Items => _items;

    public event EventHandler? Opened;

    /// <summary>The menu has shut, carrying the command picked from it, if any.</summary>
    public event EventHandler<string?>? Closed;

    public void Open()
    {
        if (!IsOpen) _bar.InvokeCommand(Command.HotKey);
    }

    /// <summary>Opens the menu on the bar whose letter is <paramref name="letter"/>, if there is one.</summary>
    public bool OpenByLetter(Key letter)
    {
        if (IsOpen || _bar.SubViews.OfType<MenuBarItem>().FirstOrDefault(m => m.HotKey.KeyCode == letter.KeyCode) is not { } menu)
            return false;
        menu.InvokeCommand(Command.HotKey);
        return true;
    }

    /// <summary>Shows each item's current key, e.g. after a rebind in Settings.</summary>
    public void Refresh()
    {
        var keys = _keybindings.Bindings
            .GroupBy(b => b.CommandId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Display, StringComparer.Ordinal);
        foreach (var (id, item) in _items)
        {
            var key = keys.GetValueOrDefault(id, string.Empty);
            if (item.KeyView.Text == key) continue;
            item.KeyView.Text = key;
            item.SetNeedsLayout();
        }
    }

    /// <summary>Dims each item that can't run, and takes a menu with none that can off the bar.</summary>
    public void ShowAvailable()
    {
        DimUnavailable();
        var shown = _menus.Where(m => m.Items.Any(i => !i.Item.Dimmed)).Select(m => m.Menu).ToArray();
        // TG's bar keeps a hidden item's place, so an empty menu leaves the bar rather than hiding.
        if (!shown.SequenceEqual(_bar.SubViews.OfType<MenuBarItem>())) _bar.Menus = shown;
    }

    private void DimUnavailable()
    {
        var titles = _commands.Registered.ToDictionary(c => c.Id, c => c.Title, StringComparer.Ordinal);
        foreach (var (id, item) in _items)
        {
            item.Dimmed = !_isAvailable(id);
            var title = Title(id, titles.GetValueOrDefault(id));
            if (item.Title != title) item.Title = title;
        }
    }

    private static string Title(string id, string? registered)
    {
        var title = Titles.GetValueOrDefault(id) ?? registered ?? id;
        return Letters.TryGetValue(id, out var letter) ? Underline(title, letter) : title;
    }

    private MenuBarItem Build((string Title, string[] Ids) entry)
    {
        var first = _items.Count;
        var menu = new MenuBarItem(entry.Title, entry.Ids.Select(Entry).ToArray());
        _menus.Add((menu, _items[first..].ToArray()));
        // A title's bare letter would open its menu from anywhere and swallow typing; keep only Alt+letter.
        menu.HotKeyBindings.Remove(menu.HotKey);
        menu.HotKeyBindings.Remove(menu.HotKey.WithShift);
        // NeutralizeBuiltinQuitKey took Esc off TG's Quit command, and the menu's close with it.
        menu.PopoverMenu?.KeyBindings.Add(Key.Esc, Command.Quit);
        menu.PopoverMenuOpenChanged += (_, e) => OnOpenChanged(e.NewValue);
        return menu;
    }

    private View Entry(string id) => id == Separator ? new Line() : Item(id);

    private CommandMenuItem Item(string id)
    {
        var item = new CommandMenuItem
        {
            Title = Title(id, _commands.Registered.FirstOrDefault(c => c.Id == id)?.Title),
            // The workbench already dispatches the key; binding it here too would run the command twice.
            BindKeyToApplication = false,
            Action = () => _picked = id,
        };
        _items.Add((id, item));
        return item;
    }

    // Where the letter starts a word, else where it first appears.
    private static string Underline(string title, char letter)
    {
        var at = Enumerable.Range(0, title.Length).Where(i => char.ToLowerInvariant(title[i]) == letter).ToList();
        if (at.Count == 0) return title;
        var start = at.FirstOrDefault(i => i == 0 || !char.IsLetterOrDigit(title[i - 1]), at[0]);
        return title.Insert(start, "_");
    }

    // Moving between menus closes one before opening the next, and TG is still moving focus as a menu
    // shuts, so decide it has shut on the next loop iteration.
    private void OnOpenChanged(bool open)
    {
        if (open)
        {
            if (IsOpen) return;
            if (!_canOpen())
            {
                _bar.HideActiveItem();
                return;
            }
            IsOpen = true;
            _picked = null;
            Opened?.Invoke(this, EventArgs.Empty);
            DimUnavailable();
            return;
        }
        _bar.App?.AddTimeout(TimeSpan.Zero, () =>
        {
            if (!IsOpen || _bar.IsOpen()) return false;
            IsOpen = false;
            var picked = _picked;
            _picked = null;
            Closed?.Invoke(this, picked);
            return false;
        });
    }
}
