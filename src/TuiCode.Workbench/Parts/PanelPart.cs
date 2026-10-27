using TuiCode.Abstractions;
using TuiCode.Workbench.History;

namespace TuiCode.Workbench.Parts;

/// <summary>The panel under the editor, holding the History tab while it's open.</summary>
public sealed class PanelPart : FrameView
{
    private readonly PaneTabs _tabs;

    public HistoryView History { get; } = new();

    public PanelPart()
    {
        History.Width = Dim.Fill();
        History.Height = Dim.Fill();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        _tabs = new PaneTabs { Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
        Add(_tabs);
    }

    public bool IsOpen => _tabs.TabCollection.Any();

    public void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        if (open)
        {
            _tabs.Add(History);
            _tabs.Value = History;
        }
        else
        {
            _tabs.Remove(History);
            _tabs.Value = null;
        }
    }

    private const int TabStripRows = 3;

    /// <summary>
    /// The History panel's share of the <paramref name="available"/> rows, plus the tab strip;
    /// the editor keeps <see cref="FileHistory.EditorFloor"/> unless the terminal is too short for both.
    /// </summary>
    public static int RowsFor(int available) =>
        Math.Max(FileHistory.MinPanelHeight + TabStripRows, Math.Min(available / 3 + TabStripRows, available - FileHistory.EditorFloor));
}
