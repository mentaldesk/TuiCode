using System.Collections;
using System.Collections.Specialized;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Actions;

internal sealed record ActionRow(string CommandId, string Label, string Bindings, string Mnemonic)
{
    private const int Gap = 2;
    private const int KeysWidth = 18;
    private const int MinLabelWidth = 10;
    private const string MnemonicHeading = "Mnemonic";
    private static readonly int MnemonicWidth = Math.Max(CommandMnemonics.All.Max(m => m.Value.Length), MnemonicHeading.Length);

    public static string Header(int width) => Columns("Command", "Binding", MnemonicHeading, width);

    /// <summary>Label, keys and mnemonic in columns. A narrow list shortens the label first; the mnemonic is never cut.</summary>
    public string Display(int width) => Columns(Label, Bindings, Mnemonic, width);

    // Keys longer than their column take the room from their own row's label.
    private static string Columns(string label, string bindings, string mnemonic, int width)
    {
        var available = Math.Max(width - MnemonicWidth - 2 * Gap, MinLabelWidth);
        var keysColumn = Math.Min(KeysWidth, Math.Max(available - MinLabelWidth, 0));
        var keys = Truncate(bindings, Math.Max(available - MinLabelWidth, 0));
        var labelWidth = available - Math.Max(keysColumn, keys.Length);
        return $"{Truncate(label, labelWidth).PadRight(labelWidth + Gap)}{keys.PadRight(available - labelWidth + Gap)}{mnemonic.PadLeft(MnemonicWidth)}";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : max <= 0 ? "" : s[..(max - 1)] + "…";
}

internal sealed class ActionListSource(IReadOnlyList<ActionRow> rows) : IListDataSource
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
