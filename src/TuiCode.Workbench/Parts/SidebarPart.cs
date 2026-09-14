using TuiCode.Explorer;
using TuiCode.Search;

namespace TuiCode.Workbench.Parts;

public enum SidebarTab { Explorer, Search }

public sealed class SidebarPart : FrameView
{
    private readonly Tabs _tabs;
    private readonly View _explorerTab;
    private readonly View _searchTab;

    public FileExplorerView Explorer { get; }
    public SearchView Search { get; }

    public SidebarTab ActiveTab => ReferenceEquals(_tabs.Value, _searchTab) ? SidebarTab.Search : SidebarTab.Explorer;

    public SidebarPart(FileExplorerView explorer, SearchView? search = null)
    {
        Explorer = explorer;
        Search = search ?? new SearchView();
        BorderStyle = LineStyle.Single;

        _explorerTab = WrapTab("Explorer", explorer);
        _searchTab = WrapTab("Search", Search);

        _tabs = new Tabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
        };
        _tabs.Add(_explorerTab, _searchTab);
        _tabs.Value = _explorerTab;
        Add(_tabs);
    }

    public void ShowTab(SidebarTab tab) =>
        _tabs.Value = tab == SidebarTab.Search ? _searchTab : _explorerTab;

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
