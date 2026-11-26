using System.Globalization;
using System.Text;
using Terminal.Gui.Text;
using TuiCode.Abstractions;
using TuiCode.Icons;
using TuiCode.Workbench.Languages;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Workbench.Usages;

/// <summary>The sidebar's Usages tab (#455): one symbol's usages, each row followable into its definition's usages (#713).</summary>
public sealed class UsagesView : View
{
    private const string Spinner = "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏";
    private static readonly TimeSpan SpinnerInterval = TimeSpan.FromMilliseconds(100);

    private readonly VerbatimLabel _header;
    private readonly VerbatimLabel _count;
    private readonly TreeView<UsageNode> _tree;
    private string _title = "Find usages of a symbol to list them here";
    private string? _counted;
    private string _separator = "  ";
    private string? _symbol;
    private int _generation;
    private int _frame;
    private bool _spinning;
    private readonly List<UsageLookup> _looking = [];

    public event EventHandler<SourceLocation>? UsageActivated;

    public event EventHandler<UsageLookup>? Following;

    public string? StopKey { get; set; }

    public bool IsLooking => _looking.Count > 0;

    public string HeaderText => _counted is null ? _title : $"{_title}{_separator}{_counted}";
    public IReadOnlyList<UsageFileNode> Files { get; private set; } = [];
    public bool ResultsHaveFocus => _tree.HasFocus;

    internal TreeView<UsageNode> Tree => _tree;

    public UsagesView(FileIcons? icons = null)
    {
        CanFocus = true;
        _header = new VerbatimLabel { X = 0, Y = 0, Width = Dim.Fill(), Height = 1, Text = _title };
        _count = new VerbatimLabel { X = 0, Y = 1, Width = Dim.Fill(), Height = 1, Text = "" };
        _tree = new TreeView<UsageNode>
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            TreeBuilder = new DelegateTreeBuilder<UsageNode>(n => n.Children, n => n is UsageFileNode or UsageLineNode { CanFollow: true }),
        };
        _tree.Style.ExpandableSymbol = new Rune('▸');
        _tree.Style.CollapseableSymbol = new Rune('▾');
        // Rows are cut to the pane's width as they're drawn, so there's nothing to scroll sideways to.
        _tree.HorizontalScrollBar.VisibilityMode = ScrollBarVisibilityMode.None;
        Add(_header, _count, _tree);

        _tree.DrawLine += (_, e) =>
        {
            var icon = e.Model is UsageFileNode file ? icons?.ForFile(file.Name) : null;
            Fit(e, icon);
        };
        if (icons is not null) icons.Changed += (_, _) => _tree.SetNeedsDraw();
        ViewportChanged += (_, _) => ShowHeader();
        _tree.Activated += (_, _) => ActivateSelected();
        // Same TG quirk as the explorer: Enter maps to Command.Activate but doesn't raise Activated.
        _tree.KeyDown += (_, key) =>
        {
            if (key == Key.Enter) ActivateSelected();
            else if (key == Key.CursorRight && _tree.SelectedObject is UsageLineNode row && StartsLooking(row)) Follow(row);
            else return;
            key.Handled = true;
        };
    }

    public UsageLookup StartLooking(string symbol)
    {
        foreach (var lookup in _looking) lookup.Stop();
        _looking.Clear();
        var started = new UsageLookup(symbol, null, ++_generation);
        _symbol = symbol;
        _looking.Add(started);
        Show(Looking(started), null, []);
        Spin();
        return started;
    }

    /// <summary>False, showing nothing, when <paramref name="lookup"/> was stopped or the tab refilled since.</summary>
    public bool ShowUsages(UsageLookup lookup, string name, IReadOnlyList<UsageFileNode> files, bool textMatches)
    {
        if (!Settle(lookup, UsageLookupState.Found)) return false;
        if (lookup.From is not { } from)
        {
            _symbol = name;
            MarkLoops(files, null);
            var suffix = textMatches ? " · text matches" : "";
            if (files.Count == 0) Show($"No usages of {name}{suffix}", null, files);
            else Show($"Usages of {name}{suffix}", UsageTree.Count(files), files);
            return true;
        }
        MarkLoops(files, from);
        from.Children.Clear();
        if (files.Count == 0) from.Children.Add(new UsageNoteNode(() => $"No usages of {lookup.Name}"));
        else from.Children.AddRange(files);
        Refresh(from);
        return true;
    }

    public bool ShowFailed(UsageLookup lookup)
    {
        if (!Settle(lookup, UsageLookupState.Failed)) return false;
        Say(lookup, $"Couldn't find usages of {lookup.Name}");
        return true;
    }

    public void StopLooking()
    {
        foreach (var lookup in _looking.ToList())
        {
            lookup.Stop();
            Say(lookup, $"Stopped finding usages of {lookup.Name}");
        }
        _looking.Clear();
    }

    internal void Follow(UsageLineNode row)
    {
        if (!StartsLooking(row)) return;
        var lookup = new UsageLookup(row.Definition!.Name, row, _generation);
        row.Lookup = lookup;
        _looking.Add(lookup);
        row.Children.Clear();
        row.Children.Add(new UsageNoteNode(() => Looking(lookup)));
        Refresh(row);
        Spin();
        Following?.Invoke(this, lookup);
    }

    private static bool StartsLooking(UsageLineNode row) =>
        row.CanFollow && row.Lookup is null or { State: UsageLookupState.Stopped or UsageLookupState.Failed };

    private bool Settle(UsageLookup lookup, UsageLookupState state)
    {
        if (lookup.Generation != _generation || lookup.State != UsageLookupState.Looking) return false;
        lookup.State = state;
        _looking.Remove(lookup);
        return true;
    }

    private void Say(UsageLookup lookup, string text)
    {
        if (lookup.From is not { } from)
        {
            Show(text, null, []);
            return;
        }
        from.Children.Clear();
        from.Children.Add(new UsageNoteNode(() => text));
        Refresh(from);
    }

    private string Looking(UsageLookup lookup) =>
        $"{Spinner[_frame % Spinner.Length]} Finding usages of {lookup.Name}…" + (StopKey is { } key ? $"  {key} stop" : "");

    private void MarkLoops(IReadOnlyList<UsageFileNode> files, UsageLineNode? from)
    {
        var path = new HashSet<string>(StringComparer.Ordinal);
        if (_symbol is not null) path.Add(_symbol);
        for (var row = from; row is not null; row = row.Above)
            if (row.Definition is { } definition) path.Add(definition.Name);
        foreach (var usage in files.SelectMany(f => f.Children).OfType<UsageLineNode>())
        {
            usage.Above = from;
            usage.Loops = usage.Definition is { } d && path.Contains(d.Name);
        }
    }

    private void Refresh(UsageLineNode row)
    {
        _tree.RefreshObject(row);
        _tree.Expand(row);
        foreach (var file in row.Children.OfType<UsageFileNode>()) _tree.Expand(file);
        SetNeedsDraw();
    }

    private void Spin()
    {
        if (_spinning || App is not { } app) return;
        _spinning = true;
        app.AddTimeout(SpinnerInterval, () =>
        {
            if (_looking.Count == 0)
            {
                _spinning = false;
                return false;
            }
            _frame++;
            if (_looking.Any(l => l.From is null)) _title = Looking(_looking.First(l => l.From is null));
            ShowHeader();
            _tree.SetNeedsDraw();
            return true;
        });
    }

    /// <summary>What a rename changed: <paramref name="changes"/> edits on the lines in <paramref name="files"/>.</summary>
    public void ShowRenamed(string oldName, string newName, int changes, IReadOnlyList<UsageFileNode> files)
    {
        foreach (var lookup in _looking) lookup.Stop();
        _looking.Clear();
        _generation++;
        Show($"Renamed {oldName} → {newName}", $"{changes} in {files.Count} {Plural.Of(files.Count, "file")}", files, " · ");
    }

    /// <summary>The first usage, or the tab itself while there are none.</summary>
    public bool FocusResults()
    {
        if (Files.Count == 0) return SetFocus();
        _tree.SelectedObject ??= Files[0].Children[0];
        return _tree.SetFocus();
    }

    private void Show(string title, string? counted, IReadOnlyList<UsageFileNode> files, string separator = "  ")
    {
        _separator = separator;
        var hadFocus = _tree.HasFocus;
        Files = files;
        _title = title;
        _counted = counted;
        ShowHeader();
        _tree.ClearObjects();
        _tree.AddObjects(files);
        foreach (var file in files) _tree.Expand(file);
        if (files.Count > 0) _tree.SelectedObject = files[0].Children[0];
        if (hadFocus && files.Count == 0) SetFocus();
        SetNeedsDraw();
    }

    /// <summary>The header on one row, or the count on the row below when the pane is too narrow for both.</summary>
    private void ShowHeader()
    {
        var width = Viewport.Width;
        if (width <= 0 || HeaderText.GetColumns() <= width)
        {
            _header.Text = HeaderText;
            _count.Text = "";
            return;
        }
        _header.Text = Fit(_title, width);
        _count.Text = Fit(_counted ?? "", width);
    }

    private static string Fit(string text, int columns) => text.GetColumns() <= columns ? text : Cut(text, columns);

    private void ActivateSelected()
    {
        switch (_tree.SelectedObject)
        {
            case UsageLineNode usage:
                UsageActivated?.Invoke(this, usage.Location);
                break;
            case UsageFileNode { Children: [UsageLineNode first, ..] }:
                UsageActivated?.Invoke(this, first.Location);
                break;
        }
    }

    /// <summary>Cuts the row's text with an ellipsis to the pane's width, and puts a file's count at its right edge.</summary>
    private void Fit(DrawTreeViewLineEventArgs<UsageNode> e, FileIcon? icon)
    {
        var start = e.IndexOfModelText;
        if (e.Cells is not { } cells || start < 0 || start >= cells.Count) return;

        var row = cells[start].Attribute ?? default;
        var lead = cells.Take(start).ToList();
        var count = e.Model is UsageFileNode file ? file.Count.ToString(CultureInfo.InvariantCulture) : null;
        var used = Columns(lead) + (icon is { } i ? i.Glyph.GetColumns() + 1 : 0) + (count is null ? 0 : count.Length + 1);
        var room = Math.Max(1, _tree.Viewport.Width - used);
        var text = e.Model?.ToString() ?? "";
        if (text.GetColumns() > room) text = Cut(text, room);

        cells.Clear();
        cells.AddRange([.. lead, .. CellsOf(text, row)]);
        if (count is not null)
            cells.AddRange([.. Enumerable.Repeat(Space(row), Math.Max(1, room - text.GetColumns() + 1)), .. CellsOf(count, row)]);
        if (icon is { } glyph) IconDrawing.Prepend(e, glyph);
    }

    private static string Cut(string text, int columns)
    {
        var kept = new StringBuilder();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext() && kept.ToString().GetColumns() + enumerator.GetTextElement().GetColumns() <= columns - 1)
            kept.Append(enumerator.GetTextElement());
        return kept.Append('…').ToString();
    }

    private static int Columns(IEnumerable<Cell> cells) => cells.Sum(c => Math.Max(1, c.Grapheme.GetColumns()));

    private static Cell Space(Attribute attribute) => new() { Grapheme = " ", Attribute = attribute };

    private static IEnumerable<Cell> CellsOf(string text, Attribute attribute)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) yield return new Cell { Grapheme = enumerator.GetTextElement(), Attribute = attribute };
    }
}
