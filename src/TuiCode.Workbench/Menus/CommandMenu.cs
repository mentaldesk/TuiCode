using TuiCode.Abstractions;

namespace TuiCode.Workbench.Menus;

/// <summary>
/// The menu bar's eight menus (#340). Every item runs a registered command by id through the same path as the
/// palette, so the menu, the key, the mnemonic and the palette are four doors into one list.
/// </summary>
public sealed class CommandMenu
{
    public const string Separator = "-";
    public const string FocusEditorTab = "Focus editor tab";

    public static readonly (string Title, string[] Ids)[] Layout =
    [
        ("_File",
        [
            CommandIds.New, CommandIds.Open, CommandIds.SaveActiveEditor, CommandIds.ReloadFromDisk, CommandIds.CloseActiveEditor,
            Separator,
            CommandIds.RenameFile, CommandIds.CutFile, CommandIds.PasteFile, CommandIds.DeleteFile,
            Separator,
            CommandIds.ShowDocumentInfo,
            Separator,
            CommandIds.OpenSettings, CommandIds.Quit,
        ]),
        ("_Edit",
        [
            CommandIds.MoveLinesUp, CommandIds.MoveLinesDown, CommandIds.DuplicateLinesUp, CommandIds.DuplicateLinesDown,
            Separator,
            CommandIds.FindInFile, CommandIds.ReplaceInFile, CommandIds.FindGlobally, CommandIds.ReplaceGlobally,
            Separator,
            CommandIds.ChangeGrammar,
        ]),
        ("_Selection",
        [
            CommandIds.AddCursorAbove, CommandIds.AddCursorBelow, CommandIds.RemoveSecondaryCursors,
            Separator,
            CommandIds.ToggleColumnSelect,
            Separator,
            CommandIds.SelectNextOccurrence, CommandIds.SelectPreviousOccurrence, CommandIds.SelectAllOccurrences,
        ]),
        ("_View",
        [
            CommandIds.ToggleSidebar, CommandIds.ShowExplorer, CommandIds.RefreshExplorer, CommandIds.FocusSidebar,
            Separator,
            CommandIds.WidenSidebar, CommandIds.NarrowSidebar,
            Separator,
            CommandIds.ToggleGutter,
            Separator,
            CommandIds.FocusEditorBody, CommandIds.FocusEditorTabStrip,
        ]),
        ("_Go",
        [
            CommandIds.GoToLine, CommandIds.GoToSymbol,
            Separator,
            CommandIds.NavigateBack, CommandIds.NavigateForward,
            Separator,
            CommandIds.NextEditor, CommandIds.PreviousEditor, FocusEditorTab,
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
            CommandIds.OpenPullRequest, CommandIds.PullRequestOverview, CommandIds.FocusReview,
            Separator,
            CommandIds.CreateComment, CommandIds.SubmitReview,
        ]),
        ("_Help",
        [
            CommandIds.ShowHelp, CommandIds.ShowActions, CommandIds.ShowMnemonics,
            Separator,
            CommandIds.ShowDiagnostics, CommandIds.ShowAbout,
        ]),
    ];

    public static IEnumerable<string> FocusEditorTabIds =>
        Enumerable.Range(1, 9).Select(CommandIds.FocusEditorByIndex);

    private readonly MenuBar _bar;
    private readonly ICommandService _commands;
    private readonly IKeybindingService _keybindings;
    private readonly Func<bool> _canOpen;
    private readonly Func<string, bool> _isAvailable;
    private readonly List<(string Id, MenuItem Item)> _items = [];
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

    public IReadOnlyList<(string Id, MenuItem Item)> Items => _items;

    public event EventHandler? Opened;

    /// <summary>The menu has shut, carrying the command picked from it, if any.</summary>
    public event EventHandler<string?>? Closed;

    public void Open()
    {
        if (!IsOpen) _bar.InvokeCommand(Command.HotKey);
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

    private MenuBarItem Build((string Title, string[] Ids) entry)
    {
        var menu = new MenuBarItem(entry.Title, entry.Ids.Select(Entry).ToArray());
        // A title's bare letter would open its menu from anywhere and swallow typing; keep only Alt+letter.
        menu.HotKeyBindings.Remove(menu.HotKey);
        menu.HotKeyBindings.Remove(menu.HotKey.WithShift);
        // NeutralizeBuiltinQuitKey took Esc off TG's Quit command, and the menu's close with it.
        menu.PopoverMenu?.KeyBindings.Add(Key.Esc, Command.Quit);
        menu.PopoverMenuOpenChanged += (_, e) => OnOpenChanged(e.NewValue);
        return menu;
    }

    private View Entry(string id) => id switch
    {
        Separator => new Line(),
        FocusEditorTab => new MenuItem(FocusEditorTab, string.Empty, new Menu(FocusEditorTabIds.Select(Item))),
        _ => Item(id),
    };

    private MenuItem Item(string id)
    {
        var item = new MenuItem
        {
            Title = _commands.Registered.FirstOrDefault(c => c.Id == id)?.Label ?? id,
            // The workbench already dispatches the key; binding it here too would run the command twice.
            BindKeyToApplication = false,
            Action = () => _picked = id,
        };
        _items.Add((id, item));
        return item;
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
            foreach (var (id, item) in _items) item.Enabled = _isAvailable(id);
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
