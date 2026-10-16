using TuiCode.Abstractions;
using TuiCode.Workbench.Assistant;
using TuiCode.Workbench.History;

namespace TuiCode.Workbench.Parts;

public enum PanelTab { History, Assistant }

/// <summary>The panel under the editor: a tab for each of History and AI assistance that's open, in that order.</summary>
public sealed class PanelPart : FrameView
{
    private readonly PaneTabs _tabs;

    public HistoryView History { get; } = new();

    public AssistantView Assistant { get; }

    public PanelPart(AssistantView? assistant = null)
    {
        Assistant = assistant ?? new AssistantView(new Conversation());
        Assistant.Title = AssistantView.TabTitle;
        Assistant.Width = Dim.Fill();
        Assistant.Height = Dim.Fill();
        History.Width = Dim.Fill();
        History.Height = Dim.Fill();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        _tabs = new PaneTabs { Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
        Add(_tabs);
    }

    private View[] Views => [History, Assistant];

    public bool IsOpen(PanelTab tab) => _tabs.TabCollection.Contains(ViewOf(tab));

    public bool HasTabs => _tabs.TabCollection.Any();

    public PanelTab? ActiveTab =>
        ReferenceEquals(_tabs.Value, History) ? PanelTab.History
        : ReferenceEquals(_tabs.Value, Assistant) ? PanelTab.Assistant
        : null;

    /// <summary>Adds or takes away <paramref name="tab"/>, keeping the others in order and the active one active.</summary>
    public void SetOpen(PanelTab tab, bool open)
    {
        if (IsOpen(tab) == open) return;
        var active = _tabs.Value;
        var wanted = Views.Where(view => ReferenceEquals(view, ViewOf(tab)) ? open : _tabs.TabCollection.Contains(view)).ToArray();
        foreach (var view in _tabs.TabCollection.ToArray()) _tabs.Remove(view);
        foreach (var view in wanted) _tabs.Add(view);
        if (wanted.Length == 0) _tabs.Value = null;
        else _tabs.Value = wanted.Contains(active) ? active : wanted[0];
    }

    public void ShowTab(PanelTab tab)
    {
        if (IsOpen(tab)) _tabs.Value = ViewOf(tab);
    }

    /// <summary>The tab <paramref name="step"/> along from the active one, wrapping, made active.</summary>
    public PanelTab? Cycle(int step)
    {
        var open = _tabs.TabCollection.ToArray();
        if (open.Length == 0) return null;
        _tabs.Value = open[(Array.IndexOf(open, _tabs.Value) + step + open.Length) % open.Length];
        return ActiveTab;
    }

    private const int TabStripRows = 3;

    /// <summary>
    /// The History panel's share of the <paramref name="available"/> rows, or half of them for a chat, plus the tab strip;
    /// the editor keeps <see cref="FileHistory.EditorFloor"/> unless the terminal is too short for both.
    /// </summary>
    public static int RowsFor(int available, bool chat)
    {
        var share = chat ? available / 2 : available / 3 + TabStripRows;
        return Math.Max(FileHistory.MinPanelHeight + TabStripRows, Math.Min(share, available - FileHistory.EditorFloor));
    }

    private View ViewOf(PanelTab tab) => tab == PanelTab.History ? History : Assistant;
}
