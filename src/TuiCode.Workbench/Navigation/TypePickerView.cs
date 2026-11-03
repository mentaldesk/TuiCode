using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Loading;
using TuiCode.Abstractions;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Languages;

namespace TuiCode.Workbench.Navigation;

/// <summary>A type Go to type found, and its file relative to the folder.</summary>
public sealed record TypeRow(TypeSymbol Type, string Place);

/// <summary>
/// Modal picker for Go to type (<c>gt</c>): the language servers match the filter, so the list shows their answer
/// as it is rather than filtering it again. The host searches as the filter changes and jumps once it closes.
/// </summary>
public sealed class TypePickerView : PickerDialog<TypeRow>
{
    public const string NoTypes = "No types match";

    private const int DialogWidth = 80;

    private readonly TypeList.Columns _columns;

    public TypePickerView(string loading, FileIconStyle icons = FileIconStyle.Off)
        : this(loading, icons, new TypeList.Columns())
    {
    }

    private TypePickerView(string loading, FileIconStyle icons, TypeList.Columns columns)
        : base("Go to type", DialogWidth, 18, loading, r => r.Type.Name, "Go to",
            r => TypeList.Row(r, columns, PickerList.Width(DialogWidth), icons))
    {
        _columns = columns;
        Commands.Register(CancelId, "Cancel", ClearOrCancel);
    }

    internal IReadOnlyList<string> VisibleItems => [.. Shown.Select(r => r.Type.Name)];

    /// <summary>What the list says in place of rows, and the line under them.</summary>
    internal string Says => $"{SubViews.OfType<LoadStateView>().Single().Says}\n{Note}";

    public void ShowTypes(IReadOnlyList<TypeRow> rows, string empty)
    {
        _columns.Fit(rows);
        ShowRows(rows, empty);
    }

    /// <summary>A line under the rows, such as which server is still loading; null takes it away.</summary>
    public void ShowStatus(string? note) => ShowNote(note);

    protected override IReadOnlyList<TypeRow> Match(string query, IReadOnlyList<TypeRow> rows) => rows;

    private void ClearOrCancel()
    {
        if (FilterField.Text.Length > 0) FilterField.Text = "";
        else Cancel();
    }
}

/// <summary>How each row of the <c>gt</c> picker reads: icon and name, then the container and the file in columns. The container gives way to the file.</summary>
internal static class TypeList
{
    private const int Gap = 2;
    private const int MaxName = 28;
    private const int MinContainer = 12;

    public static string Row(TypeRow row, Columns columns, int width, FileIconStyle icons)
    {
        var iconWidth = SymbolIcons.Width(icons);
        var icon = (SymbolIcons.For(row.Type.Kind, icons) ?? "").PadRight(iconWidth);
        var container = Math.Min(columns.Container, Math.Max(MinContainer, width - iconWidth - columns.Name - 2 * Gap - columns.Place));
        var text = Fit(icon + row.Type.Name, iconWidth + columns.Name) + new string(' ', Gap)
            + (container > 0 ? FitStart(row.Type.Container ?? "", container) + new string(' ', Gap) : "")
            + row.Place;
        return Truncate(text, width);
    }

    public sealed class Columns
    {
        public int Name { get; private set; }
        public int Container { get; private set; }
        public int Place { get; private set; }

        public void Fit(IReadOnlyList<TypeRow> rows)
        {
            Name = rows.Count == 0 ? 0 : Math.Min(MaxName, rows.Max(r => r.Type.Name.Length));
            Container = rows.Count == 0 ? 0 : rows.Max(r => r.Type.Container?.Length ?? 0);
            Place = rows.Count == 0 ? 0 : rows.Max(r => r.Place.Length);
        }
    }

    // A namespace's last parts say most, so it loses its start.
    private static string FitStart(string text, int width) =>
        (text.Length <= width ? text : "…" + text[^Math.Max(0, width - 1)..]).PadRight(width);

    private static string Fit(string text, int width) => Truncate(text, width).PadRight(width);

    private static string Truncate(string text, int width) =>
        text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "…";
}
