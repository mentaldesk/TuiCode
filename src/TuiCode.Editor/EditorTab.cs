using TuiCode.Abstractions;
using TuiCode.Syntax;

namespace TuiCode.Editor;

public sealed class EditorTab : FrameView
{
    private readonly CodeEditor _text;
    private readonly EditorGutter _gutter;
    private readonly SyntaxHighlighter? _syntax;
    private bool _grammarChosen;
    private View? _header;
    private string _eol;
    private bool _dirty;
    // What Save last wrote or the tab last read; null while nothing is on disk.
    private string? _saved;
    private DiskState _disk;
    private bool _headerColoured;
    private bool _loading;
    private int _edits;
    private (int Edits, (System.Drawing.Point Start, System.Drawing.Point End)[] Ranges, DocumentStats Stats)? _selectionStats;
    private (int Edits, SyntaxLanguage? Grammar, SymbolScan? Scan)? _symbols;
    private (bool Vertical, bool Horizontal) _barsLaidOut;
    private (int Edits, SyntaxLanguage? Grammar, SymbolScan? Scan)? _stickyScan;
    private SymbolScan? _stickyFrom;
    // Scrolling waits for the first draw: TG doesn't size the content to the text until then.
    private ViewPosition? _pendingView;

    public IFileInfo File { get; private set; }
    public bool IsDirty => _dirty;

    /// <summary>The file as this tab last saw it: on load, and again after every save (#267).</summary>
    public FileSnapshot OnDisk { get; private set; }

    /// <summary>Where the file stands now: changed under us, so saving would overwrite theirs, or gone (#271).</summary>
    public DiskState DiskNow => OnDisk.StateOf(File);

    /// <summary>What the header is telling the user (#268); the next save clears it.</summary>
    public DiskState DiskMarker => _disk;

    /// <summary>Show or drop the marker. Nothing else about the tab changes: you can keep typing through it.</summary>
    public void MarkOnDisk(DiskState state)
    {
        if (state == _disk) return;
        _disk = state;
        UpdateTitle();
    }

    /// <summary>Which glyph the disk-change marker uses, Auto already resolved; <see cref="EditorGroup"/> keeps it in step.</summary>
    public FileIconStyle IconStyle
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            if (_disk != DiskState.Unchanged) UpdateTitle();
        }
    } = FileIconStyle.Off;

    public string Content
    {
        get => _text.Text;
        set
        {
            _text.Text = value;
            OnEdited();
        }
    }

    public int CursorRow => _text.CurrentRow;
    public int CursorColumn => _text.CurrentColumn;

    public event EventHandler? DirtyChanged;

    /// <summary>Raised on every edit, whether typed, pasted, replaced or set via <see cref="Content"/>.</summary>
    public event EventHandler? ContentChanged;
    public event EventHandler? Saved;

    /// <summary>Raised when the buffer is taken as the file again, by a save or a reload, so its <c>HEAD</c> baseline can be re-read.</summary>
    public event EventHandler? BaselineReset;
    public event EventHandler<CopyOutcome>? Copied;

    /// <summary>
    /// Raised whenever the insertion point moves, carrying the position in the file's own
    /// (unwrapped) model coordinates — the raw source for cursor-location history (#35).
    /// </summary>
    public event EventHandler<(int Row, int Column)>? CursorMoved;

    public event EventHandler? GrammarChanged;

    /// <summary>Raised with the line of a pinned definition the user clicked (#474).</summary>
    public event EventHandler<int>? PinnedLineClicked;

    public EditorTab(IFileInfo file, SyntaxHighlighter? syntax = null)
        : this(file, syntax, null)
    {
    }

    /// <summary>
    /// A tab holding <paramref name="content"/> at <paramref name="file"/>'s path, dirty from the start and
    /// nothing on disk yet — the base version of a file this branch deleted (#247). Ctrl+S writes it back.
    /// </summary>
    internal static EditorTab Unsaved(IFileInfo file, string content, SyntaxHighlighter? syntax = null) =>
        new(file, syntax, content);

    private EditorTab(IFileInfo file, SyntaxHighlighter? syntax, string? unsaved)
    {
        File = file;
        _syntax = syntax;
        BorderStyle = LineStyle.None;

        var initial = unsaved ?? file.FileSystem.File.ReadAllText(file.FullName);
        OnDisk = FileSnapshot.Of(file, initial);
        _saved = unsaved is null ? initial : file.Exists ? file.FileSystem.File.ReadAllText(file.FullName) : null;
        _eol = DetectEol(initial);

        _text = new CodeEditor
        {
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Text = initial,
            Syntax = syntax?.CreateCache(syntax.LanguageForFile(file.Name)),
        };
        _text.ViewportSettings |= ViewportSettingsFlags.HasScrollBars;
        Settings = EditorSettings.Default;
        _gutter = new EditorGutter(_text, syntax) { X = 0, Y = 0, Height = Dim.Fill(Dim.Func(_ => _text.Padding!.Thickness.Bottom)) };
        _text.HorizontalScrollBar.VisibleChanged += (_, _) => SetNeedsLayout();
        ThinScrollBar.Apply(_text.HorizontalScrollBar);
        ScrollBarResize.Hold(_text.VerticalScrollBar, () => _text.Viewport.Y);
        ScrollBarResize.Hold(_text.HorizontalScrollBar, () => _text.Viewport.X);
        _text.X = Pos.Right(_gutter);
        // Subscribe AFTER setting initial text so the load doesn't mark dirty.
        _text.Edited += (_, _) => OnEdited();
        _text.Copied += (_, outcome) => Copied?.Invoke(this, outcome);
        _text.RefreshingSticky = RefreshSticky;
        _text.Drawing = ScrollToPendingView;
        _text.PinnedLineClicked += (_, line) => PinnedLineClicked?.Invoke(this, line);
        // Point is (X=column, Y=row). Re-expose in (row, column) order to match the rest of the editor API.
        _text.CursorMoved += (_, point) => CursorMoved?.Invoke(this, (point.Y, point.X));
        Add(_gutter, _text);

        _dirty = unsaved is not null && !MatchesSaved();
        UpdateTitle();
    }

    // A bar that shows or hides mid-layout changes the padding, and TG drops the relayout that asks for (#315).
    protected override void OnSubViewsLaidOut(LayoutEventArgs args)
    {
        base.OnSubViewsLaidOut(args);
        var bars = (_text.VerticalScrollBar.Visible, _text.HorizontalScrollBar.Visible);
        if (bars == _barsLaidOut) return;
        _barsLaidOut = bars;
        SetNeedsLayout();
        Layout();
    }

    public EditorSettings Settings
    {
        get;
        set
        {
            field = value;
            _text.TabWidth = value.IndentSize;
            _text.InsertSpaces = value.InsertSpaces;
            _text.SetNeedsDraw();
        }
    }

    /// <summary>Time per frame for the scan that finds what to pin, on top of syntax colouring's.</summary>
    internal TimeSpan StickyBudget { get; set; } = TimeSpan.FromMilliseconds(10);

    internal SymbolScan? StickyScan => _stickyScan?.Scan;

    /// <summary>The lines pinned over the top of the text at its last draw.</summary>
    public IReadOnlyList<int> PinnedLines => _text.Pinned;

    // Invoke would run inline, inside this draw, which clears the request.
    private void DrawAgainSoon() => _text.App?.AddTimeout(TimeSpan.Zero, () =>
    {
        _text.SetNeedsDraw();
        return false;
    });

    // Not ScanSymbols(): the gs picker draws from that scan as it fills (see "Sticky lines" in AGENTS.md).
    private void RefreshSticky()
    {
        if (!Settings.StickyLines || Grammar is null || _text.Viewport.Height < StickyLines.MinHeight)
        {
            _text.Sticky = StickyLines.None;
            _stickyScan = null;
            _stickyFrom = null;
            return;
        }
        var stale = _stickyScan is not var (edits, grammar, _) || edits != _edits || !Equals(grammar, Grammar);
        if (stale && _stickyScan?.Scan is not { Done: false })
            _stickyScan = (_edits, Grammar, _syntax?.CreateSymbolScan(Grammar, Lines));
        if (_stickyScan?.Scan is not { } scan)
        {
            _text.Sticky = StickyLines.None;
            return;
        }
        if (!scan.Advance(StickyBudget))
        {
            DrawAgainSoon();
            return;
        }
        if (ReferenceEquals(scan, _stickyFrom)) return;
        _stickyFrom = scan;
        _text.Sticky = StickyLines.From(scan.Symbols, scan.Lines);
        if (stale) DrawAgainSoon();
    }

    public bool GutterVisible
    {
        get => _gutter.Visible;
        set
        {
            _gutter.Visible = value;
            _text.X = value ? Pos.Right(_gutter) : 0;
            SetNeedsLayout();
        }
    }

    internal IReadOnlyList<LineChange> LineChanges => _gutter.Changes;

    /// <summary>The file's lines at <c>HEAD</c>, which the gutter marks changes against; null falls back to the last save.</summary>
    public IReadOnlyList<string>? CommittedLines
    {
        get => _gutter.Committed;
        set => _gutter.Committed = value;
    }

    /// <summary>Whether syntax colouring is available at all; without it every tab is plain text.</summary>
    public bool HasSyntax => _syntax is not null;

    /// <summary>The grammar colouring this tab, or null for plain text.</summary>
    public SyntaxLanguage? Grammar => _text.Syntax?.Language;

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
        _text.Syntax = _syntax.CreateCache(grammar);
        GrammarChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Take up what's on disk (#269), returning false and touching nothing when that's already what the
    /// tab holds. The cursor keeps its line and column, clamped to the new file; the gutter's baseline and
    /// the undo history describe the new content. The tab comes out clean — the buffer *is* the file now,
    /// so a reload asked for over unsaved edits (#270) drops them along with the marker.
    /// <paramref name="evenIfUnchanged"/> takes the file up when it still matches the snapshot too (#272).
    /// </summary>
    public bool Reload(bool evenIfUnchanged = false)
    {
        var content = evenIfUnchanged ? ReadNow() : OnDisk.ReadIfChanged(File);
        if (content is null) return false;

        var (row, column) = (CursorRow, CursorColumn);
        _eol = DetectEol(content);
        // Text = clears the undo history: Ctrl+Z mustn't walk back through edits made against text that's gone.
        // It also raises Edited, which would dirty the tab; taking up the file isn't an edit.
        _loading = true;
        try
        {
            _text.Text = content;
        }
        finally
        {
            _loading = false;
        }
        OnDisk = FileSnapshot.Of(File, content);
        _saved = content;
        MoveCursor(row, column);
        _edits++;
        _gutter.ResetBaseline();
        BaselineReset?.Invoke(this, EventArgs.Empty);
        _disk = DiskState.Unchanged;
        if (_dirty)
        {
            _dirty = false;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
        UpdateTitle();
        ContentChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private string? ReadNow()
    {
        File.Refresh();
        return File.Exists ? File.FileSystem.File.ReadAllText(File.FullName) : null;
    }

    /// <summary>Follow a rename or move; the buffer is untouched.</summary>
    internal void Relocate(IFileInfo file)
    {
        File = file;
        OnDisk = FileSnapshot.Of(file, OnDisk.Content);
        UpdateTitle();
        InferGrammar();
    }

    public bool FocusContent() => _text.SetFocus();
    public bool ContentHasFocus => _text.HasFocus;

    /// <summary>
    /// Move the cursor to the given (zero-based) row/column, leaving one caret and nothing selected.
    /// Out-of-range values clamp to the nearest valid position.
    /// </summary>
    public void MoveCursor(int row, int col)
    {
        _pendingView = null;
        row = Math.Clamp(row, 0, Math.Max(_text.Lines - 1, 0));
        col = Math.Clamp(col, 0, _text.LineLength(row));
        _text.MoveCaret(new System.Drawing.Point(col, row));
        if (WordWrap) _text.SetNeedsDraw();
    }

    /// <summary>Moves to the partner of the bracket at the cursor, or to the bracket closing the block it's in; false when there's none (#539).</summary>
    public bool GoToMatchingBracket()
    {
        if (_text.MatchingBracket() is not { } target) return false;
        MoveCursor(target.Y, target.X);
        RevealLines(target.Y, target.Y);
        return true;
    }

    /// <summary>The cursor and the scroll, to come back to when the file is next opened (#502).</summary>
    public ViewPosition ViewPosition => _pendingView ?? new(CursorRow, CursorColumn, _text.TopLine, WordWrap ? 0 : _text.Viewport.X);

    /// <summary>Puts the cursor and the scroll back where <paramref name="position"/> had them, clamped to the file as it is now.</summary>
    public void RestoreViewPosition(ViewPosition position)
    {
        MoveCursor(position.Line, position.Column);
        _pendingView = position with { Line = CursorRow, Column = CursorColumn };
        _text.SetNeedsDraw();
    }

    private void ScrollToPendingView()
    {
        if (_pendingView is not { } view || _text.Viewport.Height <= 0) return;
        _pendingView = null;
        _text.ScrollToLine(view.TopLine, view.LeftColumn);
        if (CursorRow < _text.TopLine || CursorRow >= _text.TopLine + _text.Viewport.Height) RevealLines(CursorRow, CursorRow);
    }

    /// <summary>The cursor's column in UTF-16 chars, as language servers count them.</summary>
    public int CursorCharacter => ToCharColumn(CursorRow, CursorColumn);

    /// <summary><see cref="MoveCursor"/> with the column in UTF-16 chars.</summary>
    public void MoveCursorToCharacter(int row, int character)
    {
        row = Math.Clamp(row, 0, Math.Max(_text.Lines - 1, 0));
        MoveCursor(row, ToCellColumn(row, character));
    }

    internal CodeEditor Code => _text;

    internal CodeEditor.UndoHistory History => _text.History;

    /// <summary>A screen row while wrapped.</summary>
    public int TopRow => _text.Viewport.Y;

    public int VisibleRows => _text.Viewport.Height;

    /// <summary>
    /// Scroll so lines <paramref name="first"/> to <paramref name="last"/> are in view with a margin
    /// above and below, or not at all when they already are (#200). Every explicit jump ends here.
    /// </summary>
    public void RevealLines(int first, int last)
    {
        _pendingView = null;
        if (WordWrap)
        {
            _text.RevealWrappedLines(first, last);
            return;
        }
        var viewport = _text.Viewport;
        if (Reveal.TopRow(viewport.Y, viewport.Height, _text.Lines, first, last, _text.PinnedCountAt) is not { } top) return;
        _text.Viewport = viewport with { Y = top };
    }

    /// <summary>
    /// The buffer's lines as the editor models them (terminators excluded). Columns in a
    /// <see cref="TextMatch"/> computed against these line up with <see cref="Select"/> / <see cref="Replace"/>.
    /// </summary>
    public IReadOnlyList<string> Lines => [.. _text.LineStrings];

    /// <summary>Like <see cref="Lines"/> but cheap to re-read; later reads update the returned list in place.</summary>
    internal IReadOnlyList<string> SnapshotLines => _text.LineStrings;

    public string SelectedText => _text.SelectedText;

    /// <summary>The cursor's line, with the cursor's column in UTF-16 chars.</summary>
    public (string Text, int Column) CursorLine =>
        (_text.LineText(CursorRow), ToCharColumn(CursorRow, CursorColumn));

    public CopyOutcome CopyText(string text) => _text.CopyText(text);

    public int CaretCount => _text.CaretCount;

    public DocumentStats CountDocument() => DocumentStats.Of(_text.LineStrings);

    /// <summary>The file's definitions, scanned in slices; the same scan comes back until the buffer or the grammar changes.</summary>
    public SymbolScan? ScanSymbols()
    {
        if (_symbols is (var edits, var grammar, var scan) && edits == _edits && Equals(grammar, Grammar)) return scan;
        var fresh = _syntax?.CreateSymbolScan(Grammar, Lines);
        _symbols = (_edits, Grammar, fresh);
        return fresh;
    }

    /// <summary>The counts across every caret's selection, or null when nothing is selected. Recounted only after the carets or the buffer change.</summary>
    public DocumentStats? CountSelection()
    {
        if (!_text.IsSelecting && !_text.HasSecondaryCarets) return null;
        var ranges = _text.Carets.Where(c => c.Start != c.End).Select(c => (c.Start, c.End)).ToArray();
        if (ranges.Length == 0) return null;
        if (_selectionStats is (var edits, var counted, var cached) && edits == _edits && counted.AsSpan().SequenceEqual(ranges)) return cached;

        var stats = DocumentStats.Of(_text.LineStrings, ranges);
        _selectionStats = (_edits, ranges, stats);
        SelectionCounts++;
        return stats;
    }

    internal int SelectionCounts { get; private set; }

    /// <summary>What <see cref="Save"/> will write: the Line endings setting, or on Auto the file's own.</summary>
    public LineEnding LineEnding => Settings.LineEnding switch
    {
        LineEnding.Auto => _eol == "\r\n" ? LineEnding.CRLF : LineEnding.LF,
        var chosen => chosen,
    };

    public void MoveLines(LineDirection direction) => _text.MoveLines(direction);

    public void DuplicateLines(LineDirection direction) => _text.DuplicateLines(direction);

    public void IndentLines() => _text.IndentLines();

    public void OutdentLines() => _text.OutdentLines();

    /// <summary>Toggles the grammar's line comment, or its block markers around each line (#389); false when it has neither.</summary>
    public bool ToggleLineComment()
    {
        if (Grammar?.LineComment is { } marker)
            _text.ToggleLineComment(marker);
        else if (Grammar?.BlockComment is var (open, close))
            _text.ToggleLineComment(open, close);
        else
            return false;
        return true;
    }

    /// <summary>Replaces <paramref name="count"/> lines from <paramref name="start"/> with <paramref name="lines"/>, as one undo step (#245).</summary>
    public void ReplaceLines(int start, int count, IReadOnlyList<string> lines) => _text.ReplaceLines(start, count, lines);

    public bool HasSecondaryCursors => _text.HasSecondaryCarets;

    public bool WordWrap
    {
        get => _text.SoftWrap;
        set => _text.SoftWrap = value;
    }

    /// <summary>Whether extending the selection sweeps a rectangle rather than a run of text (#114).</summary>
    public bool ColumnSelect
    {
        get => _text.ColumnSelect;
        set => _text.ColumnSelect = value;
    }

    /// <summary>Add a cursor on the line above or below every cursor.</summary>
    public void AddCursor(LineDirection direction) => _text.AddCaret(direction);

    public void RemoveSecondaryCursors() => _text.RemoveSecondaryCarets();

    public bool HasSelection => _text.HasSelection;

    /// <summary>Back to one cursor, where the primary one is, with nothing selected.</summary>
    public void ClearSelectionAndCursors()
    {
        _text.RemoveSecondaryCarets();
        ClearSelection();
    }

    public void SelectNextOccurrence() => _text.SelectNextOccurrence();

    public void SelectPreviousOccurrence() => _text.SelectPreviousOccurrence();

    public void SelectAllOccurrences() => _text.SelectAllOccurrences();

    /// <summary>The rows the selection covers, leaving out a last row it ends at the start of; null with nothing selected.</summary>
    public (int First, int Last)? SelectedRows
    {
        get
        {
            if (!_text.HasSelection) return null;
            var (anchor, caret) = (_text.SelectionAnchor, new System.Drawing.Point(_text.CurrentColumn, _text.CurrentRow));
            var (start, end) = anchor.Y < caret.Y || (anchor.Y == caret.Y && anchor.X <= caret.X) ? (anchor, caret) : (caret, anchor);
            var last = end.X == 0 && end.Y > start.Y ? end.Y - 1 : end.Y;
            return (start.Y, last);
        }
    }

    /// <summary>
    /// Start of the selection, or the cursor when nothing is selected — with the column in UTF-16 chars,
    /// comparable with <see cref="TextMatch"/> positions.
    /// </summary>
    public (int Row, int Column) SelectionOrigin
    {
        get
        {
            var (row, col) = (_text.CurrentRow, _text.CurrentColumn);
            if (_text.HasSelection)
            {
                var (startCol, startRow) = (_text.SelectionAnchor.X, _text.SelectionAnchor.Y);
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
        _text.Y = _gutter.Y = header is null ? 0 : Pos.Bottom(header);
        SetNeedsLayout();
    }

    /// <summary>Paint every match with the highlight colour (find-in-file). An empty set clears it.</summary>
    public void SetHighlights(IEnumerable<TextMatch> matches)
    {
        _text.Highlights.Clear();
        foreach (var m in matches)
        {
            if (!_text.Highlights.TryGetValue(m.Row, out var ranges))
                _text.Highlights[m.Row] = ranges = [];
            ranges.Add((ToCellColumn(m.Row, m.Column), ToCellColumn(m.Row, m.End)));
        }
        _text.SetNeedsDraw();
    }

    /// <summary>Select <paramref name="match"/>, leaving the cursor at its end and scrolling it into view.</summary>
    public void Select(TextMatch match)
    {
        var start = ToCellColumn(match.Row, match.Column);
        var end = ToCellColumn(match.Row, match.End);
        _pendingView = null;
        _text.RemoveSecondaryCarets();
        _text.Select(new System.Drawing.Point(start, match.Row), new System.Drawing.Point(end, match.Row));
    }

    public void ClearSelection() => _text.ClearSelection();

    /// <summary>Replace the text covered by <paramref name="match"/>, as one undo step.</summary>
    public void Replace(TextMatch match, string replacement)
    {
        Select(match);
        _text.EditAtPrimary(() =>
        {
            if (match.Length > 0) _text.ReplaceSelection(replacement);
            else _text.Document!.Insert(_text.CaretOffset, replacement);
        });
        _text.ClearSelection();
    }

    private int ToCharColumn(int row, int column) => CodeEditor.CharsBefore(_text.LineText(row), column);

    // TextMatch columns count UTF-16 chars; the editor's columns count graphemes.
    private int ToCellColumn(int row, int charColumn) => CodeEditor.ColumnOf(_text.LineText(row), charColumn);

    public void Save()
    {
        var content = SaveContent();
        // A deleted file's directory can have gone with it (#271), and a tab opened on a file that never
        // existed (#247) has nothing above it either; recreate the path rather than throwing.
        if (File.FileSystem.Path.GetDirectoryName(File.FullName) is { Length: > 0 } directory)
            File.FileSystem.Directory.CreateDirectory(directory);
        File.FileSystem.File.WriteAllText(File.FullName, content);
        OnDisk = FileSnapshot.Of(File, content);
        _saved = content;
        _gutter.ResetBaseline();
        BaselineReset?.Invoke(this, EventArgs.Empty);
        _disk = DiskState.Unchanged;
        if (_dirty)
        {
            _dirty = false;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
        UpdateTitle();
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private string Eol => LineEnding == LineEnding.CRLF ? "\r\n" : "\n";

    // The editor's Text joins its lines with Environment.NewLine, so on Windows the buffer comes back CRLF
    // regardless of the file's real endings; re-emit with the EOL detected on load, as VS Code does.
    private string SaveContent()
    {
        var eol = Eol;
        var content = Normalize(_text.Text, eol);
        if (Settings.InsertFinalNewline && content.Length > 0 && !content.EndsWith(eol, StringComparison.Ordinal))
            content += eol;
        return content;
    }

    // What SaveContent would write, compared in place: typing in a large file mustn't walk or build its text each key (#550).
    private bool MatchesSaved()
    {
        if (_saved is not { } saved) return false;
        var lines = _text.LineStrings;
        var eol = Eol;
        var finalEol = Settings.InsertFinalNewline && lines.Count > 0 && lines[^1].Length > 0;
        var length = _text.LineChars + (Math.Max(lines.Count - 1, 0) + (finalEol ? 1 : 0)) * eol.Length;
        if (length != saved.Length) return false;

        var at = 0;
        bool Next(string text)
        {
            if (!saved.AsSpan(at, text.Length).SequenceEqual(text)) return false;
            at += text.Length;
            return true;
        }
        for (var i = 0; i < lines.Count; i++)
            if ((i > 0 && !Next(eol)) || !Next(lines[i])) return false;
        return !finalEol || Next(eol);
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

    // Collapse whatever endings the editor produced to LF, then re-emit with the target EOL.
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
        if (_loading) return;
        _edits++;
        SetDirty(!MatchesSaved());
        _gutter.OnContentChanged();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetDirty(bool dirty)
    {
        if (_dirty == dirty) return;
        _dirty = dirty;
        UpdateTitle();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTitle()
    {
        // The trailing space keeps the marker off the tab's right border.
        var mark = DiskMark.For(_disk, IconStyle);
        Title = $"{(_dirty ? "● " : "")}{File.Name}{(mark.Length > 0 ? $" {mark} " : "")}";
        PaneTabs.ShowTitle(this);
        if (Border.View is BorderView { TitleView: { } view })
            ColourWhileMarked(view);
    }

    // TG has no VisualRole for a warning and GetAttributeForRole isn't virtual in 2.1.0, so the marked
    // tab's name is recoloured as its attribute is resolved; Handled makes the result stick.
    private void ColourWhileMarked(View header)
    {
        if (_headerColoured) return;
        _headerColoured = true;
        header.GettingAttributeForRole += (_, e) =>
        {
            if (_disk == DiskState.Unchanged) return;
            e.Result = WarningColour.On(e.Result ?? GetAttributeForRole(e.Role));
            e.Handled = true;
        };
    }
}
