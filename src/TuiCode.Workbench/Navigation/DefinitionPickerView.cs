using MentalDesk.Tui.Dialogs;
using TuiCode.Abstractions;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Languages;

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

/// <summary>Picks one of several definitions, such as a partial class's; the host does the move once it closes.</summary>
public sealed class DefinitionPickerView : PickerDialog<DefinitionRow>
{
    private const int DialogWidth = 66;

    public DefinitionPickerView(string symbol, IReadOnlyList<DefinitionRow> rows)
        : base($"Definitions of {symbol}", DialogWidth, Math.Min(18, rows.Count + 4), rows, r => $"{r.Place}  {r.Code}", "Go to",
            r => r.Display(rows.Max(d => d.Place.Length), PickerList.Width(DialogWidth)))
    {
        Commands.Register(CancelId, "Cancel", ClearOrCancel);
    }

    internal IReadOnlyList<string> VisibleItems => [.. Shown.Select(r => r.Place)];

    private void ClearOrCancel()
    {
        if (FilterField.Text.Length > 0) FilterField.Text = "";
        else Cancel();
    }
}
