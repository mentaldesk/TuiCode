using TuiCode.Explorer;
using TuiCode.Search;

namespace TuiCode.Workbench.Parts;

public enum SidebarTab { Explorer, Find }

public sealed class SidebarPart : FrameView
{
    private readonly Tabs _tabs;
    private readonly View _explorerTab;
    private readonly View _findTab;

    public FileExplorerView Explorer { get; }
    public SearchView Search { get; }

    public SidebarTab ActiveTab => ReferenceEquals(_tabs.Value, _findTab) ? SidebarTab.Find : SidebarTab.Explorer;

    public SidebarPart(FileExplorerView explorer, SearchView? search = null)
    {
        Explorer = explorer;
        Search = search ?? new SearchView();
        BorderStyle = LineStyle.Single;

        _explorerTab = WrapTab("Explorer", explorer);
        // Titled after the Find globally / Replace globally commands that open it (fg / rg).
        _findTab = WrapTab("Find", Search);

        _tabs = new Tabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
        };
        _tabs.Add(_explorerTab, _findTab);
        _tabs.Value = _explorerTab;
        Add(_tabs);
    }

    public void ShowTab(SidebarTab tab) =>
        _tabs.Value = tab == SidebarTab.Find ? _findTab : _explorerTab;

    private static View WrapTab(string title, View content)
    {
        content.X = 0;
        content.Y = 0;
        content.Width = Dim.Fill();
        content.Height = Dim.Fill();

        var tab = new View
        {
            Title = title,
            CanFocus = true,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        tab.Add(content);
        return tab;
    }
}
