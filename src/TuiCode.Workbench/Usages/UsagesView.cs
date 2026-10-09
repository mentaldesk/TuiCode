using System.Globalization;
using System.Text;
using Terminal.Gui.Text;
using TuiCode.Icons;
using TuiCode.Workbench.Languages;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Workbench.Usages;

/// <summary>The sidebar's Usages tab (#455): one symbol's usages, kept until the next Find usages.</summary>
public sealed class UsagesView : View
{
    private readonly Label _header;
    private readonly Label _count;
    private readonly TreeView<UsageNode> _tree;
    private string _title = "Find usages of a symbol to list them here";
    private string? _counted;

    public event EventHandler<SourceLocation>? UsageActivated;

    public string HeaderText => _counted is null ? _title : $"{_title}  {_counted}";
    public IReadOnlyList<UsageFileNode> Files { get; private set; } = [];
    public bool ResultsHaveFocus => _tree.HasFocus;

    internal TreeView<UsageNode> Tree => _tree;

    public UsagesView(FileIcons? icons = null)
    {
        CanFocus = true;
        _header = new Label { X = 0, Y = 0, Width = Dim.Fill(), Height = 1, Text = _title };
        _count = new Label { X = 0, Y = 1, Width = Dim.Fill(), Height = 1, Text = "" };
        _tree = new TreeView<UsageNode>
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            TreeBuilder = new DelegateTreeBuilder<UsageNode>(n => n.Children, n => n.Children.Count > 0),
        };
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
            if (key != Key.Enter) return;
            ActivateSelected();
            key.Handled = true;
        };
    }

    public void ShowSearching(string symbol) => Show($"Finding usages of {symbol}…", null, []);

    public void ShowFailed(string symbol) => Show($"Couldn't find usages of {symbol}", null, []);

    public void ShowUsages(string symbol, IReadOnlyList<UsageFileNode> files)
    {
        if (files.Count == 0) Show($"No usages of {symbol}", null, files);
        else Show($"Usages of {symbol}", UsageTree.Count(files), files);
    }

    /// <summary>The first usage, or the tab itself while there are none.</summary>
    public bool FocusResults()
    {
        if (Files.Count == 0) return SetFocus();
        _tree.SelectedObject ??= Files[0].Children[0];
        return _tree.SetFocus();
    }

    private void Show(string title, string? counted, IReadOnlyList<UsageFileNode> files)
    {
        var hadFocus = _tree.HasFocus;
        Files = files;
        _title = title;
        _counted = counted;
        ShowHeader();
        _tree.ClearObjects();
        _tree.AddObjects(files);
        _tree.ExpandAll();
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
