using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Search;
using TuiCode.Workbench.References;
using TuiCode.Workbench.Review;

namespace TuiCode.Workbench.Parts;

public enum SidebarTab { Explorer, Find, References, Review }

public sealed class SidebarPart : FrameView
{
    private readonly PaneTabs _tabs;
    private readonly View _explorerTab;
    private readonly View _findTab;
    private readonly View _referencesTab;
    private readonly View _reviewTab;
    private readonly View _noFolder;

    public FileExplorerView Explorer { get; }
    public SearchView Search { get; }
    public ReferencesView References { get; }
    public ReviewView Review { get; }

    /// <summary>The <c>[ Open Folder ]</c> button shown in place of the tree while no folder is open (#451).</summary>
    public Button OpenFolderButton { get; }

    public bool IsShowingNoFolder => _noFolder.Visible;

    public event EventHandler? OpenFolderRequested;

    public SidebarTab ActiveTab =>
        ReferenceEquals(_tabs.Value, _findTab) ? SidebarTab.Find
        : ReferenceEquals(_tabs.Value, _referencesTab) ? SidebarTab.References
        : ReferenceEquals(_tabs.Value, _reviewTab) ? SidebarTab.Review
        : SidebarTab.Explorer;

    public SidebarPart(FileExplorerView explorer, SearchView? search = null, ReviewView? review = null, ReferencesView? references = null)
    {
        Explorer = explorer;
        Search = search ?? new SearchView();
        References = references ?? new ReferencesView();
        Review = review ?? new ReviewView();
        BorderStyle = LineStyle.Single;
        SchemeName = "Sidebar";

        _explorerTab = WrapTab("Explorer", explorer);
        OpenFolderButton = new Button { Text = "Open Folder", X = 1, Y = 3 };
        OpenFolderButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            OpenFolderRequested?.Invoke(this, EventArgs.Empty);
        };
        _noFolder = new View
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            Visible = false,
        };
        _noFolder.Add(new Label { Text = "No folder open", X = 1, Y = 1 }, OpenFolderButton);
        _explorerTab.Add(_noFolder);
        // Titled after the Find globally / Replace globally commands that open it (fg / rg).
        _findTab = WrapTab("Find", Search);
        _referencesTab = WrapTab("References", References);
        _reviewTab = WrapTab("Review", Review);

        _tabs = new PaneTabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
        };
        _tabs.Add(_explorerTab, _findTab, _referencesTab, _reviewTab);
        _tabs.Value = _explorerTab;
        _tabs.ValueChanged += (_, _) =>
        {
            if (ActiveTab == SidebarTab.Review) Review.Refresh();
        };
        Add(_tabs);
    }

    /// <summary>Puts "No folder open" and its button in place of the explorer's tree, or the tree back.</summary>
    public void ShowNoFolder(bool show)
    {
        var buttonHadFocus = OpenFolderButton.HasFocus;
        _noFolder.Visible = show;
        Explorer.Visible = !show;
        if (!show && buttonHadFocus) Explorer.SetFocus();
    }

    public void ShowTab(SidebarTab tab) =>
        _tabs.Value = tab switch
        {
            SidebarTab.Find => _findTab,
            SidebarTab.References => _referencesTab,
            SidebarTab.Review => _reviewTab,
            _ => _explorerTab,
        };

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
