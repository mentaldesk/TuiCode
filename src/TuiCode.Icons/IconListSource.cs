using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Terminal.Gui.Text;
using Terminal.Gui.Views;

namespace TuiCode.Icons;

public sealed class IconListSource(IReadOnlyList<string> items, IReadOnlyList<FileIcon?> icons) : IListDataSource
{
    private readonly ListWrapper<string> _text = new(new ObservableCollection<string>(items));

    public event NotifyCollectionChangedEventHandler? CollectionChanged
    {
        add => _text.CollectionChanged += value;
        remove => _text.CollectionChanged -= value;
    }

    public int Count => _text.Count;
    public int MaxItemLength => _text.MaxItemLength;

    public bool SuspendCollectionChangedEvent
    {
        get => _text.SuspendCollectionChangedEvent;
        set => _text.SuspendCollectionChangedEvent = value;
    }

    public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX = 0)
    {
        // Scrolled sideways, the icon would sit on top of the text; draw the text alone.
        if (viewportX > 0 || icons[item] is not { } icon)
        {
            _text.Render(listView, selected, item, col, row, width, viewportX);
            return;
        }

        var rowAttribute = listView.GetCurrentAttribute();
        listView.Move(col, row);
        listView.SetAttribute(IconDrawing.AttributeFor(icon, rowAttribute));
        listView.AddStr(icon.Glyph);
        listView.SetAttribute(rowAttribute);
        listView.AddStr(" ");
        var iconWidth = icon.Glyph.GetColumns() + 1;
        _text.Render(listView, selected, item, col + iconWidth, row, width - iconWidth);
    }

    public bool IsMarked(int item) => _text.IsMarked(item);
    public void SetMark(int item, bool value) => _text.SetMark(item, value);
    public IList ToList() => _text.ToList();
    public void Dispose() => _text.Dispose();
}
