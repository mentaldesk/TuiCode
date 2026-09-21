using System.Collections;
using System.Collections.Specialized;
using TuiCode.Abstractions;
using TuiCode.Syntax;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Navigation;

/// <summary>
/// Modal picker for Go to symbol (<c>gs</c>): a filter over the definitions in the active file. It opens
/// before the scan has finished and fills in as <see cref="Advance"/> takes slices of it, so a big file
/// costs a few frames rather than a freeze. Enter jumps; the host does the move and closes the picker.
/// </summary>
public sealed class SymbolPickerView : Window
{
    private const string HintText = "Type to filter · Up/Down/PgUp/PgDn · Enter go to · Esc cancel";

    /// <summary>What an empty scan says, and what <c>gs</c> puts in the status bar for a file with no grammar.</summary>
    public const string NoSymbols = "No symbols in this file";

    private const int DialogWidth = 66;
    private const int DialogHeight = 20;

    private static readonly TimeSpan ScanBudget = TimeSpan.FromMilliseconds(15);

    private readonly TextField _filter;
    private readonly ListView _list;
    private readonly Label _hint;
    private readonly AlertView _alert;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    private readonly SymbolScan _scan;
    private IReadOnlyList<FileSymbol> _visible = [];

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;

    /// <summary>The symbol to jump to; the host moves the cursor and closes the picker.</summary>
    public event EventHandler<FileSymbol>? Submitted;

    public SymbolPickerView(string fileName, SymbolScan scan)
    {
        _scan = scan;
        Title = $"Go to symbol in {fileName}";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        Height = DialogHeight;
        CanFocus = true;

        _filter = new TextField { X = 1, Y = 0, Width = Dim.Fill(1) };
        _filter.TextChanged += (_, _) => ShowEntries(keepSelection: false);

        _list = new ListView { X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(1) };
        _hint = new Label { X = 1, Y = Pos.AnchorEnd(1), Width = Dim.Fill(1), Text = HintText };
        _alert = new AlertView(DialogWidth - 2) { X = 0, Y = Pos.AnchorEnd() };
        Add(_filter, _list, _hint, _alert);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();
        ShowEntries(keepSelection: false);
        ReportEmpty();
    }

    internal string Filter => _filter.Text ?? "";

    internal string Status => _alert.Message;

    internal IReadOnlyList<string> VisibleItems => [.. _visible.Select(s => s.Name)];

    internal int? SelectedItem => _list.SelectedItem;

    /// <summary>Whether the whole file has been scanned; until then rows are still arriving.</summary>
    public bool Scanned => _scan.Done;

    public bool FocusFilter() => _filter.SetFocus();

    /// <summary>Scans the next slice and shows what it found. The host drives this from <c>Iteration</c>.</summary>
    public void Advance()
    {
        if (_scan.Done) return;
        var before = _scan.Symbols.Count;
        _scan.Advance(ScanBudget);
        if (_scan.Symbols.Count != before) ShowEntries(keepSelection: true);
        ReportEmpty();
    }

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.SymbolPickerCancel, OnCancel);
        _scopeCommands.Register(CommandIds.SymbolPickerConfirm, OnConfirm);
        _scopeCommands.Register(CommandIds.SymbolPickerUp, () => MoveSelection(-1));
        _scopeCommands.Register(CommandIds.SymbolPickerDown, () => MoveSelection(1));
        _scopeCommands.Register(CommandIds.SymbolPickerPageUp, () => MoveSelection(-PageHeight));
        _scopeCommands.Register(CommandIds.SymbolPickerPageDown, () => MoveSelection(PageHeight));

        _scopeKeybindings.Bind("Esc", CommandIds.SymbolPickerCancel);
        _scopeKeybindings.Bind("Enter", CommandIds.SymbolPickerConfirm);
        _scopeKeybindings.Bind("CursorUp", CommandIds.SymbolPickerUp);
        _scopeKeybindings.Bind("CursorDown", CommandIds.SymbolPickerDown);
        _scopeKeybindings.Bind("PageUp", CommandIds.SymbolPickerPageUp);
        _scopeKeybindings.Bind("PageDown", CommandIds.SymbolPickerPageDown);
    }

    private void OnCancel()
    {
        if (Filter.Length > 0) _filter.Text = "";
        else Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnConfirm()
    {
        if (_list.SelectedItem is { } i && i < _visible.Count) Submitted?.Invoke(this, _visible[i]);
    }

    private int PageHeight => Math.Max(1, _list.Viewport.Height);

    private void MoveSelection(int delta)
    {
        if (_visible.Count == 0) return;
        _list.SelectedItem = Math.Clamp((_list.SelectedItem ?? -1) + delta, 0, _visible.Count - 1);
    }

    private void ShowEntries(bool keepSelection)
    {
        var previous = keepSelection ? _list.SelectedItem ?? 0 : 0;
        _visible = SymbolList.Filter(_scan.Symbols, Filter);
        _list.Source = new SymbolListSource(_visible);
        _list.SelectedItem = _visible.Count == 0 ? null : Math.Clamp(previous, 0, _visible.Count - 1);
    }

    /// <summary>A file the grammar finds nothing in says so, but only once the scan is over.</summary>
    private void ReportEmpty()
    {
        var empty = _scan.Done && _scan.Symbols.Count == 0;
        if (empty == (_alert.Lines > 0)) return;
        if (empty) Alert(NoSymbols); else ClearAlert();
    }

    private void Alert(string message)
    {
        _alert.Show(message, AlertSeverity.Info);
        Height = DialogHeight + _alert.Lines;
        _list.Height = Dim.Fill(_alert.Lines + 1);
        _hint.Y = Pos.AnchorEnd(_alert.Lines + 1);
        SetNeedsLayout();
    }

    private void ClearAlert()
    {
        _alert.Clear();
        Height = DialogHeight;
        _list.Height = Dim.Fill(1);
        _hint.Y = Pos.AnchorEnd(1);
        SetNeedsLayout();
    }

    private sealed class SymbolListSource(IReadOnlyList<FileSymbol> rows) : IListDataSource
    {
        public event NotifyCollectionChangedEventHandler? CollectionChanged { add { } remove { } }

        public int Count => rows.Count;

        public int MaxItemLength => 0;

        public bool SuspendCollectionChangedEvent { get; set; }

        public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX = 0)
        {
            listView.Move(col, row);
            listView.AddStr(Truncate(rows[item].Name, width).PadRight(width));
        }

        public bool IsMarked(int item) => false;

        public void SetMark(int item, bool value) { }

        public IList ToList() => rows.Select(r => r.Name).ToList();

        public void Dispose() { }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : max <= 1 ? s[..max] : s[..(max - 1)] + "…";
    }
}
