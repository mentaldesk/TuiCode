using System.Collections;
using System.Collections.Specialized;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Syntax;

namespace TuiCode.Workbench.Navigation;

/// <summary>The <c>gs</c> picker's rows: the text <see cref="SymbolList"/> renders, then the kind icon in the space it left.</summary>
internal sealed class SymbolListSource(IReadOnlyList<FileSymbol> rows, bool indent, FileIconStyle icons) : IListDataSource
{
    private readonly int _iconWidth = SymbolIcons.Width(icons);
    private IReadOnlyList<string> _rendered = [];
    private int _nameWidth;
    private int _width = -1;

    public event NotifyCollectionChangedEventHandler? CollectionChanged { add { } remove { } }

    public int Count => rows.Count;

    public int MaxItemLength => 0;

    public bool SuspendCollectionChangedEvent { get; set; }

    public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX = 0)
    {
        if (width != _width)
        {
            _rendered = SymbolList.Render(rows, width, indent, _iconWidth);
            _nameWidth = SymbolList.NameWidth(rows, width);
            _width = width;
        }
        listView.Move(col, row);
        listView.AddStr(_rendered[item].PadRight(width));

        var at = SymbolList.IconColumn(rows[item], indent);
        if (SymbolIcons.For(rows[item].Kind, icons) is not { } glyph || at + _iconWidth > _nameWidth) return;
        listView.Move(col + at, row);
        listView.AddStr(glyph);
    }

    public bool IsMarked(int item) => false;

    public void SetMark(int item, bool value) { }

    public IList ToList() => rows.Select(r => r.Name).ToList();

    public void Dispose() { }
}
