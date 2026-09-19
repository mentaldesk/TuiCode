using Terminal.Gui.Text;

namespace TuiCode.Editor;

// Tab and Shift+Tab (#14): TG's own only insert and remove tab characters.
internal sealed partial class EditorTextView
{
    public bool InsertSpaces { get; set; }

    private bool Indent()
    {
        if (!TabKeyAddsTab || ReadOnly) return false;
        if (!InsertSpaces)
        {
            InsertText("\t");
            return true;
        }
        if (IsSelecting && (SelectionStartRow != CurrentRow || SelectionStartColumn != CurrentColumn))
            DeleteCharLeft();
        InsertText(new string(' ', TabWidth - DisplayColumn() % TabWidth));
        return true;
    }

    // Removes a tab, or the spaces back to the previous tab stop, just left of the caret.
    private bool Outdent()
    {
        if (!TabKeyAddsTab || ReadOnly) return false;
        if (IsSelecting || CurrentColumn == 0) return true;

        var line = GetLine(CurrentRow);
        var col = Math.Min(CurrentColumn, line.Count);
        if (line[col - 1].Grapheme == "\t")
        {
            DeleteCharLeft();
            return true;
        }

        var toStop = DisplayColumn() % TabWidth is var partial and > 0 ? partial : TabWidth;
        var spaces = 0;
        while (spaces < toStop && col - spaces > 0 && line[col - spaces - 1].Grapheme == " ")
            spaces++;
        for (var i = 0; i < spaces; i++)
            DeleteCharLeft();
        return true;
    }

    private int DisplayColumn()
    {
        var line = GetLine(CurrentRow);
        var width = 0;
        for (var i = 0; i < CurrentColumn && i < line.Count; i++)
        {
            var grapheme = line[i].Grapheme;
            width += grapheme == "\t" ? TabWidth - width % TabWidth : Math.Max(grapheme.GetColumns(), 1);
        }
        return width;
    }
}
