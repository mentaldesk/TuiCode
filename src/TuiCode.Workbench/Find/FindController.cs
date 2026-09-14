using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Search;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Find;

/// <summary>
/// Find/replace in the active editor (#33). Owns the <see cref="FindBarView"/>, docks it on the active
/// tab (following tab switches), keeps the match set and highlights in step with the buffer, and
/// drives selection. While open it layers a non-modal input scope over the workbench: Enter /
/// Shift+Enter / Tab / Ctrl+Enter act only while the bar has focus, Esc also closes from the editor
/// body, and every other key — Ctrl+S, Ctrl+Tab, … — falls through to the workbench as usual.
/// </summary>
internal sealed class FindController : IDisposable
{
    private readonly EditorGroup _group;
    private readonly IInputScopeStack _scopes;
    private readonly FindBarView _bar = new();
    private readonly LayeredScope _scope;
    private EditorTab? _tab;
    private IReadOnlyList<TextMatch> _matches = [];
    private int _current = -1;
    // Where the search starts from while typing: the cursor when find opened (or the last match
    // navigated to), so refining the query doesn't ratchet forward through the buffer.
    private (int Row, int Column) _anchor;
    private bool _replacing;

    /// <summary>Raised after the bar closes, so the host can hand focus back to the editor.</summary>
    public event EventHandler? Closed;

    public FindController(EditorGroup group, IInputScopeStack scopes, IKeybindingService below)
    {
        _group = group;
        _scopes = scopes;

        var commands = new CommandService();
        var bindings = new KeybindingService(commands);
        commands.Register(CommandIds.FindNext, () => { if (_bar.ReplacementHasFocus) ReplaceOne(); else Next(); });
        commands.Register(CommandIds.FindPrevious, Previous);
        commands.Register(CommandIds.FindClose, Close);
        commands.Register(CommandIds.FindSwitchField, SwitchField);
        commands.Register(CommandIds.FindReplaceAll, ReplaceAll);
        bindings.Bind("Enter", CommandIds.FindNext);
        bindings.Bind("F3", CommandIds.FindNext);
        // Shift+Enter needs a terminal that reports it (kitty protocol); Shift+F3 always works.
        bindings.Bind("Shift+Enter", CommandIds.FindPrevious);
        bindings.Bind("Shift+F3", CommandIds.FindPrevious);
        bindings.Bind("Esc", CommandIds.FindClose);
        bindings.Bind("Tab", CommandIds.FindSwitchField);
        bindings.Bind("Shift+Tab", CommandIds.FindSwitchField);
        bindings.Bind("Ctrl+Enter", CommandIds.FindReplaceAll);

        _scope = new LayeredScope(bindings, below,
            key => _bar.HasFocus || (key == Key.Esc && _tab?.ContentHasFocus == true));

        _bar.QueryChanged += (_, _) => Recompute(selectFromAnchor: true);
    }

    public bool IsOpen => _tab is not null;

    internal FindBarView Bar => _bar;
    internal IReadOnlyList<TextMatch> Matches => _matches;
    internal int CurrentIndex => _current;

    /// <summary>Show the bar on the active tab (or re-focus it), with or without the replace row.</summary>
    public void Open(bool replace)
    {
        if (_group.ActiveTab is not { } tab) return;

        if (_tab is null)
            _scopes.Push(_scope);
        Attach(tab);

        _anchor = tab.SelectionOrigin;
        var selected = tab.SelectedText;
        _bar.ShowReplace(replace);
        // Seed from a single-line selection, like VS Code. Setting the same text raises no change event,
        // so recompute explicitly either way.
        if (selected.Length > 0 && !selected.Contains('\n') && !selected.Contains('\r'))
            _bar.Query = selected;
        Recompute(selectFromAnchor: true);

        if (replace && _bar.Query.Length > 0) _bar.FocusReplacement();
        else _bar.FocusQuery();
    }

    public void Close()
    {
        if (_tab is not { } tab) return;
        _scopes.Pop(_scope);
        tab.ClearSelection();
        Detach();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Follow the active tab: the bar moves to the newly active editor, or closes when none is left.</summary>
    public void OnActiveTabChanged(EditorTab? tab)
    {
        if (_tab is null || ReferenceEquals(_tab, tab)) return;
        if (tab is null)
        {
            Close();
            return;
        }
        Attach(tab);
        _anchor = tab.SelectionOrigin;
        Recompute(selectFromAnchor: false);
    }

    public void Next()
    {
        if (_tab is not { } tab || _matches.Count == 0) return;
        if (_current >= 0)
        {
            Go(TextSearch.IndexAfter(_matches, _matches[_current].Row, _matches[_current].Column));
            return;
        }
        var (row, col) = tab.SelectionOrigin;
        Go(TextSearch.IndexAtOrAfter(_matches, row, col));
    }

    public void Previous()
    {
        if (_tab is not { } tab || _matches.Count == 0) return;
        var (row, col) = _current >= 0 ? (_matches[_current].Row, _matches[_current].Column) : tab.SelectionOrigin;
        Go(TextSearch.IndexBefore(_matches, row, col));
    }

    /// <summary>Replace the current match and move to the next; with no current match, just select the next one first.</summary>
    public void ReplaceOne()
    {
        if (_tab is not { } tab || !_bar.ReplaceVisible || _matches.Count == 0) return;
        if (_current < 0)
        {
            Next();
            return;
        }

        var match = _matches[_current];
        var replacement = _bar.Replacement;
        Edit(() => tab.Replace(match, replacement));
        Recompute(selectFromAnchor: false);
        // Resume after the inserted text so a replacement containing the query isn't matched again.
        Go(TextSearch.IndexAtOrAfter(_matches, match.Row, match.Column + replacement.Length));
    }

    public void ReplaceAll()
    {
        if (_tab is not { } tab || !_bar.ReplaceVisible || _matches.Count == 0) return;

        var replacement = _bar.Replacement;
        var count = _matches.Count;
        // Back to front, so earlier matches' coordinates stay valid as later ones change length.
        Edit(() =>
        {
            for (var i = _matches.Count - 1; i >= 0; i--)
                tab.Replace(_matches[i], replacement);
        });
        Recompute(selectFromAnchor: false);
        _bar.Status = $"Replaced {count}";
    }

    private void SwitchField()
    {
        if (!_bar.ReplaceVisible) return;
        if (_bar.ReplacementHasFocus) _bar.FocusQuery();
        else _bar.FocusReplacement();
    }

    private void Go(int index)
    {
        if (_tab is not { } tab || index < 0) return;
        _current = index;
        var match = _matches[index];
        _anchor = (match.Row, match.Column);
        tab.Select(match);
        UpdateStatus();
    }

    private void Recompute(bool selectFromAnchor)
    {
        if (_tab is not { } tab) return;

        var previous = _current >= 0 ? _matches[_current] : (TextMatch?)null;
        _matches = TextSearch.FindAll(tab.Lines, _bar.Query);
        tab.SetHighlights(_matches);

        if (selectFromAnchor)
        {
            _current = -1;
            var index = TextSearch.IndexAtOrAfter(_matches, _anchor.Row, _anchor.Column);
            if (index >= 0) Go(index);
            else tab.ClearSelection();
        }
        else
        {
            // An edit elsewhere keeps the current match if it still exists; otherwise there's no current.
            _current = previous is { } p ? IndexOf(_matches, p) : -1;
        }
        UpdateStatus();
    }

    private void UpdateStatus() =>
        _bar.Status = _bar.Query.Length == 0 ? string.Empty
            : _matches.Count == 0 ? "No results"
            : _current >= 0 ? $"{_current + 1} of {_matches.Count}"
            : $"{_matches.Count} results";

    // Our own replacements fire ContentChanged per edit; recompute once afterwards instead.
    private void Edit(Action edit)
    {
        _replacing = true;
        try { edit(); }
        finally { _replacing = false; }
    }

    private void OnContentChanged(object? sender, EventArgs e)
    {
        if (!_replacing) Recompute(selectFromAnchor: false);
    }

    private void Attach(EditorTab tab)
    {
        if (ReferenceEquals(_tab, tab)) return;
        Detach();
        _tab = tab;
        tab.SetHeader(_bar);
        tab.ContentChanged += OnContentChanged;
    }

    private void Detach()
    {
        if (_tab is not { } tab) return;
        tab.ContentChanged -= OnContentChanged;
        tab.SetHighlights([]);
        tab.SetHeader(null);
        _tab = null;
        _matches = [];
        _current = -1;
    }

    private static int IndexOf(IReadOnlyList<TextMatch> matches, TextMatch match)
    {
        for (var i = 0; i < matches.Count; i++)
            if (matches[i] == match) return i;
        return -1;
    }

    public void Dispose()
    {
        Detach();
        _bar.Dispose();
    }
}
