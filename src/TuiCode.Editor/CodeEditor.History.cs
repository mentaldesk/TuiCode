namespace TuiCode.Editor;

// Undo is ours, not Editor's, so a step restores every caret: see "Multiple cursors and undo" in AGENTS.md.
internal sealed partial class CodeEditor
{
    internal sealed record EditGroup(Change[] Changes, Caret[] Before, Caret[] After);

    internal readonly record struct Change(int OldStart, string[] Old, int NewStart, string[] New);

    private readonly List<EditGroup> _undo = [];
    private int _undone;
    private readonly List<string> _recorded = [];
    private bool _editing;

    /// <summary>Runs <paramref name="edit"/>, which returns the carets afterwards (primary first), as one undo step.</summary>
    private void Edit(Func<IReadOnlyList<Caret>> edit)
    {
        var before = Carets;
        var viewport = Viewport;
        IReadOnlyList<Caret> after;
        _editing = true;
        _visitingCarets = true;
        try
        {
            after = edit();
            TrimAutoIndents(after);
        }
        finally
        {
            _editing = false;
            _visitingCarets = false;
        }

        var hunks = LineDiff.Hunks(_recorded, _lines);
        if (hunks.Count > 0) SetNeedsDraw();
        Viewport = viewport;
        SetCarets(after);
        // Moves while visiting carets aren't reported, so report where the primary caret ended up.
        if (Carets[0].Position != before[0].Position) CursorMoved?.Invoke(this, Carets[0].Position);
        if (hunks.Count == 0) return;
        Record(hunks, before);
        Edited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Runs <paramref name="edit"/> at Editor's own caret and selection as one undo step.</summary>
    internal void EditAtPrimary(Action edit)
    {
        var before = Carets;
        _editing = true;
        try
        {
            edit();
            TrimAutoIndents(Carets);
        }
        finally
        {
            _editing = false;
        }
        if (RecordPending(before)) Edited?.Invoke(this, EventArgs.Empty);
    }

    private bool RecordPending(Caret[] before)
    {
        var hunks = LineDiff.Hunks(_recorded, _lines);
        if (hunks.Count == 0) return false;
        Record(hunks, before);
        return true;
    }

    private void Record(List<Hunk> hunks, Caret[] before)
    {
        var changes = hunks
            .Select(h => new Change(h.OldStart, [.. _recorded.GetRange(h.OldStart, h.OldCount)], h.NewStart, Slice(_lines, h.NewStart, h.NewCount)))
            .ToArray();
        _undo.RemoveRange(_undo.Count - _undone, _undone);
        _undone = 0;
        _undo.Add(new EditGroup(changes, before, Carets));
        Replay(changes, undo: false, applyToDocument: false);
    }

    /// <summary>The undo and redo steps, for a tab reopened later on the same lines (#578).</summary>
    internal UndoHistory History => new([.. _undo], _undone, [.. _recorded]);

    internal sealed class UndoHistory(EditGroup[] steps, int undone, string[] lines)
    {
        /// <summary>Puts the steps back on <paramref name="editor"/>, unless its lines aren't the ones they were recorded against.</summary>
        internal void RestoreTo(CodeEditor editor)
        {
            if (!lines.SequenceEqual(editor._recorded, StringComparer.Ordinal)) return;
            editor._undo.Clear();
            editor._undo.AddRange(steps);
            editor._undone = undone;
        }
    }

    public bool Undo() => Step(undo: true);

    public bool Redo() => Step(undo: false);

    private bool Step(bool undo)
    {
        if (Refused() || (undo ? _undone == _undo.Count : _undone == 0)) return true;
        var group = undo ? _undo[_undo.Count - 1 - _undone++] : _undo[_undo.Count - _undone--];
        ClearSelection();
        _autoIndents.Clear();
        _editing = true;
        try
        {
            Replay(group.Changes, undo, applyToDocument: true);
        }
        finally
        {
            _editing = false;
        }
        SetCarets(undo ? group.Before : group.After);
        Edited?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // Last change first, so each change's start row is still where it was recorded.
    private void Replay(Change[] changes, bool undo, bool applyToDocument)
    {
        for (var i = changes.Length - 1; i >= 0; i--)
        {
            var change = changes[i];
            var (start, count, lines) = undo
                ? (change.NewStart, change.New.Length, change.Old)
                : (change.OldStart, change.Old.Length, change.New);
            if (applyToDocument) ReplaceLineRange(start, count, lines);
            if (count == lines.Length)
            {
                for (var k = 0; k < count; k++)
                    _recorded[start + k] = lines[k];
            }
            else
            {
                _recorded.RemoveRange(start, count);
                _recorded.InsertRange(start, lines);
            }
        }
        if (applyToDocument) SetNeedsDraw();
    }

    /// <summary>Replaces <paramref name="count"/> lines from <paramref name="start"/> with <paramref name="lines"/> in the document.</summary>
    private void ReplaceLineRange(int start, int count, string[] lines)
    {
        var document = Document!;
        var text = string.Join("\n", lines);
        if (count == 0)
        {
            if (lines.Length == 0) return;
            if (start < Lines)
                document.Insert(document.GetLineByNumber(start + 1).Offset, text + "\n");
            else
                document.Insert(document.TextLength, "\n" + text);
            return;
        }

        var first = document.GetLineByNumber(start + 1);
        var last = document.GetLineByNumber(start + count);
        if (lines.Length > 0)
        {
            document.Replace(first.Offset, last.EndOffset - first.Offset, text);
        }
        else if (start + count < Lines)
        {
            document.Remove(first.Offset, last.NextLine!.Offset - first.Offset);
        }
        else if (start > 0)
        {
            var above = first.PreviousLine!;
            document.Remove(above.EndOffset, last.EndOffset - above.EndOffset);
        }
        else
        {
            document.Remove(0, document.TextLength);
        }
    }

    private static string[] Slice(IReadOnlyList<string> lines, int start, int count)
    {
        var slice = new string[count];
        for (var i = 0; i < count; i++)
            slice[i] = lines[start + i];
        return slice;
    }
}
