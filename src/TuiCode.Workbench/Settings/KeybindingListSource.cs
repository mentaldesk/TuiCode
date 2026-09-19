using System.Collections;
using System.Collections.Specialized;

namespace TuiCode.Workbench.Settings;

/// <summary>Lays each row out at the width it's drawn at, so the columns follow the pane when the terminal resizes.</summary>
internal sealed class KeybindingListSource(IReadOnlyList<KeybindingRow> rows) : IListDataSource
{
    public event NotifyCollectionChangedEventHandler? CollectionChanged { add { } remove { } }

    public int Count => rows.Count;

    // Rows never exceed the viewport, so there's nothing to scroll sideways to.
    public int MaxItemLength => 0;

    public bool SuspendCollectionChangedEvent { get; set; }

    public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX = 0)
    {
        listView.Move(col, row);
        listView.AddStr(rows[item].Display(width).PadRight(width));
    }

    public bool IsMarked(int item) => false;

    public void SetMark(int item, bool value) { }

    public IList ToList() => rows.Select(r => r.Label).ToList();

    public void Dispose() { }
}
