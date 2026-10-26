using MentalDesk.Tui.Dialogs;
using TuiCode.Abstractions;
using TuiCode.Icons;
using TuiCode.Syntax;
using TuiCode.Workbench.Controls;

namespace TuiCode.Workbench.Navigation;

/// <summary>
/// Modal picker for Go to symbol (<c>gs</c>): a filter over the definitions in the active file. It opens
/// before the scan has finished and fills in as <see cref="Advance"/> takes slices of it, so a big file
/// costs a few frames rather than a freeze. The host jumps once it closes.
/// </summary>
public sealed class SymbolPickerView : PickerDialog<FileSymbol>
{
    /// <summary>What an empty scan says, and what <c>gs</c> puts in the status bar for a file with no grammar.</summary>
    public const string NoSymbols = "No symbols in this file";

    private const int DialogWidth = 66;

    private static readonly TimeSpan ScanBudget = TimeSpan.FromMilliseconds(15);

    private readonly SymbolScan _scan;

    public SymbolPickerView(string fileName, SymbolScan scan, FileIcons? icons = null)
        : this(fileName, scan, icons?.Style ?? FileIconStyle.Off, new RowLayout(scan))
    {
    }

    private SymbolPickerView(string fileName, SymbolScan scan, FileIconStyle icons, RowLayout layout)
        : base($"Go to symbol in {fileName}", DialogWidth, 18, "Scanning symbols…", s => s.Name, "Go to",
            s => SymbolList.Row(s, layout.Columns, PickerList.Width(DialogWidth), layout.Indented, icons))
    {
        _scan = scan;
        layout.Filter = FilterField;
        Commands.Register(CancelId, "Cancel", ClearOrCancel);
        ShowScanned();
    }

    internal IReadOnlyList<string> VisibleItems => [.. Shown.Select(s => s.Name)];

    /// <summary>Whether the whole file has been scanned; until then rows are still arriving.</summary>
    public bool Scanned => _scan.Done;

    /// <summary>Scans the next slice and shows what it found. The host drives this from <c>Iteration</c>.</summary>
    public void Advance()
    {
        if (_scan.Done) return;
        var before = _scan.Symbols.Count;
        _scan.Advance(ScanBudget);
        if (_scan.Symbols.Count != before || _scan.Done) ShowScanned();
    }

    private void ShowScanned()
    {
        if (_scan.Symbols.Count > 0 || _scan.Done) ShowRows([.. _scan.Symbols], NoSymbols);
    }

    private void ClearOrCancel()
    {
        if (FilterField.Text.Length > 0) FilterField.Text = "";
        else Cancel();
    }

    /// <summary>A filter leaves rows whose containing type may be gone, so it flattens them.</summary>
    private sealed class RowLayout(SymbolScan scan)
    {
        private SymbolList.Columns? _columns;
        private int _counted = -1;

        public TextField? Filter { get; set; }

        public bool Indented => Filter is null || Filter.Text.Trim().Length == 0;

        public SymbolList.Columns Columns
        {
            get
            {
                if (_counted != scan.Symbols.Count)
                {
                    _columns = SymbolList.Columns.Of(scan.Symbols);
                    _counted = scan.Symbols.Count;
                }
                return _columns!;
            }
        }
    }
}
