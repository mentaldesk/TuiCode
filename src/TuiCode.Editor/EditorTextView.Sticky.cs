using Terminal.Gui.Drivers;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Editor;

// Sticky lines (#474); see "Sticky lines" in AGENTS.md.
internal sealed partial class EditorTextView
{
    private IReadOnlyList<int> _pinned = [];
    private (int Row, int Column, StickyLines Sticky)? _pinnedFor;
    private bool _drawingPinned;

    /// <summary>The enclosing definitions to pin; <see cref="StickyLines.None"/> pins nothing.</summary>
    public StickyLines Sticky
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field = value;
            SetNeedsDraw();
        }
    } = StickyLines.None;

    /// <summary>Runs before each draw, so the tab can bring <see cref="Sticky"/> up to date.</summary>
    public Action? RefreshingSticky { get; set; }

    /// <summary>The lines pinned over the top rows at the last draw, outermost first.</summary>
    public IReadOnlyList<int> Pinned => _pinned;

    /// <summary>Raised with a pinned line's number when it's clicked.</summary>
    public event EventHandler<int>? PinnedLineClicked;

    /// <summary>How many rows are pinned over a view whose top row is <paramref name="top"/>.</summary>
    internal int PinnedCountAt(int top) => PinnedAt(top).Count;

    private IReadOnlyList<int> PinnedAt(int top)
    {
        if (Sticky.IsEmpty || Viewport.Height < StickyLines.MinHeight || Lines == 0) return [];
        return Sticky.At(top, row => SoftWrap ? _wrap.At(row).Line : Math.Min(row, Lines - 1));
    }

    // A caret that moves up under the pinned rows scrolls the text down to clear them, as does one they grow over.
    private void PinForCaret()
    {
        _pinned = PinnedAt(Viewport.Y);
        var current = (CurrentRow, CurrentColumn, Sticky);
        if (_pinnedFor == current) return;
        _pinnedFor = current;
        if (_pinned.Count == 0) return;

        var row = SoftWrap ? Locate(InsertionPoint).Row : CurrentRow;
        if (row < Viewport.Y || row >= Viewport.Y + _pinned.Count) return;
        var top = Reveal.Below(row, 0, Math.Max((SoftWrap ? _wrap.Rows : Lines) - Viewport.Height, 0), PinnedCountAt);
        if (SoftWrap) ScrollToRow(top);
        else Viewport = Viewport with { Y = top };
        _pinned = PinnedAt(Viewport.Y);
    }

    private void DrawPinned(int right)
    {
        if (_pinned.Count == 0) return;
        var editable = _editable;
        _editable = PinnedAttribute(editable);
        _drawingPinned = true;
        try
        {
            for (var row = 0; row < _pinned.Count; row++)
            {
                var index = _pinned[row];
                var line = GetLine(index);
                if (!SoftWrap)
                {
                    DrawRow(line, index, row, right, FirstVisibleGlyph(line), line.Count);
                    continue;
                }
                var starts = _wrap.Starts(index);
                var col = DrawRow(line, index, row, right, (0, 0, 0), starts.Length > 1 ? starts[1] : line.Count);
                if (starts.Length <= 1) continue;
                SetAttribute(_editable);
                AddStr(Math.Min(col, right - 1), row, "…");
            }
        }
        finally
        {
            _drawingPinned = false;
            _editable = editable;
        }
    }

    /// <summary>The theme's current-line background, or the text's background lifted a little toward its foreground.</summary>
    internal Attribute PinnedAttribute(Attribute editable)
    {
        if (Syntax?.Highlighter.EditorColors.TryGetValue("editor.lineHighlightBackground", out var hex) == true
            && Color.TryParse(hex, out Color? themed) && themed is { } color)
            return editable with { Background = color };
        var (back, fore) = (editable.Background, editable.Foreground);
        return editable with { Background = new Color(Mix(back.R, fore.R), Mix(back.G, fore.G), Mix(back.B, fore.B)) };

        static int Mix(byte from, byte to) => from + (to - from) * 12 / 100;
    }

    // The caret is under a pinned row only when something other than a caret move scrolled it there.
    private void HideCursorUnderPinned()
    {
        if (_pinned.Count == 0 || Cursor.Position is not { } position) return;
        if (ScreenToViewport(position).Y < _pinned.Count) Cursor = Cursor with { Position = null };
    }

    private bool OnPinnedMouse(Mouse mouse)
    {
        const MouseFlags leftButton = MouseFlags.LeftButtonPressed | MouseFlags.LeftButtonReleased | MouseFlags.LeftButtonClicked
                                      | MouseFlags.LeftButtonDoubleClicked | MouseFlags.LeftButtonTripleClicked;
        if ((mouse.Flags & leftButton) == 0 || mouse.Position is not { } point || point.Y < 0 || point.Y >= _pinned.Count)
            return false;
        if (CanFocus && !HasFocus) SetFocus();
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked)) PinnedLineClicked?.Invoke(this, _pinned[point.Y]);
        return true;
    }
}
