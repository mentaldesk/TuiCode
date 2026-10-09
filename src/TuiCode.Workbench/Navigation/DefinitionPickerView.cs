using System.Collections;
using System.Collections.Specialized;
using TuiCode.Abstractions;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Navigation;

/// <summary>One of several definitions: where it is (<c>Widget.cs:12</c>) and the line there.</summary>
public sealed record DefinitionRow(SourceLocation Location, string Place, string Code)
{
    public string Display(int placeWidth, int width)
    {
        var text = $"{Place.PadRight(placeWidth)}  {Code}";
        return text.Length > width ? text[..Math.Max(0, width - 1)] + "…" : text;
    }
}

/// <summary>Picks one of several definitions, such as a partial class's; the host does the move and closes it.</summary>
public sealed class DefinitionPickerView : Window
{
    private const string HintText = "Type to filter · Up/Down/PgUp/PgDn · Enter go to · Esc cancel";

    private readonly TextField _filter;
    private readonly ListView _list;
    private readonly IReadOnlyList<DefinitionRow> _rows;
    private readonly int _placeWidth;
    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;
    private IReadOnlyList<DefinitionRow> _visible = [];

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;

    public event EventHandler<SourceLocation>? Submitted;

    public DefinitionPickerView(string symbol, IReadOnlyList<DefinitionRow> rows)
    {
        _rows = rows;
        _placeWidth = rows.Max(r => r.Place.Length);
        Title = $"Definitions of {symbol}";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 66;
        Height = Math.Min(20, rows.Count + 4);
        CanFocus = true;

        _filter = new TextField { X = 1, Y = 0, Width = Dim.Fill(1) };
        _filter.TextChanged += (_, _) => ShowEntries();
        _list = new ListView { X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(1) };
        var hint = new Label { X = 1, Y = Pos.AnchorEnd(1), Width = Dim.Fill(1), Text = HintText };
        Add(_filter, _list, hint);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.DefinitionPickerCancel, OnCancel);
        _scopeCommands.Register(CommandIds.DefinitionPickerConfirm, OnConfirm);
        _scopeCommands.Register(CommandIds.DefinitionPickerUp, () => MoveSelection(-1));
        _scopeCommands.Register(CommandIds.DefinitionPickerDown, () => MoveSelection(1));
        _scopeCommands.Register(CommandIds.DefinitionPickerPageUp, () => MoveSelection(-PageHeight));
        _scopeCommands.Register(CommandIds.DefinitionPickerPageDown, () => MoveSelection(PageHeight));
        _scopeKeybindings.Bind("Esc", CommandIds.DefinitionPickerCancel);
        _scopeKeybindings.Bind("Enter", CommandIds.DefinitionPickerConfirm);
        _scopeKeybindings.Bind("CursorUp", CommandIds.DefinitionPickerUp);
        _scopeKeybindings.Bind("CursorDown", CommandIds.DefinitionPickerDown);
        _scopeKeybindings.Bind("PageUp", CommandIds.DefinitionPickerPageUp);
        _scopeKeybindings.Bind("PageDown", CommandIds.DefinitionPickerPageDown);
        ShowEntries();
    }

    internal string Filter => _filter.Text ?? "";

    internal IReadOnlyList<string> VisibleItems => [.. _visible.Select(r => r.Place)];

    internal int? SelectedItem => _list.SelectedItem;

    public bool FocusFilter() => _filter.SetFocus();

    private void OnCancel()
    {
        if (Filter.Length > 0) _filter.Text = "";
        else Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnConfirm()
    {
        if (_list.SelectedItem is { } i && i < _visible.Count) Submitted?.Invoke(this, _visible[i].Location);
    }

    private int PageHeight => Math.Max(1, _list.Viewport.Height);

    private void MoveSelection(int delta)
    {
        if (_visible.Count == 0) return;
        _list.SelectedItem = Math.Clamp((_list.SelectedItem ?? -1) + delta, 0, _visible.Count - 1);
    }

    private void ShowEntries()
    {
        var filter = Filter.Trim();
        _visible = [.. _rows.Where(r => filter.Length == 0
            || r.Place.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || r.Code.Contains(filter, StringComparison.OrdinalIgnoreCase))];
        _list.Source = new DefinitionListSource(_visible, _placeWidth);
        _list.SelectedItem = _visible.Count == 0 ? null : 0;
    }

    private sealed class DefinitionListSource(IReadOnlyList<DefinitionRow> rows, int placeWidth) : IListDataSource
    {
        public event NotifyCollectionChangedEventHandler? CollectionChanged { add { } remove { } }

        public int Count => rows.Count;

        public int MaxItemLength => 0;

        public bool SuspendCollectionChangedEvent { get; set; }

        public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX = 0)
        {
            listView.Move(col, row);
            listView.AddStr(rows[item].Display(placeWidth, width).PadRight(width));
        }

        public bool IsMarked(int item) => false;

        public void SetMark(int item, bool value) { }

        public IList ToList() => rows.Select(r => r.Place).ToList();

        public void Dispose() { }
    }
}
