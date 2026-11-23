using System.Collections;
using System.Collections.Specialized;
using Terminal.Gui.Configuration;
using Terminal.Gui.Text;
using TuiCode.Abstractions;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Services;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Workbench.Navigation;

/// <summary>The list of what fits at the caret. It never takes focus: the host feeds it its keys and closes it.</summary>
public sealed class CompletionView : ListView
{
    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;
    private readonly Func<CompletionItem, string?> _detail;
    private Rows _rows;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;

    public event EventHandler? Chosen;

    /// <summary>Raised when the selection moves, which can bring other rows into view.</summary>
    public event EventHandler? Moved;

    /// <param name="detail">Each item's detail, where it's known beyond what the server first sent.</param>
    public CompletionView(IReadOnlyList<CompletionMatch> matches, Func<CompletionItem, string?>? detail = null)
    {
        CanFocus = false;
        SchemeName = "Menu";
        _detail = detail ?? (item => item.Detail);
        _rows = new Rows(matches, _detail);
        Source = _rows;
        SelectedItem = 0;

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.CompletionClose, () => Cancelled?.Invoke(this, EventArgs.Empty));
        _scopeCommands.Register(CommandIds.CompletionAccept, () => Chosen?.Invoke(this, EventArgs.Empty));
        _scopeCommands.Register(CommandIds.CompletionUp, () => Step(() => MoveUp(false)));
        _scopeCommands.Register(CommandIds.CompletionDown, () => Step(() => MoveDown(false)));
        _scopeCommands.Register(CommandIds.CompletionPageUp, () => Step(() => MovePageUp(false)));
        _scopeCommands.Register(CommandIds.CompletionPageDown, () => Step(() => MovePageDown(false)));
        _scopeKeybindings.Bind("Esc", CommandIds.CompletionClose);
        _scopeKeybindings.Bind("Tab", CommandIds.CompletionAccept);
        _scopeKeybindings.Bind("Enter", CommandIds.CompletionAccept);
        _scopeKeybindings.Bind("CursorUp", CommandIds.CompletionUp);
        _scopeKeybindings.Bind("CursorDown", CommandIds.CompletionDown);
        _scopeKeybindings.Bind("PageUp", CommandIds.CompletionPageUp);
        _scopeKeybindings.Bind("PageDown", CommandIds.CompletionPageDown);
    }

    public IReadOnlyList<CompletionMatch> Matches => _rows.Matches;

    public CompletionMatch? Selected => SelectedItem is { } i && i >= 0 && i < _rows.Matches.Count ? _rows.Matches[i] : null;

    /// <summary>The box's width for its rows, and its height, at most <see cref="Completions.MaxHeight"/>.</summary>
    public (int Width, int Height) Wanted => (_rows.Width, Math.Min(_rows.Matches.Count, Completions.MaxHeight));

    /// <summary>Shows <paramref name="matches"/> instead, from the best match.</summary>
    public void Show(IReadOnlyList<CompletionMatch> matches)
    {
        _rows = new Rows(matches, _detail);
        Source = _rows;
        SelectedItem = matches.Count == 0 ? null : 0;
        Viewport = Viewport with { Y = 0 };
    }

    /// <summary>Draws the rows again, as details arrive, leaving the selection and scroll where they are.</summary>
    public void Refresh()
    {
        var (selected, top) = (SelectedItem, Viewport.Y);
        _rows = new Rows(_rows.Matches, _detail);
        Source = _rows;
        SelectedItem = selected;
        Viewport = Viewport with { Y = top };
    }

    /// <summary>The items in view now.</summary>
    public IEnumerable<CompletionItem> InView =>
        _rows.Matches.Skip(Viewport.Y).Take(Math.Max(Viewport.Height, Completions.MaxHeight)).Select(m => m.Item);

    private void Step(Action move)
    {
        move();
        SetNeedsDraw();
        Moved?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Rows(IReadOnlyList<CompletionMatch> matches, Func<CompletionItem, string?> detailOf) : IListDataSource
    {
        private readonly (int Label, int Row) _widths = Completions.Widths(matches, detailOf);

        public IReadOnlyList<CompletionMatch> Matches => matches;

        public int Width => _widths.Row;

        public event NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add { }
            remove { }
        }

        public int Count => matches.Count;

        public int MaxItemLength => _widths.Row;

        public bool SuspendCollectionChangedEvent { get; set; }

        public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX = 0)
        {
            var match = matches[item];
            var normal = listView.GetAttributeForRole(item == listView.SelectedItem ? VisualRole.Focus : VisualRole.Normal);
            var accent = SchemeManager.TryGetScheme("Accent", out var scheme) ? normal with { Foreground = scheme.HotNormal.Foreground } : normal;
            var dim = normal with { Foreground = listView.GetAttributeForRole(VisualRole.Disabled).Foreground };
            listView.Move(col, row);
            listView.SetAttribute(normal);
            var cells = new List<(string Text, Attribute Colour)> { (" ", normal), (Completions.KindMark(match.Item.Kind), normal), (" ", normal) };
            var label = Fit(match.Item.Label, _widths.Label);
            var marks = match.Marks.ToHashSet();
            for (var i = 0; i < label.Length; i++) cells.Add((label[i].ToString(), marks.Contains(i) ? accent : normal));
            cells.Add((new string(' ', Math.Max(_widths.Label - label.GetColumns(), 0)), normal));
            var detail = Completions.Detail(detailOf(match.Item));
            if (detail.Length > 0)
            {
                cells.Add(("  ", normal));
                var used = cells.Sum(c => c.Text.GetColumns());
                cells.Add((Fit(detail, Math.Max(width - used - 1, 0)), dim));
            }
            var left = width;
            foreach (var (text, colour) in cells)
            {
                if (left <= 0) break;
                var piece = Fit(text, left, ellipsis: false);
                listView.SetAttribute(colour);
                listView.AddStr(piece);
                left -= piece.GetColumns();
            }
            listView.SetAttribute(normal);
            if (left > 0) listView.AddStr(new string(' ', left));
        }

        private static string Fit(string text, int width, bool ellipsis = true)
        {
            if (text.GetColumns() <= width) return text;
            if (width <= 0) return "";
            var result = "";
            foreach (var rune in text.EnumerateRunes())
            {
                var next = result + rune;
                if (next.GetColumns() > width - (ellipsis ? 1 : 0)) break;
                result = next;
            }
            return ellipsis ? result + "…" : result;
        }

        public bool IsMarked(int item) => false;

        public void SetMark(int item, bool value) { }

        public IList ToList() => matches.Select(m => m.Item.Label).ToList();

        public void Dispose() { }
    }
}
