using System.Reflection;
using System.Runtime.CompilerServices;

namespace TuiCode.Editor;

// Undo is ours, not TG's: see "Multiple cursors and undo" in AGENTS.md.
internal sealed partial class EditorTextView
{
    private sealed record EditGroup(Change[] Changes, Caret[] Before, Caret[] After);

    private readonly record struct Change(int OldStart, string[] Old, int NewStart, string[] New);

    private readonly List<EditGroup> _undo = [];
    private int _undone;
    private readonly List<string> _recorded = [];
    private bool _recordedEdit;

    public EditorTextView()
    {
        AddCommand(Command.Undo, () => Undo());
        AddCommand(Command.Redo, () => Redo());
        AddCommand(Command.NextTabStop, () => Indent());
        AddCommand(Command.PreviousTabStop, () => Outdent());
    }

    public override string Text
    {
        get => base.Text;
        set
        {
            _secondary.Clear();
            base.Text = value;
            _undo.Clear();
            _undone = 0;
            _recorded.Clear();
            _recorded.AddRange(Snapshot.Refresh(GetAllLines()));
        }
    }

    /// <summary>Runs <paramref name="edit"/>, which returns the carets afterwards (primary first), as one undo step.</summary>
    private void Edit(Func<IReadOnlyList<Caret>> edit)
    {
        var before = Carets;
        var viewport = Viewport;
        IReadOnlyList<Caret> after;
        _holdContentsChanged = true;
        _visitingCarets = true;
        try
        {
            after = edit();
        }
        finally
        {
            _holdContentsChanged = false;
            _visitingCarets = false;
        }

        var lines = Snapshot.Refresh(GetAllLines());
        var hunks = LineDiff.Hunks(_recorded, lines);
        if (hunks.Count > 0)
        {
            // Rows other than the cursor's may have changed, which the width cache kept across edits doesn't allow for.
            CachedMaxWidth(ModelField.GetValue(this)!) = -1;
            SetNeedsDraw();
        }
        Viewport = viewport;
        SetCarets(after);
        // Moves while visiting carets aren't reported, so report where the primary caret ended up.
        if (Carets[0].Position != before[0].Position) RaiseUnwrappedCursorPositionChanged(this, null, null);
        if (hunks.Count == 0) return;
        Record(hunks, lines, before);
        RaiseContentsChanged();
    }

    /// <summary>Runs <paramref name="edit"/> at TG's own insertion point and selection as one undo step.</summary>
    internal void EditAtPrimary(Action edit)
    {
        var before = Carets;
        _holdContentsChanged = true;
        try
        {
            edit();
        }
        finally
        {
            _holdContentsChanged = false;
        }
        if (RecordPending(before)) RaiseContentsChanged();
    }

    private bool RecordPending(Caret[] before)
    {
        var lines = Snapshot.Refresh(GetAllLines());
        var hunks = LineDiff.Hunks(_recorded, lines);
        if (hunks.Count == 0) return false;
        Record(hunks, lines, before);
        return true;
    }

    // Raised for an edit already recorded, so OnContentsChanged needn't look for one.
    private void RaiseContentsChanged()
    {
        _recordedEdit = true;
        try
        {
            OnContentsChanged();
        }
        finally
        {
            _recordedEdit = false;
        }
    }

    private void Record(List<Hunk> hunks, IReadOnlyList<string> lines, Caret[] before)
    {
        var changes = hunks
            .Select(h => new Change(h.OldStart, [.. _recorded.GetRange(h.OldStart, h.OldCount)], h.NewStart, Slice(lines, h.NewStart, h.NewCount)))
            .ToArray();
        _undo.RemoveRange(_undo.Count - _undone, _undone);
        _undone = 0;
        _undo.Add(new EditGroup(changes, before, Carets));
        Replay(changes, undo: false, applyToModel: false);

        // TG still records every edit in its own history, which nothing reads now.
        var history = HistoryField.GetValue(this)!;
        HistoryItems(history).Clear();
        HistoryIndex(history) = -1;
    }

    public new bool Undo() => Step(undo: true);

    public new bool Redo() => Step(undo: false);

    private bool Step(bool undo)
    {
        if (ReadOnly || (undo ? _undone == _undo.Count : _undone == 0)) return true;
        var group = undo ? _undo[_undo.Count - 1 - _undone++] : _undo[_undo.Count - _undone--];
        // The carets are set afterwards; until then TG mustn't read a selection the change has invalidated.
        IsSelecting = false;
        Replay(group.Changes, undo, applyToModel: true);
        SetCarets(undo ? group.Before : group.After);
        RaiseContentsChanged();
        return true;
    }

    // Last change first, so each change's start row is still where it was recorded.
    private void Replay(Change[] changes, bool undo, bool applyToModel)
    {
        var model = ModelField.GetValue(this)!;
        for (var i = changes.Length - 1; i >= 0; i--)
        {
            var change = changes[i];
            var (start, count, lines) = undo
                ? (change.NewStart, change.New.Length, change.Old)
                : (change.OldStart, change.Old.Length, change.New);
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
            if (!applyToModel) continue;

            var shared = Math.Min(count, lines.Length);
            for (var k = 0; k < shared; k++)
                ReplaceLine(model, start + k, Cell.ToCellList(lines[k]));
            for (var k = shared; k < count; k++)
                RemoveLine(model, start + shared);
            for (var k = shared; k < lines.Length; k++)
                AddLine(model, start + k, Cell.ToCellList(lines[k]));
        }
        if (!applyToModel) return;
        CachedMaxWidth(model) = -1;
        SetNeedsDraw();
    }

    private static string[] Slice(IReadOnlyList<string> lines, int start, int count)
    {
        var slice = new string[count];
        for (var i = 0; i < count; i++)
            slice[i] = lines[start + i];
        return slice;
    }

    private const string HistoryTextType = "Terminal.Gui.Views.HistoryText, Terminal.Gui";

    private static readonly FieldInfo HistoryField = typeof(TextView).GetField("_historyText", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_historyTextItems")]
    private static extern ref List<HistoryTextItemEventArgs> HistoryItems([UnsafeAccessorType(HistoryTextType)] object history);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_idxHistoryText")]
    private static extern ref int HistoryIndex([UnsafeAccessorType(HistoryTextType)] object history);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "RaiseUnwrappedCursorPositionChanged")]
    private static extern void RaiseUnwrappedCursorPositionChanged(TextView view, int? row, int? column);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "AddLine")]
    private static extern void AddLine([UnsafeAccessorType(TextModelType)] object model, int row, List<Cell> cells);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "RemoveLine")]
    private static extern void RemoveLine([UnsafeAccessorType(TextModelType)] object model, int row);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ReplaceLine")]
    private static extern void ReplaceLine([UnsafeAccessorType(TextModelType)] object model, int row, List<Cell> cells);
}
