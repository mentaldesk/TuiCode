using MentalDesk.Tui;
using Terminal.Gui.Text;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Workbench.Focus;
using KeyRow = MentalDesk.Tui.Keys.KeyRow;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench;

public sealed partial class WorkbenchHost
{
    private CompletionSession? _completion;
    private int _completionRequest;
    private bool _afterTypingQueued;
    // The name the list was last closed on with Esc, by accepting or for want of a match: typing more of it doesn't open it again.
    private (EditorTab Tab, int Row, int WordStart)? _completionDismissed;

    private sealed class CompletionSession(EditorTab tab, int row, int wordStart, int asked, CompletionList list, CompletionView view, System.Drawing.Rectangle text)
    {
        public EditorTab Tab { get; } = tab;
        public int Row { get; } = row;
        public int WordStart { get; } = wordStart;
        public int Asked { get; set; } = asked;
        public CompletionList List { get; set; } = list;
        public CompletionView View { get; } = view;
        public System.Drawing.Rectangle Text { get; } = text;
        public LayeredScope? Scope { get; set; }
        public string Shown { get; set; } = "";

        public Dictionary<(string Label, int Kind), string> Details { get; } = [];
        public HashSet<(string Label, int Kind)> Resolving { get; } = [];

        public string? DetailOf(CompletionItem item) => item.Detail ?? Details.GetValueOrDefault((item.Label, item.Kind));
    }

    private static readonly KeyRow[] CompletionKeys =
    [
        new("Tab Enter", "Put in the selected name"), new("↑ ↓ PgUp PgDn", "Choose"), new("Esc", "Close, leaving the text"),
        new("Type", "Narrow the list"), new("Any other key", "Close, then act in the editor"),
    ];

    internal CompletionView? Completion => _completion?.View;

    /// <summary>Show completions (<c>sc</c>): what the language server says fits at the caret, in a list under its line.</summary>
    private void ShowCompletions()
    {
        if (Group.ActiveTab is not { } tab || ReadyLanguageServer(tab) is not { } server) return;
        if (tab.CaretCount > 1)
        {
            _workbench.StatusBar.ShowMessage("Completions need a single caret");
            return;
        }
        RequestCompletions(tab, server, trigger: null, manual: true);
    }

    private void QueueAfterTyping()
    {
        if (_afterTypingQueued) return;
        _afterTypingQueued = true;
        _app.AddTimeout(TimeSpan.Zero, () =>
        {
            _afterTypingQueued = false;
            AfterTyping();
            return false;
        });
    }

    private void AfterTyping()
    {
        if (_completion is not null)
        {
            KeepCompletions();
            return;
        }
        if (!_settings.Editor.SuggestAsYouType || _focus.Region != FocusRegion.Editor || Group.ActiveTab is not { CaretCount: 1 } tab
            || ActiveLanguageServer() is not { State: LanguageServerState.Ready } server)
            return;
        var (line, character) = tab.CursorLine;
        if (Completions.Trigger(line, character, server.TriggerCharacters) is not { } trigger) return;
        var wordStart = Completions.WordStart(line, character);
        if (trigger.Length == 0 && character - wordStart > 1 && _completionDismissed == (tab, tab.CursorRow, wordStart)) return;
        _workbench.Languages?.Flush();
        RequestCompletions(tab, server, trigger.Length == 0 ? null : trigger, manual: false);
    }

    private void RequestCompletions(EditorTab tab, LanguageServer server, string? trigger, bool manual, bool again = false)
    {
        var (row, (line, character)) = (tab.CursorRow, tab.CursorLine);
        var wordStart = Completions.WordStart(line, character);
        var request = ++_completionRequest;
        var status = _workbench.StatusBar;
        server.CompletionAsync(tab.File.FullName, row, character, trigger, again).ContinueWith(answer => _app.Invoke(() =>
        {
            if (_disposed || request != _completionRequest) return;
            if (!answer.IsCompletedSuccessfully)
            {
                if (manual) status.ShowMessage($"Show completions failed: {answer.Exception?.InnerException?.Message}", Severity.Error);
                CloseCompletions();
                return;
            }
            if (Group.ActiveTab != tab || tab.CursorRow != row || Prefix(tab, wordStart) is null)
            {
                CloseCompletions();
                return;
            }
            ShowCompletionList(tab, row, wordStart, character, answer.Result, manual);
        }), TaskScheduler.Default);
    }

    private static string? Prefix(EditorTab tab, int wordStart)
    {
        var (line, character) = tab.CursorLine;
        if (character < wordStart || character > line.Length) return null;
        var prefix = line[wordStart..character];
        return prefix.All(Completions.IsNameChar) ? prefix : null;
    }

    private void ShowCompletionList(EditorTab tab, int row, int wordStart, int asked, CompletionList list, bool manual)
    {
        var prefix = Prefix(tab, wordStart)!;
        var matches = Completions.Narrow(list.Items, prefix);
        if (matches.Count == 0)
        {
            if (manual) _workbench.StatusBar.ShowMessage("No completions here");
            CloseCompletions();
            _completionDismissed = (tab, row, wordStart);
            return;
        }
        if (_completion is { } open && open.Tab == tab && open.Row == row && open.WordStart == wordStart)
        {
            open.List = list;
            open.Asked = asked;
            open.Shown = prefix;
            open.View.Show(matches);
            PlaceCompletions(open, prefix);
            ResolveDetails(open);
            return;
        }
        CloseCompletions();
        CloseQuickInfo();
        if (_workbench.HasDialog || _menu.IsOpen || _focus.Region != FocusRegion.Editor || tab.CaretCount > 1 || tab.CaretOnScreen is null
            || _scopes.Top is not { } below) return;
        CompletionSession? session = null;
        var view = new CompletionView(matches, item => session?.DetailOf(item) ?? item.Detail);
        session = new CompletionSession(tab, row, wordStart, asked, list, view, tab.TextOnScreen) { Shown = prefix };
        view.Cancelled += (_, _) => Dismiss();
        view.Chosen += (_, _) => AcceptCompletion();
        view.Moved += (_, _) => ResolveDetails(session);
        session.Scope = new LayeredScope(view.Scope, below, _ => true, key =>
        {
            if (!IsHelpKey(key) && !KeepsCompletions(key)) CloseCompletions();
        });
        _completion = session;
        PlaceCompletions(session, prefix);
        _workbench.Add(view);
        _scopes.Push(session.Scope);
        ResolveDetails(session);
    }

    // csharp-ls gives an item its detail only when asked to resolve it.
    private void ResolveDetails(CompletionSession session)
    {
        if (ActiveLanguageServer() is not { State: LanguageServerState.Ready } server) return;
        foreach (var item in session.View.InView)
        {
            var key = (item.Label, item.Kind);
            if (item.Detail is not null || !session.List.Sent.TryGetValue(item, out var sent) || !session.Resolving.Add(key)) continue;
            server.CompletionDetailAsync(sent).ContinueWith(answer => _app.Invoke(() =>
            {
                if (_disposed || _completion != session || !answer.IsCompletedSuccessfully || answer.Result is not { } detail) return;
                session.Details[key] = detail;
                session.View.Refresh();
                PlaceCompletions(session, session.Shown);
            }), TaskScheduler.Default);
        }
    }

    private static bool KeepsCompletions(Key key) =>
        key == Key.Backspace || (!key.IsCtrl && !key.IsAlt && key.AsRune.Value is var c && c < char.MaxValue && Completions.IsNameChar((char)c));

    private void PlaceCompletions(CompletionSession session, string prefix)
    {
        if (session.Tab.CaretOnScreen is not { } caret) return;
        var (width, height) = session.View.Wanted;
        var anchor = new System.Drawing.Point(caret.X - prefix.GetColumns() - 3, caret.Y);
        var box = QuickInfo.Place(session.Text, anchor, width, height);
        var origin = _workbench.ScreenToViewport(box.Location);
        var view = session.View;
        (view.X, view.Y, view.Width, view.Height) = (origin.X, origin.Y, box.Width, box.Height);
        view.EnsureSelectedItemVisible();
    }

    private void KeepCompletions()
    {
        if (_completion is not { } session || !ReferenceEquals(_scopes.Top, session.Scope)) return;
        var tab = session.Tab;
        if (Group.ActiveTab != tab || _focus.Region != FocusRegion.Editor || tab.CaretCount > 1 || tab.CursorRow != session.Row
            || tab.TextOnScreen != session.Text || Prefix(tab, session.WordStart) is not { } prefix)
        {
            CloseCompletions();
            return;
        }
        if (prefix == session.Shown) return;
        session.Shown = prefix;
        if (session.List.IsIncomplete && ActiveLanguageServer() is { State: LanguageServerState.Ready } server)
        {
            _workbench.Languages?.Flush();
            RequestCompletions(tab, server, trigger: null, manual: false, again: true);
            return;
        }
        var matches = Completions.Narrow(session.List.Items, prefix);
        if (matches.Count == 0)
        {
            Dismiss();
            return;
        }
        session.View.Show(matches);
        PlaceCompletions(session, prefix);
        ResolveDetails(session);
    }

    private void AcceptCompletion()
    {
        if (_completion is not { } session || !ReferenceEquals(_scopes.Top, session.Scope) || session.View.Selected is not { } match) return;
        var (range, text) = Completions.Apply(match.Item, session.Row, session.WordStart, session.Asked, session.Tab.CursorCharacter);
        var tab = session.Tab;
        Dismiss();
        tab.Replace(range, text);
    }

    private void Dismiss()
    {
        if (_completion is { } session) _completionDismissed = (session.Tab, session.Row, session.WordStart);
        CloseCompletions();
    }

    private void CloseCompletions()
    {
        _completionRequest++;
        if (_completion is not { } session) return;
        _completion = null;
        if (session.Scope is { } scope) _scopes.Pop(scope);
        _workbench.Remove(session.View);
        session.View.Dispose();
    }

    private static bool TypesText(Key key) =>
        !key.IsCtrl && !key.IsAlt && key.AsRune.Value >= ' ' && key.AsRune.Value != 0x7F;
}
