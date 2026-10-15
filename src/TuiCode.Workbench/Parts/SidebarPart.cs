using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Loading;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using Terminal.Gui.Text;
using TuiCode.Icons;
using TuiCode.Workbench.Assistant;
using TuiCode.Search;
using TuiCode.Workbench.Usages;
using TuiCode.Workbench.Review;

namespace TuiCode.Workbench.Parts;

public enum SidebarTab { Explorer, Find, Usages, Review, Assistant }

public sealed class SidebarPart : FrameView
{
    private readonly PaneTabs _tabs;
    private readonly View _explorerTab;
    private readonly View _findTab;
    private readonly View _usagesTab;
    private readonly View _reviewTab;
    private readonly View _assistantTab;
    private readonly FileIcons? _icons;
    private readonly View _noFolder;

    public FileExplorerView Explorer { get; }
    public SearchView Search { get; }
    public UsagesView Usages { get; }
    public ReviewView Review { get; }
    public AssistantView Assistant { get; }

    /// <summary>The <c>[ Open Folder ]</c> button shown in place of the tree while no folder is open (#451).</summary>
    public Button OpenFolderButton { get; }

    public bool IsShowingNoFolder => _noFolder.Visible;

    public event EventHandler? OpenFolderRequested;

    public SidebarTab ActiveTab =>
        ReferenceEquals(_tabs.Value, _findTab) ? SidebarTab.Find
        : ReferenceEquals(_tabs.Value, _usagesTab) ? SidebarTab.Usages
        : ReferenceEquals(_tabs.Value, _reviewTab) ? SidebarTab.Review
        : ReferenceEquals(_tabs.Value, _assistantTab) ? SidebarTab.Assistant
        : SidebarTab.Explorer;

    public SidebarPart(
        FileExplorerView explorer,
        SearchView? search = null,
        ReviewView? review = null,
        UsagesView? usages = null,
        AssistantView? assistant = null,
        FileIcons? icons = null)
    {
        Explorer = explorer;
        Search = search ?? new SearchView();
        Usages = usages ?? new UsagesView();
        Review = review ?? new ReviewView();
        Assistant = assistant ?? new AssistantView(new Conversation());
        _icons = icons;
        BorderStyle = LineStyle.Single;
        SchemeName = "Sidebar";

        _explorerTab = WrapTab("Explorer", explorer);
        OpenFolderButton = AppButton.Primary("Open Folder");
        OpenFolderButton.X = 1;
        OpenFolderButton.Y = 3;
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
        _noFolder.Add(new VerbatimLabel { Text = "No folder open", X = 1, Y = 1 }, OpenFolderButton);
        _explorerTab.Add(_noFolder);
        // Titled after the Find globally / Replace globally commands that open it (fg / rg).
        _findTab = WrapTab("Find", Search);
        _usagesTab = WrapTab("Usages", Usages);
        Review.Width = Dim.Fill();
        Review.Height = Dim.Fill();
        Review.LoadState = new LoadStateView(Review, "Loading changes…");
        _reviewTab = WrapTab("Review", Review.LoadState);
        // LoadStateView puts its reload spinner on the border's first row, which on a tab is above the line under the headers.
        _reviewTab.Border.GetOrCreateView().SubViewAdded += (_, e) =>
        {
            if (e.SubView is SpinnerView spinner) spinner.Y = Math.Max(0, _reviewTab.Border.Thickness.Top - 1);
        };

        _tabs = new PaneTabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
        };
        _assistantTab = WrapTab(AssistantTitle, Assistant);
        _tabs.Add(_explorerTab, _findTab, _usagesTab, _reviewTab, _assistantTab);
        _tabs.Value = _explorerTab;
        _tabs.ValueChanged += (_, _) =>
        {
            ShowTitles();
            if (ActiveTab == SidebarTab.Review) Review.Refresh();
        };
        if (_icons is not null) _icons.Changed += (_, _) => ShowTitles();
        ViewportChanged += (_, _) => ShowTitles();
        Add(_tabs);
        ShowTitles();
    }

    public const string AssistantTitle = "AI assistance";

    // nf-fa-folder_tree, nf-md-file_find, nf-fa-connectdevelop, nf-cod-code_review and nf-cod-chat_sparkle.
    private static readonly string[] NerdIcons = ["\uef81", "\U000F021E", "\uf20e", "\uec37", "\uec4f"];
    private static readonly string[] EmojiIcons = ["📁", "🔍", "🔗", "👀", "✨"];

    private View[] TabViews => [_explorerTab, _findTab, _usagesTab, _reviewTab, _assistantTab];

    private static readonly string[] Names = ["Explorer", "Find", "Usages", "Review", AssistantTitle];

    private void ShowTitles()
    {
        var titles = HeaderTitles(_icons?.Style ?? FileIconStyle.Off, Array.IndexOf(TabViews, _tabs.Value), Viewport.Width);
        var tabs = TabViews;
        for (var i = 0; i < tabs.Length; i++)
        {
            if (tabs[i].Title == titles[i]) continue;
            tabs[i].Title = titles[i];
            PaneTabs.ShowTitle(tabs[i]);
        }
    }

    /// <summary>
    /// The headers for <paramref name="style"/> with tab <paramref name="selected"/> active, fitting <paramref name="width"/>:
    /// the selected tab's name is cut, then dropped, and emoji too wide for the strip give way to each name's initial.
    /// </summary>
    internal static string[] HeaderTitles(FileIconStyle style, int selected, int width)
    {
        if (style is not (FileIconStyle.NerdFont or FileIconStyle.Emoji)) return [.. Names];
        var icons = style == FileIconStyle.NerdFont ? NerdIcons : EmojiIcons;
        if (width > 0 && Columns(icons) > width) icons = [.. Names.Select(name => name[..1])];
        var titles = icons.ToArray();
        if (selected < 0 || selected >= titles.Length) return titles;
        var room = width - Columns(titles);
        var name = Names[selected];
        if (room >= name.GetColumns() + 1) titles[selected] = $"{icons[selected]} {name}";
        else if (room >= 3) titles[selected] = $"{icons[selected]} {name[..(room - 2)]}…";
        return titles;
    }

    // Each header takes its text and the border after it; the strip starts with one more.
    private static int Columns(IEnumerable<string> titles) => titles.Sum(t => t.GetColumns() + 1) + 1;

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
            SidebarTab.Usages => _usagesTab,
            SidebarTab.Review => _reviewTab,
            SidebarTab.Assistant => _assistantTab,
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
