using Terminal.Gui.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Point = System.Drawing.Point;

namespace TuiCode.Editor;

// Merge conflicts (#693): see "Merge conflicts" in AGENTS.md.
internal sealed partial class CodeEditor
{
    private static readonly (Color Color, int Percent) DefaultCurrentTint = (new Color(0x40, 0xC8, 0xAE), 30);
    private static readonly (Color Color, int Percent) DefaultIncomingTint = (new Color(0x40, 0xA6, 0xFF), 30);

    private int _markerLines;
    private IReadOnlyList<MergeConflict>? _conflicts;
    private Attribute _currentTint;
    private Attribute _incomingTint;
    private Attribute _faint;
    private readonly List<(int Row, int From, int To, MergeConflict Conflict, ConflictKeep Keep)> _keepWords = [];

    /// <summary>The file's conflict blocks, read from its markers; empty without a scan while no line is a marker.</summary>
    public IReadOnlyList<MergeConflict> Conflicts => _markerLines == 0 ? [] : _conflicts ??= MergeConflicts.Find(_lines);

    public MergeConflict? ConflictAt(int line) => MergeConflicts.At(Conflicts, line);

    private void CountMarkers(IEnumerable<string> lines, int sign)
    {
        foreach (var line in lines)
            if (MergeConflicts.IsMarker(line)) _markerLines += sign;
        _conflicts = null;
    }

    /// <summary>Replaces <paramref name="conflict"/>, markers and base included, with the side or sides kept, as one undo step.</summary>
    public void KeepConflict(MergeConflict conflict, ConflictKeep keep)
    {
        if (Refused()) return;
        var kept = conflict.Kept(_lines, keep);
        Edit(() =>
        {
            ReplaceLineRange(conflict.Start, conflict.End - conflict.Start + 1, [.. kept]);
            return [new Caret(new Point(0, Math.Min(conflict.Start, Lines - 1)))];
        });
    }

    private void PrepareConflicts()
    {
        _keepWords.Clear();
        if (Conflicts.Count == 0) return;
        _currentTint = Tinted("merge.currentContentBackground", DefaultCurrentTint);
        _incomingTint = Tinted("merge.incomingContentBackground", DefaultIncomingTint);
        _faint = _editable with { Style = _editable.Style | TextStyle.Faint };
    }

    /// <summary>What a conflict's line is drawn over: a side's tint, faint for the base, or null for the file's own colours.</summary>
    private Attribute? ConflictRow(int line)
    {
        if (_drawingPinned || Conflicts.Count == 0 || ConflictAt(line) is not { } conflict) return null;
        if (line > conflict.Start && line < (conflict.Base ?? conflict.Separator)) return _currentTint;
        if (line > conflict.Separator && line < conflict.End) return _incomingTint;
        if (conflict.Base is { } at && line >= at && line < conflict.Separator) return _faint;
        return null;
    }

    /// <summary>
    /// After a <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> line, faint, which side it is and the three keep words; after a
    /// <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> line, which side that is.
    /// </summary>
    private void DrawConflictHint(int line, int row, int col, int right)
    {
        if (_drawingPinned || Conflicts.Count == 0 || ConflictAt(line) is not { } conflict) return;
        if (line != conflict.Start && line != conflict.End) return;
        if (!SoftWrap) col = ColumnsBefore(Graphemes(line), LineLength(line)) - Viewport.X;
        col += 4;

        if (line == conflict.End)
        {
            Write($"Incoming change ({conflict.IncomingName})", _faint, null);
            return;
        }
        Write($"Current change ({conflict.CurrentName})", _faint, null);
        var word = _editable with { Style = _editable.Style | TextStyle.Underline };
        foreach (var (text, keep) in (ReadOnlySpan<(string, ConflictKeep)>)
                 [("Keep current", ConflictKeep.Current), ("Keep incoming", ConflictKeep.Incoming), ("Keep both", ConflictKeep.Both)])
        {
            Write(" · ", _faint, null);
            Write(text, word, keep);
        }

        void Write(string text, Attribute attribute, ConflictKeep? keep)
        {
            var from = col;
            SetAttribute(attribute);
            foreach (var grapheme in Split(text))
            {
                if (col >= right) break;
                if (col >= 0) AddStr(col, row, grapheme);
                col += Math.Max(grapheme.GetColumns(), 1);
            }
            if (keep is { } chosen && Math.Min(col, right) > Math.Max(from, 0))
                _keepWords.Add((row, Math.Max(from, 0), Math.Min(col, right), conflict, chosen));
        }
    }

    // A click on a keep word keeps that side; pressing or releasing on one does nothing else.
    private bool OnKeepWordMouse(Mouse mouse)
    {
        const MouseFlags leftButton = MouseFlags.LeftButtonPressed | MouseFlags.LeftButtonReleased | MouseFlags.LeftButtonClicked
                                      | MouseFlags.LeftButtonDoubleClicked | MouseFlags.LeftButtonTripleClicked;
        if ((mouse.Flags & leftButton) == 0 || mouse.Position is not { } point) return false;
        foreach (var (row, from, to, conflict, keep) in _keepWords)
        {
            if (point.Y != row || point.X < from || point.X >= to) continue;
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                if (CanFocus && !HasFocus) SetFocus();
                KeepConflict(conflict, keep);
            }
            return true;
        }
        return false;
    }

    private Attribute Tinted(string key, (Color Color, int Percent) fallback)
    {
        var (tint, percent) = Syntax?.Highlighter.EditorColors.TryGetValue(key, out var hex) == true && ParseRgba(hex) is { } themed
            ? themed
            : fallback;
        var back = _editable.Background;
        return _editable with
        {
            Background = new Color(Mix(back.R, tint.R, percent), Mix(back.G, tint.G, percent), Mix(back.B, tint.B, percent)),
        };

        static int Mix(byte from, byte to, int percent) => from + (to - from) * percent / 100;
    }

    // Themes give these with an alpha (#RRGGBBAA), which a terminal cell can't take, so it's blended over the text's background.
    private static (Color Color, int Percent)? ParseRgba(string hex)
    {
        if (hex.Length == 9 && hex[0] == '#' && Color.TryParse(hex[..7], out Color? rgb) && rgb is { } color
            && int.TryParse(hex.AsSpan(7), System.Globalization.NumberStyles.HexNumber, null, out var alpha))
            return (color, alpha * 100 / 255);
        return Color.TryParse(hex, out Color? solid) && solid is { } opaque ? (opaque, 100) : null;
    }
}
