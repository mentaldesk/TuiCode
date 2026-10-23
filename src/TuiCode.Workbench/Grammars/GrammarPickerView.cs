using MentalDesk.Tui.Dialogs;
using TuiCode.Syntax;
using TuiCode.Workbench.Controls;

namespace TuiCode.Workbench.Grammars;

/// <summary>A row of the grammar picker: null <see cref="Grammar"/> is Plain Text.</summary>
internal sealed record GrammarRow(SyntaxLanguage? Grammar, string Name, bool IsCurrent);

/// <summary>Modal grammar list with a filter box, shared by <c>cg</c> and the Grammars settings pane.</summary>
internal sealed class GrammarPickerView : PickerDialog<GrammarRow>
{
    public GrammarPickerView(IEnumerable<SyntaxLanguage> grammars, string title, SyntaxLanguage? current)
        : this(title, Rows(grammars, current))
    {
    }

    private GrammarPickerView(string title, IReadOnlyList<GrammarRow> rows)
        : base(title, 60, 20, rows, r => r.Grammar is null ? r.Name : $"{r.Name} {r.Grammar.Id}", "Choose",
            r => (r.IsCurrent ? "● " : "  ") + r.Name)
    {
        this.Select(Math.Max(0, rows.ToList().FindIndex(r => r.IsCurrent)));
    }

    /// <summary>The grammar chosen, null for Plain Text; only meaningful once <see cref="AppDialog.Confirmed"/>.</summary>
    public SyntaxLanguage? Grammar => Chosen?.Grammar;

    private static IReadOnlyList<GrammarRow> Rows(IEnumerable<SyntaxLanguage> grammars, SyntaxLanguage? current) =>
    [
        new GrammarRow(null, Workbench.PlainTextName, current is null),
        .. grammars
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GrammarRow(g, g.Name, g.Id == current?.Id)),
    ];
}
