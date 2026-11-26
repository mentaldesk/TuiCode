using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Usages;

namespace TuiCode.Tests;

// Builds TG views. Serialised (#77).
public class UsagesViewTests : StaticConfigurationTest
{
    private static readonly string Targets = Path.GetFullPath("/work/Sentry.Native.targets");
    private static readonly string Project = Path.GetFullPath("/work/Sentry.csproj");

    private static readonly Dictionary<string, string[]> Text = new()
    {
        [Targets] = [.. Enumerable.Range(0, 20).Select(i => i switch
        {
            9 => "Inputs=\"@(SentryNativeBuildInputs)\"",
            15 => "<Target Name=\"_SetupSentryNative\" DependsOnTargets=\"_BuildSentryNativeSDK\" />",
            17 => "<Target Name=\"_Again\" DependsOnTargets=\"_SetupSentryNative\" />",
            _ => "",
        })],
        [Project] = ["", "<Target Name=\"Restore\" DependsOnTargets=\"_BuildSentryNativeSDK\" />"],
    };

    private static readonly UsageDefinition Build = new("_BuildSentryNativeSDK", "_BuildSentryNativeSDK", new(Targets, 8, 16));
    private static readonly UsageDefinition Setup = new("_SetupSentryNative", "_SetupSentryNative", new(Targets, 15, 16));
    private static readonly UsageDefinition Restore = new("Restore", "Restore", new(Project, 1, 14));

    [Fact]
    public void The_header_says_when_the_usages_are_text_matches()
    {
        using var view = new UsagesView();

        view.ShowUsages(view.StartLooking("Hunks"), "Diff.Hunks", Files((Targets, 9, Build)), textMatches: false);
        var exact = view.HeaderText;
        view.ShowUsages(view.StartLooking("SentryNativeBuildInputs"), "SentryNativeBuildInputs", Files((Targets, 9, Build)), textMatches: true);

        Assert.Equal("Usages of Diff.Hunks  1 in 1 file", exact);
        Assert.Equal("Usages of SentryNativeBuildInputs · text matches  1 in 1 file", view.HeaderText);
    }

    [Fact]
    public void Following_a_row_looks_once_and_says_so_under_it_until_the_answer_comes()
    {
        using var view = new UsagesView { StopKey = "Esc" };
        var asked = new List<UsageLookup>();
        view.Following += (_, lookup) => asked.Add(lookup);
        view.ShowUsages(view.StartLooking("SentryNativeBuildInputs"), "SentryNativeBuildInputs", Files((Targets, 9, Build)), textMatches: true);
        var row = Row(view.Files[0]);

        view.Follow(row);
        view.Follow(row);

        var lookup = Assert.Single(asked);
        Assert.Same(row, lookup.From);
        Assert.Equal("_BuildSentryNativeSDK", lookup.Name);
        Assert.Matches("^. Finding usages of _BuildSentryNativeSDK…  Esc stop$", Assert.Single(row.Children).ToString());
    }

    [Fact]
    public void The_answer_lists_the_definitions_usages_under_the_row_grouped_by_file_and_marks_a_loop()
    {
        using var view = new UsagesView();
        UsageLookup? following = null;
        view.Following += (_, lookup) => following = lookup;
        view.ShowUsages(view.StartLooking("SentryNativeBuildInputs"), "SentryNativeBuildInputs", Files((Targets, 9, Build)), textMatches: true);
        var build = Row(view.Files[0]);
        view.Follow(build);

        Assert.True(view.ShowUsages(following!, "_BuildSentryNativeSDK", Files((Targets, 15, Setup), (Project, 1, Restore)), textMatches: true));
        var setup = Row(build.Children.OfType<UsageFileNode>().Single(f => f.Path == Targets));
        view.Follow(setup);
        view.ShowUsages(following!, "_SetupSentryNative", Files((Targets, 17, Build)), textMatches: true);

        Assert.Equal(["Sentry.csproj", "Sentry.Native.targets"], build.Children.Cast<UsageFileNode>().Select(f => f.Name));
        Assert.Equal("16  in _SetupSentryNative  <Target Name=\"_SetupSentryNative\" DependsOnTargets=\"_BuildSentryNativeSDK\" />", setup.ToString());
        var again = Row(setup.Children.Cast<UsageFileNode>().Single());
        Assert.True(again.Loops);
        Assert.False(again.CanFollow);
        Assert.StartsWith("18  ↺ in _BuildSentryNativeSDK", again.ToString(), StringComparison.Ordinal);
        Assert.Same(setup, again.Above);
    }

    [Fact]
    public void A_definition_with_no_usages_says_so_under_its_row()
    {
        using var view = new UsagesView();
        UsageLookup? following = null;
        view.Following += (_, lookup) => following = lookup;
        view.ShowUsages(view.StartLooking("SentryNativeBuildInputs"), "SentryNativeBuildInputs", Files((Targets, 9, Build)), textMatches: true);
        var row = Row(view.Files[0]);
        view.Follow(row);

        view.ShowUsages(following!, "_BuildSentryNativeSDK", [], textMatches: true);

        Assert.Equal("No usages of _BuildSentryNativeSDK", Assert.Single(row.Children).ToString());
    }

    [Fact]
    public void Stopping_says_where_each_lookup_would_have_landed_and_drops_its_late_answer()
    {
        using var view = new UsagesView();
        UsageLookup? following = null;
        view.Following += (_, lookup) => following = lookup;
        view.ShowUsages(view.StartLooking("SentryNativeBuildInputs"), "SentryNativeBuildInputs", Files((Targets, 9, Build)), textMatches: true);
        var row = Row(view.Files[0]);
        view.Follow(row);

        Assert.True(view.IsLooking);
        view.StopLooking();

        Assert.False(view.IsLooking);
        Assert.True(following!.Token.IsCancellationRequested);
        Assert.Equal("Stopped finding usages of _BuildSentryNativeSDK", Assert.Single(row.Children).ToString());
        Assert.False(view.ShowUsages(following, "_BuildSentryNativeSDK", Files((Project, 1, Restore)), textMatches: true));
        Assert.Equal("Stopped finding usages of _BuildSentryNativeSDK", Assert.Single(row.Children).ToString());
    }

    [Fact]
    public void A_stopped_row_looks_again_when_followed()
    {
        using var view = new UsagesView();
        var asked = 0;
        view.Following += (_, _) => asked++;
        view.ShowUsages(view.StartLooking("SentryNativeBuildInputs"), "SentryNativeBuildInputs", Files((Targets, 9, Build)), textMatches: true);
        var row = Row(view.Files[0]);
        view.Follow(row);
        view.StopLooking();

        view.Follow(row);

        Assert.Equal(2, asked);
        Assert.True(view.IsLooking);
    }

    [Fact]
    public void Stopping_gu_itself_says_so_in_the_header()
    {
        using var view = new UsagesView();
        var lookup = view.StartLooking("Hunks");

        view.StopLooking();

        Assert.Equal("Stopped finding usages of Hunks", view.HeaderText);
        Assert.False(view.ShowUsages(lookup, "Diff.Hunks", Files((Targets, 9, Build)), textMatches: false));
        Assert.Empty(view.Files);
    }

    [Fact]
    public void An_answer_for_a_row_after_the_tab_was_refilled_is_dropped()
    {
        using var view = new UsagesView();
        UsageLookup? following = null;
        view.Following += (_, lookup) => following = lookup;
        view.ShowUsages(view.StartLooking("SentryNativeBuildInputs"), "SentryNativeBuildInputs", Files((Targets, 9, Build)), textMatches: true);
        view.Follow(Row(view.Files[0]));
        view.ShowUsages(view.StartLooking("Hunks"), "Diff.Hunks", Files((Project, 1, Restore)), textMatches: false);

        Assert.False(view.ShowUsages(following!, "_BuildSentryNativeSDK", Files((Project, 1, Restore)), textMatches: true));
        Assert.Equal("Usages of Diff.Hunks  1 in 1 file", view.HeaderText);
        Assert.Equal(["Sentry.csproj"], view.Files.Select(f => f.Name));
    }

    private static UsageLineNode Row(UsageFileNode file) => (UsageLineNode)file.Children.Single();

    private static IReadOnlyList<UsageFileNode> Files(params (string Path, int Line, UsageDefinition? In)[] usages) =>
        UsageTree.Build(usages.Select(u => new SourceLocation(u.Path, u.Line, 0)), path => Text[path], Path.GetFullPath("/work"),
            usage => usages.Single(u => u.Path == usage.Path && u.Line == usage.Line).In);
}
