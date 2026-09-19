using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Terminal.Gui.Text;
using TuiCode.Abstractions;
using TuiCode.Syntax;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Editor;

public sealed class EditorTab : FrameView
{
    private readonly EditorTextView _textView;
    private readonly EditorGutter _gutter;
    private readonly SyntaxHighlighter? _syntax;
    private bool _grammarChosen;
    private View? _header;
    private readonly string _eol;
    private bool _dirty;

    public IFileInfo File { get; private set; }
    public bool IsDirty => _dirty;

    public string Content
    {
        get => _textView.Text;
        set
        {
            _textView.Text = value;
            OnEdited();
        }
    }

    public int CursorRow => _textView.CurrentRow;
    public int CursorColumn => _textView.CurrentColumn;

    public event EventHandler? DirtyChanged;

    /// <summary>Raised on every edit, whether typed, pasted, replaced or set via <see cref="Content"/>.</summary>
    public event EventHandler? ContentChanged;
    public event EventHandler? Saved;

    /// <summary>
    /// Raised whenever the insertion point moves, carrying the position in the file's own
    /// (unwrapped) model coordinates — the raw source for cursor-location history (#35).
    /// </summary>
    public event EventHandler<(int Row, int Column)>? CursorMoved;

    public event EventHandler? GrammarChanged;

    public EditorTab(IFileInfo file, SyntaxHighlighter? syntax = null)
    {
        File = file;
        _syntax = syntax;
        BorderStyle = LineStyle.None;

        var initial = file.FileSystem.File.ReadAllText(file.FullName);
        _eol = DetectEol(initial);

        _textView = new EditorTextView
        {
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Text = initial,
            Syntax = syntax?.CreateCache(syntax.LanguageForFile(file.Name)),
        };
        Settings = EditorSettings.Default;
        _gutter = new EditorGutter(_textView, syntax) { X = 0, Y = 0, Height = Dim.Fill() };
        _textView.X = Pos.Right(_gutter);
        // Subscribe AFTER setting initial text so the load doesn't mark dirty.
        _textView.ContentsChanged += (_, _) => OnEdited();
        // Point is (X=column, Y=row). Re-expose in (row, column) order to match the rest of the editor API.
        _textView.UnwrappedCursorPositionChanged += (_, point) =>
        {
            if (!_textView.IsVisitingCarets) CursorMoved?.Invoke(this, (point.Y, point.X));
        };
        Add(_gutter, _textView);

        UpdateTitle();
    }

    public EditorSettings Settings
    {
        get;
        set
        {
            field = value;
            _textView.TabWidth = value.IndentSize;
            _textView.InsertSpaces = value.InsertSpaces;
        }
    }

    public bool GutterVisible
    {
        get => _gutter.Visible;
        set
        {
            _gutter.Visible = value;
            _textView.X = value ? Pos.Right(_gutter) : 0;
            SetNeedsLayout();
        }
    }

    internal IReadOnlyList<LineChange> LineChanges => _gutter.Changes;

    /// <summary>Whether syntax colouring is available at all; without it every tab is plain text.</summary>
    public bool HasSyntax => _syntax is not null;

    /// <summary>The grammar colouring this tab, or null for plain text.</summary>
    public SyntaxLanguage? Grammar => _textView.Syntax?.Language;

    /// <summary>Pins this tab to <paramref name="grammar"/> (null for plain text), ignoring associations.</summary>
    public void SetGrammar(SyntaxLanguage? grammar)
    {
        _grammarChosen = true;
        ApplyGrammar(grammar);
    }

    /// <summary>Re-pick the grammar from the current associations, unless one was chosen for this tab.</summary>
    public void InferGrammar()
    {
        if (!_grammarChosen && _syntax is not null)
            ApplyGrammar(_syntax.LanguageForFile(File.Name));
    }

    private void ApplyGrammar(SyntaxLanguage? grammar)
    {
        if (_syntax is null || Equals(Grammar, grammar)) return;
        _textView.Syntax = _syntax.CreateCache(grammar);
        GrammarChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Follow a rename or move; the buffer is untouched.</summary>
    internal void Relocate(IFileInfo file)
    {
        File = file;
        UpdateTitle();
        InferGrammar();
    }

    public bool FocusContent() => _textView.SetFocus();
    public bool ContentHasFocus => _textView.HasFocus;

    /// <summary>
    /// Move the cursor to the given (zero-based) row/column. Out-of-range values clamp
    /// to the nearest valid position.
    /// </summary>
    public void MoveCursor(int row, int col)
    {
        row = Math.Clamp(row, 0, Math.Max(_textView.Lines - 1, 0));
        col = Math.Clamp(col, 0, _textView.GetLine(row).Count);
        _textView.RemoveSecondaryCarets();
        _textView.InsertionPoint = new System.Drawing.Point(col, row);
    }

    /// <summary>
    /// The buffer's lines as the editor models them (terminators excluded). Columns in a
    /// <see cref="TextMatch"/> computed against these line up with <see cref="Select"/> / <see cref="Replace"/>.
    /// </summary>
    public IReadOnlyList<string> Lines => _textView.LineStrings;

    /// <summary>Like <see cref="Lines"/> but cheap to re-read; later reads update the returned list in place.</summary>
    internal IReadOnlyList<string> SnapshotLines => _textView.Snapshot.Refresh(_textView.GetAllLines());

    public string SelectedText => _textView.SelectedText;

    public int CaretCount => _textView.CaretCount;

    public DocumentStats CountDocument() => DocumentStats.Of(_textView.Snapshot.Refresh(_textView.GetAllLines()));

    /// <summary>The counts across every caret's selection, or null when nothing is selected.</summary>
    public DocumentStats? CountSelection()
    {
        var ranges = _textView.Carets.Where(c => c.Start != c.End).Select(c => (c.Start, c.End)).ToArray();
        return ranges.Length == 0 ? null : DocumentStats.Of(_textView.Snapshot.Refresh(_textView.GetAllLines()), ranges);
    }

    /// <summary>What <see cref="Save"/> will write: the Line endings setting, or on Auto the file's own.</summary>
    public LineEnding LineEnding => Settings.LineEnding switch
    {
        LineEnding.Auto => _eol == "\r\n" ? LineEnding.CRLF : LineEnding.LF,
        var chosen => chosen,
    };

    public void MoveLines(LineDirection direction) => _textView.MoveLines(direction);

    public void DuplicateLines(LineDirection direction) => _textView.DuplicateLines(direction);

    public bool HasSecondaryCursors => _textView.HasSecondaryCarets;

    /// <summary>Whether extending the selection sweeps a rectangle rather than a run of text (#114).</summary>
    public bool ColumnSelect
    {
        get => _textView.ColumnSelect;
        set => _textView.ColumnSelect = value;
    }

    /// <summary>Add a cursor on the line above or below every cursor.</summary>
    public void AddCursor(LineDirection direction) => _textView.AddCaret(direction);

    public void RemoveSecondaryCursors() => _textView.RemoveSecondaryCarets();

    public void SelectNextOccurrence() => _textView.SelectNextOccurrence();

    public void SelectPreviousOccurrence() => _textView.SelectPreviousOccurrence();

    public void SelectAllOccurrences() => _textView.SelectAllOccurrences();

    /// <summary>
    /// Start of the selection, or the cursor when nothing is selected — with the column in UTF-16 chars,
    /// comparable with <see cref="TextMatch"/> positions.
    /// </summary>
    public (int Row, int Column) SelectionOrigin
    {
        get
        {
            var (row, col) = (_textView.CurrentRow, _textView.CurrentColumn);
            if (_textView.IsSelecting)
            {
                var (startRow, startCol) = (_textView.SelectionStartRow, _textView.SelectionStartColumn);
                if (startRow < row || (startRow == row && startCol < col))
                    (row, col) = (startRow, startCol);
            }
            return (row, ToCharColumn(row, col));
        }
    }

    /// <summary>
    /// Dock <paramref name="header"/> (e.g. the find bar) above the text, pushing the text down; null removes it.
    /// The header is not disposed on removal — its owner is.
    /// </summary>
    public void SetHeader(View? header)
    {
        if (ReferenceEquals(_header, header)) return;
        if (_header is not null) Remove(_header);
        _header = header;
        if (header is not null)
        {
            header.X = 0;
            header.Y = 0;
            header.Width = Dim.Fill();
            Add(header);
        }
        _textView.Y = _gutter.Y = header is null ? 0 : Pos.Bottom(header);
        SetNeedsLayout();
    }

    /// <summary>Paint every match with the highlight colour (find-in-file). An empty set clears it.</summary>
    public void SetHighlights(IEnumerable<TextMatch> matches)
    {
        _textView.Highlights.Clear();
        foreach (var m in matches)
        {
            if (!_textView.Highlights.TryGetValue(m.Row, out var ranges))
                _textView.Highlights[m.Row] = ranges = [];
            ranges.Add((ToCellColumn(m.Row, m.Column), ToCellColumn(m.Row, m.End)));
        }
        _textView.SetNeedsDraw();
    }

    /// <summary>Select <paramref name="match"/>, leaving the cursor at its end and scrolling it into view.</summary>
    public void Select(TextMatch match)
    {
        var start = ToCellColumn(match.Row, match.Column);
        var end = ToCellColumn(match.Row, match.End);
        _textView.RemoveSecondaryCarets();
        _textView.InsertionPoint = new System.Drawing.Point(start, match.Row);
        // Row before column: the column setter clamps against the selection-start row's line.
        _textView.SelectionStartRow = match.Row;
        _textView.SelectionStartColumn = start;
        _textView.InsertionPoint = new System.Drawing.Point(end, match.Row);
    }

    public void ClearSelection()
    {
        _textView.IsSelecting = false;
        _textView.SetNeedsDraw();
    }

    /// <summary>Replace the text covered by <paramref name="match"/>, as one undo step.</summary>
    public void Replace(TextMatch match, string replacement)
    {
        Select(match);
        _textView.EditAtPrimary(() =>
        {
            if (match.Length > 0) _textView.DeleteCharLeft();
            if (replacement.Length > 0) _textView.InsertText(replacement);
        });
        _textView.IsSelecting = false;
    }

    private int ToCharColumn(int row, int cellColumn)
    {
        var line = _textView.GetLine(row);
        var chars = 0;
        for (var i = 0; i < Math.Min(cellColumn, line.Count); i++)
            chars += line[i].Grapheme.Length;
        return chars;
    }

    // TextMatch columns count UTF-16 chars; the TextView model counts grapheme cells.
    private int ToCellColumn(int row, int charColumn)
    {
        var line = _textView.GetLine(row);
        var chars = 0;
        for (var i = 0; i < line.Count; i++)
        {
            if (chars >= charColumn) return i;
            chars += line[i].Grapheme.Length;
        }
        return line.Count;
    }

    public void Save()
    {
        // TextView.Text joins its lines with Environment.NewLine, so on Windows the
        // buffer comes back CRLF regardless of the file's real endings. Re-emit using
        // the EOL we detected on load so a file's line-ending style round-trips
        // unchanged on every OS (matches VS Code's preserve-on-save behaviour).
        var eol = LineEnding == LineEnding.CRLF ? "\r\n" : "\n";
        var content = Normalize(_textView.Text, eol);
        if (Settings.InsertFinalNewline && content.Length > 0 && !content.EndsWith(eol, StringComparison.Ordinal))
            content += eol;
        File.FileSystem.File.WriteAllText(File.FullName, content);
        _gutter.ResetBaseline();
        if (_dirty)
        {
            _dirty = false;
            UpdateTitle();
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
        Saved?.Invoke(this, EventArgs.Empty);
    }

    // The file's line-ending style, fixed on load. An empty buffer is a new/blank file: there's
    // nothing to preserve, so it takes the OS default (CRLF on Windows, LF elsewhere) — what VS
    // Code does for new files, and what Windows users expect. A non-empty file keeps its own
    // style instead: the first line break wins (CRLF vs LF), or LF if it has none (single line).
    private static string DetectEol(string text)
    {
        if (text.Length == 0) return Environment.NewLine;
        var i = text.IndexOf('\n');
        if (i < 0) return "\n";
        return i > 0 && text[i - 1] == '\r' ? "\r\n" : "\n";
    }

    // Collapse whatever endings TextView produced to LF, then re-emit with the target EOL.
    private static string Normalize(string text, string eol)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return eol == "\n" ? text : text.Replace("\n", eol);
    }

    // The header (find bar) belongs to its controller, which outlives this tab — don't take it down with us.
    protected override void Dispose(bool disposing)
    {
        if (disposing) SetHeader(null);
        base.Dispose(disposing);
    }

    private void OnEdited()
    {
        MarkDirty();
        _gutter.OnContentChanged();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MarkDirty()
    {
        if (_dirty) return;
        _dirty = true;
        UpdateTitle();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTitle()
    {
        Title = _dirty ? $"● {File.Name}" : File.Name;
        // TG redraws the tab header from Title only on layout, and positions headers from a cached width first.
        if (Border.View is BorderView { TitleView: ITitleView header }) header.MeasuredTabLength = 0;
        SetNeedsLayout();
    }
}

/// <summary>
/// TextView that paints syntax colours (#21) and find-in-file match highlights (#33), raises
/// <see cref="TextView.ContentsChanged"/> only for edits that change the text, and keeps the content-width cache across edits.
/// </summary>
internal sealed partial class EditorTextView : TextView
{
    private static readonly Command[] KillCommands =
        [Command.CutToEndOfLine, Command.CutToStartOfLine, Command.KillWordLeft, Command.KillWordRight];

    // Run with ContentsChanged held and raised once if the text changed, since TG 2.1.0 raises it unreliably for these.
    private static readonly HashSet<Command> EditCommands =
    [
        .. KillCommands, Command.NewLine, Command.DeleteCharLeft, Command.DeleteCharRight,
        Command.NextTabStop, Command.PreviousTabStop, Command.Paste, Command.Cut, Command.DeleteAll,
    ];

    private bool _holdContentsChanged;

    private Attribute _editable;
    private Attribute _highlight;
    private Attribute _cellAttribute;
    private Color[] _tokenColors = [];
    private int _tokenColorsVersion = -1;
    private long _selectionStart;
    private long _selectionEnd;

    /// <summary>Row → half-open [start, end) cell-column ranges to highlight.</summary>
    public Dictionary<int, List<(int Start, int End)>> Highlights { get; } = new();

    public IReadOnlyList<string> LineStrings => GetAllLines().Select(Cell.ToString).ToArray();

    /// <summary>Shared by the gutter and syntax colouring, so unchanged lines keep one string instance.</summary>
    internal LineSnapshot Snapshot { get; } = new();

    public LineTokenCache? Syntax
    {
        get;
        set
        {
            field = value;
            SetNeedsDraw();
        }
    }

    /// <summary>Lexing time allowed per frame; the rest continues on later iterations.</summary>
    internal TimeSpan SyntaxBudget { get; set; } = TimeSpan.FromMilliseconds(15);

    public override void OnContentsChanged()
    {
        if (_holdContentsChanged) return;
        // Records edits that don't come through a key, such as from TG's context menu.
        if (!_recordedEdit) RecordPending(Carets);
        var model = ModelField.GetValue(this)!;
        var maxWidth = MaxWidthAfterEdit(model);
        base.OnContentsChanged();
        CachedMaxWidth(model) = maxWidth;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (base.OnKeyDown(key)) return true;
        var bound = KeyBindings.TryGet(key, out var binding);
        if (ColumnSelect && bound && ExtendColumnSelection(binding)) return true;
        // Anything else ends the box, so the next extend starts one from where the caret now is.
        _box = null;
        if (!bound) return false;
        if (HasSecondaryCarets) return InvokeAtCarets(binding);
        if (!binding.Commands.Any(EditCommands.Contains)) return false;

        EditAtPrimary(() => InvokeEditCommands(binding.Commands, binding));
        // Already invoked: returning false would let TG invoke the bound commands a second time.
        return true;
    }

    // TG 2.1.0's kill commands ignore a selection and then throw reading it, so delete the selection instead, as VS Code does.
    private void InvokeEditCommands(Command[] commands, Terminal.Gui.Input.KeyBinding binding)
    {
        if (IsSelecting && commands.Any(KillCommands.Contains))
        {
            if (SelectionStartRow != CurrentRow || SelectionStartColumn != CurrentColumn)
            {
                DeleteCharLeft();
                return;
            }
            IsSelecting = false;
        }
        InvokeCommands(commands, binding);
    }

    // TG 2.1.0's TextView.OnDrawingContent, but stopping at the viewport bottom: upstream walks every row to EOF.
    protected override bool OnDrawingContent(DrawContext? context)
    {
        _editable = GetAttributeForRole(VisualRole.Editable);
        _highlight = GetAttributeForRole(VisualRole.Highlight);
        (_selectionStart, _selectionEnd) = SelectionBounds();
        PrepareCaretSelections();
        PrepareSyntax();
        SetAttributeForRole(Enabled ? VisualRole.Editable : VisualRole.Disabled);

        var right = Viewport.Width;
        var bottom = Viewport.Height;
        var row = 0;
        for (var idxRow = Viewport.Y; idxRow < Lines && row < bottom; idxRow++, row++)
            DrawRow(GetLine(idxRow), idxRow, row, right);
        DrawCaretUnderlines();

        if (row < bottom)
        {
            SetAttributeForRole(ReadOnly ? VisualRole.ReadOnly : VisualRole.Editable);
            ClearRegion(0, row, right, bottom);
        }
        return false;
    }

    private void PrepareSyntax()
    {
        if (Syntax is null) return;
        if (_tokenColorsVersion != Syntax.Highlighter.ThemeVersion)
        {
            _tokenColors = Syntax.Highlighter.Colors.Select(hex => hex is null ? default : Color.Parse(hex)).ToArray();
            _tokenColorsVersion = Syntax.Highlighter.ThemeVersion;
        }

        // Refreshed every frame rather than on edit: TG doesn't report every edit (see ContentsChanged in AGENTS.md).
        Syntax.Update(Snapshot.Refresh(GetAllLines()));
        var lastVisible = Math.Min(Viewport.Y + Viewport.Height, Lines) - 1;
        if (!Syntax.TokenizeThrough(lastVisible, SyntaxBudget))
            App?.Invoke(SetNeedsDraw);
    }

    private void DrawRow(List<Cell> line, int idxRow, int row, int right)
    {
        var (colWidths, col, idxCol) = FirstVisibleGlyph(line);
        var wasPreviousWideGlyphNegativeCol = false;
        Move(0, row);

        var tokens = Syntax?.TokensFor(idxRow);
        var chars = tokens is null ? 0 : CharsBefore(line, idxCol);
        var token = 0;
        var attributeToken = -1;
        _cellAttribute = _editable;

        for (; idxCol < line.Count; idxCol++)
        {
            var text = line[idxCol].Grapheme;
            var cols = text.GetColumns(false);

            if (tokens is { Length: > 0 })
            {
                while (token + 2 < tokens.Length && tokens[token + 2] <= chars)
                    token += 2;
                if (token != attributeToken)
                {
                    _cellAttribute = TokenAttribute(tokens[token + 1]);
                    attributeToken = token;
                }
                chars += text.Length;
            }

            if (InSelection(idxCol, idxRow))
                OnDrawSelectionColor(line, idxCol, idxRow);
            else if (idxCol == CurrentColumn && idxRow == CurrentRow && !IsSelecting && !Used && HasFocus)
                OnDrawUsedColor(line, idxCol, idxRow);
            else if (ReadOnly)
                OnDrawReadOnlyColor(line, idxCol, idxRow);
            else
                OnDrawNormalColor(line, idxCol, idxRow);

            if (text == "\t")
            {
                cols = TabWidth > 0 ? TabWidth - colWidths % TabWidth : 0;
                if (col + cols > right) cols = right - col;
                for (var i = 0; i < cols; i++)
                    AddRune(col + i, row, (Rune)' ');
            }
            else
            {
                if (col < 0 && cols > 1)
                    wasPreviousWideGlyphNegativeCol = true;
                else
                    AddStr(col, row, text);
                cols = Math.Max(cols, 1);
            }

            if (col + cols > Viewport.Right) break;
            col += cols;
            colWidths += cols;

            if (idxCol + 1 < line.Count && col + line[idxCol + 1].Grapheme.GetColumns() > right) break;
        }

        if (wasPreviousWideGlyphNegativeCol) AddStr(0, row, " ");

        if (col < right)
        {
            SetAttributeForRole(ReadOnly ? VisualRole.ReadOnly : VisualRole.Editable);
            ClearRegion(col, row, right, row + 1);
        }
    }

    // Col is where that glyph starts relative to the viewport, so <= 0 when it straddles the left edge.
    private (int ColWidths, int Col, int Index) FirstVisibleGlyph(List<Cell> line)
    {
        var start = Viewport.X;
        if (start <= 0 || line.Count == 0) return (0, 0, 0);

        var sum = 0;
        var count = Math.Min(start, line.Count);
        for (var i = 0; i < count; i++)
        {
            var grapheme = line[i].Grapheme;
            var width = grapheme == "\t"
                ? TabWidth > 0 ? TabWidth - sum % TabWidth : 0
                : Math.Max(grapheme.GetColumns(), 1);
            if (sum + width > start) return (sum, sum - start, i);
            sum += width;
        }
        return (sum, sum - start, count);
    }

    private (long Start, long End) SelectionBounds()
    {
        var anchor = Encode(SelectionStartRow, SelectionStartColumn);
        var point = Encode(CurrentRow, CurrentColumn);
        return anchor > point ? (point, anchor) : (anchor, point);
    }

    private bool InSelection(int col, int row)
    {
        var q = Encode(row, col);
        if (IsSelecting && q >= _selectionStart && q < _selectionEnd) return true;
        foreach (var (start, end) in _caretSelections)
            if (q >= start && q < end) return true;
        return false;
    }

    private static long Encode(int row, int col) => ((long)(uint)row << 32) | (uint)col;

    private void ClearRegion(int left, int top, int right, int bottom)
    {
        for (var row = top; row < bottom; row++)
        {
            Move(left, row);
            for (var col = left; col < right; col++)
                AddRune(col, row, (Rune)' ');
        }
    }

    private static int CharsBefore(List<Cell> line, int idxCol)
    {
        var chars = 0;
        for (var i = 0; i < idxCol; i++)
            chars += line[i].Grapheme.Length;
        return chars;
    }

    private Attribute TokenAttribute(int metadata)
    {
        var attribute = _editable;
        var foreground = SyntaxHighlighter.ForegroundOf(metadata);
        if (foreground != SyntaxHighlighter.DefaultForeground && foreground < _tokenColors.Length)
            attribute = attribute with { Foreground = _tokenColors[foreground] };

        var style = SyntaxHighlighter.StyleOf(metadata);
        if (style == TokenStyle.None) return attribute;
        return attribute with
        {
            Style = attribute.Style
                    | (style.HasFlag(TokenStyle.Italic) ? TextStyle.Italic : TextStyle.None)
                    | (style.HasFlag(TokenStyle.Bold) ? TextStyle.Bold : TextStyle.None)
                    | (style.HasFlag(TokenStyle.Underline) ? TextStyle.Underline : TextStyle.None)
                    | (style.HasFlag(TokenStyle.Strikethrough) ? TextStyle.Strikethrough : TextStyle.None),
        };
    }

    // Skips base, which resolves the scheme attribute (allocating) and raises DrawNormalColor for every cell.
    protected override void OnDrawNormalColor(List<Cell> line, int idxCol, int idxRow)
    {
        SetAttribute(IsHighlighted(idxCol, idxRow) ? _highlight : line[idxCol].Attribute ?? _cellAttribute);
    }

    // We never enable WordWrap, so draw coordinates are model coordinates.
    private bool IsHighlighted(int idxCol, int idxRow)
    {
        if (!Highlights.TryGetValue(idxRow, out var ranges)) return false;
        foreach (var (start, end) in ranges)
            if (idxCol >= start && idxCol < end) return true;
        return false;
    }

    // TG 2.1.0's base discards the width cache on every edit; any change beyond the current row has already invalidated it.
    private int MaxWidthAfterEdit(object model)
    {
        var maxWidth = CachedMaxWidth(model);
        // On a one-line model this is a full-range query, which returns the cache instead of measuring.
        if (maxWidth < 0 || Lines == 1) return -1;

        var width = GetMaxVisibleLine(model, CurrentRow, CurrentRow + 1, TabWidth);
        var widestRows = WidestRows(model);
        if (width > maxWidth)
        {
            widestRows.Clear();
            widestRows[CurrentRow] = width;
            return width;
        }
        if (width == maxWidth) widestRows[CurrentRow] = width;
        else if (widestRows.Remove(CurrentRow) && widestRows.Count == 0) return -1;
        return maxWidth;
    }

    private const string TextModelType = "Terminal.Gui.Views.TextModel, Terminal.Gui";

    // UnsafeAccessorType can't return a ref to an inaccessible type.
    private static readonly FieldInfo ModelField = typeof(TextView).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_cachedMaxWidth")]
    private static extern ref int CachedMaxWidth([UnsafeAccessorType(TextModelType)] object model);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_cachedMaxWidthPerLine")]
    private static extern ref Dictionary<int, int> WidestRows([UnsafeAccessorType(TextModelType)] object model);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetMaxVisibleLine")]
    private static extern int GetMaxVisibleLine([UnsafeAccessorType(TextModelType)] object model, int first, int last, int tabWidth);
}
