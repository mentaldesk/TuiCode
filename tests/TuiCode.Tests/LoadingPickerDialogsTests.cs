using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Fields;
using MentalDesk.Tui.Keys;
using MentalDesk.Tui.Loading;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Syntax;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Git;
using TuiCode.Workbench.Navigation;

namespace TuiCode.Tests;

// Open worktree, Open pull request, Compare to revision and Go to symbol on the library's PickerDialog (#573). Builds TG views — serialised (#77).
public class LoadingPickerDialogsTests : StaticConfigurationTest
{
    private const string Home = "/Users/me";

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private static readonly IReadOnlyList<GitHubPullRequestSummary> PullRequests =
    [
        new(132, "Command scopes should be fixed", "jamescrosswell", ReviewRequested: true),
        new(129, "Delete, rename and move files", "octocat"),
    ];

    public static TheoryData<string, string> Pickers => new()
    {
        { "worktree", "Enter Open" },
        { "pr", "Enter Check out" },
        { "revision", "Enter Compare" },
        { "symbol", "Enter Go to" },
    };

    [Theory]
    [MemberData(nameof(Pickers))]
    public void A_picker_says_Type_to_filter_and_shows_its_keys_on_two_buttons_with_no_hint_row(string kind, string pick)
    {
        using var picker = Picker(kind);

        Assert.Equal("Type to filter", Assert.IsType<PlaceholderField>(picker.FilterField).Placeholder);
        Assert.Equal([pick, "Esc Cancel"], picker.Dialog.SubViews.OfType<Button>().Select(b => b.Text.Trim()));
        Assert.DoesNotContain(picker.Dialog.SubViews, v => v is Label && v.Text.Contains("Esc"));
    }

    [Theory]
    [MemberData(nameof(Pickers))]
    public void With_no_match_the_list_says_so_and_the_pick_button_dims(string kind, string _)
    {
        using var picker = Picker(kind);

        picker.FilterField.Text = "zzzzqqq";

        Assert.True(picker.NoMatches());
        Assert.False(picker.PickButton.Enabled);
    }

    public static TheoryData<string, string, string[]> Sheets => new()
    {
        { "worktree", "Open worktree", ["Open", "Previous", "Next", "Page up", "Page down", "Cancel"] },
        { "pr", "Open pull request", ["Check out", "Previous", "Next", "Page up", "Page down", "Cancel"] },
        { "revision", "Compare a.cs to revision", ["Compare", "Previous", "Next", "Page up", "Page down", "Cancel"] },
        { "symbol", "Go to symbol in a.cs", ["Go to", "Previous", "Next", "Page up", "Page down", "Cancel"] },
    };

    [Theory]
    [MemberData(nameof(Sheets))]
    public void F1_asks_for_a_sheet_titled_with_the_picker_listing_its_keys(string kind, string title, string[] labels)
    {
        using var picker = Picker(kind);
        KeySheet? sheet = null;
        picker.Dialog.KeysRequested += (_, s) => sheet = s;

        picker.Dialog.Scope.Handle(Key.F1);

        Assert.NotNull(sheet);
        Assert.Equal(title, sheet.Here!.Heading);
        Assert.Equal(labels.Order(), sheet.Here.Groups.SelectMany(g => g.Rows).Select(r => r.Label).Order());
    }

    [Fact]
    public void A_failed_load_lists_Retry_in_F1_and_R_retries_it()
    {
        using var picker = new PullRequestPickerView();
        var retried = 0;
        picker.ShowLoadFailed("gh: not logged in", () => retried++);
        KeySheet? sheet = null;
        picker.KeysRequested += (_, s) => sheet = s;

        picker.Scope.Handle(Key.F1);
        picker.Scope.Handle(Key.R);

        Assert.Contains("Retry", sheet!.Here!.Groups.SelectMany(g => g.Rows).Select(r => r.Label));
        Assert.Equal(1, retried);
    }

    [Fact]
    public void Pull_requests_load_inside_the_list_with_the_pick_button_dimmed_until_they_arrive()
    {
        using var picker = new PullRequestPickerView();
        var whileLoading = (picker.State, picker.PickButton.Enabled);

        picker.Load(PullRequests);

        Assert.Equal((LoadState.Loading, false), whileLoading);
        Assert.Equal(LoadState.Loaded, picker.State);
        Assert.True(picker.PickButton.Enabled);
        Assert.Equal(["#132", "#129"], picker.VisibleItems);
    }

    [Fact]
    public void A_failed_load_says_why_in_the_list_and_a_retry_loads_again()
    {
        using var picker = new RevisionPickerView("a.cs");
        picker.ShowLoadFailed("fatal: bad object", () => picker.ShowLoading());
        var failed = (picker.State, picker.Says(), picker.PickButton.Enabled);

        picker.Scope.Handle(Key.R);
        var retrying = picker.State;
        picker.Load([new GitRef("main", GitRefKind.Branch)], []);

        Assert.Equal(LoadState.Failed, failed.State);
        Assert.Contains("fatal: bad object", failed.Item2);
        Assert.False(failed.Enabled);
        Assert.Equal(LoadState.Loading, retrying);
        Assert.Equal(["HEAD", "main"], picker.VisibleItems);
    }

    [Fact]
    public void Outside_a_git_repository_the_list_says_so_with_no_retry()
    {
        using var picker = new PullRequestPickerView();

        picker.ShowLoadFailed("/work isn't in a git repository", null);

        Assert.Equal(LoadState.Empty, picker.State);
        Assert.Equal("/work isn't in a git repository", picker.Says());
    }

    [Fact]
    public void Enter_compares_to_the_typed_revision_when_nothing_matches()
    {
        using var picker = new RevisionPickerView("a.cs");
        picker.Load([new GitRef("main", GitRefKind.Branch)], []);
        RevisionEntry? comparing = null;
        picker.Comparing += (_, e) => comparing = e;

        picker.FilterField.Text = "HEAD~3";
        picker.Scope.Handle(Key.Enter);

        Assert.True(picker.NoMatches);
        Assert.Equal("HEAD~3", comparing?.Revision);
        Assert.False(picker.Confirmed);
    }

    [Fact]
    public void A_pull_request_that_cant_be_checked_out_says_why_and_the_picker_stays_open()
    {
        using var picker = new PullRequestPickerView();
        picker.Load(PullRequests);
        var closed = false;
        picker.Closed += (_, _) => closed = true;
        picker.Picking += (_, e) => e.Cancel = true;

        picker.Scope.Handle(Key.Enter);
        picker.ShowError("pr-132 already has local changes");

        Assert.False(closed);
        Assert.Equal("pr-132 already has local changes", picker.Alert.Message);
    }

    [Fact]
    public void Symbols_fill_in_as_the_scan_advances_and_a_file_with_none_says_so()
    {
        using var picker = new SymbolPickerView("a.cs", Scan(["class Widget", "{", "    void Go() { }", "}"]));
        using var empty = new SymbolPickerView("a.cs", Scan(["// nothing here"]));

        while (!picker.Scanned) picker.Advance();
        while (!empty.Scanned) empty.Advance();

        Assert.Equal(["Widget", "Go"], picker.VisibleItems);
        Assert.Equal(LoadState.Empty, empty.State);
        Assert.Equal(SymbolPickerView.NoSymbols, empty.Says());
    }

    [Theory]
    [InlineData("main", "wt")]
    [InlineData("feature/a-branch-name-far-too-long-for-its-column", "wt-folder-name-far-too-long-for-its-column")]
    public void The_worktree_header_lines_up_with_the_rows(string branch, string name)
    {
        using var picker = new WorktreePickerView([new WorktreeRow($"/code/{name}", branch, name, "~/code")]);

        var row = picker.Labels.Single();

        Assert.Equal(row.IndexOf("wt", StringComparison.Ordinal), picker.Header.IndexOf("Worktree", StringComparison.Ordinal));
        Assert.Equal(row.IndexOf("~/code", StringComparison.Ordinal), picker.Header.IndexOf("Location", StringComparison.Ordinal));
    }

    private static SymbolScan Scan(string[] lines) => Syntax.CreateSymbolScan(Syntax.LanguageById("csharp"), lines)!;

    private static AnyPicker Picker(string kind)
    {
        switch (kind)
        {
            case "worktree":
                return AnyPicker.Of(new WorktreePickerView(
                [
                    WorktreeList.Row(new GitWorktree("/Users/me/code/feature", "feat/thing"), Home),
                    WorktreeList.Row(new GitWorktree("/Users/me/code/pr-7", null, "3f9c2e1abc"), Home),
                ]));
            case "pr":
                var pr = new PullRequestPickerView();
                pr.Load(PullRequests);
                return AnyPicker.Of(pr);
            case "revision":
                var revision = new RevisionPickerView("a.cs");
                revision.Load([new GitRef("main", GitRefKind.Branch)], []);
                return AnyPicker.Of(revision);
            default:
                var symbol = new SymbolPickerView("a.cs", Scan(["class Widget", "{", "    void Go() { }", "}"]));
                while (!symbol.Scanned) symbol.Advance();
                return AnyPicker.Of(symbol);
        }
    }

    private sealed record AnyPicker(AppDialog Dialog, TextField FilterField, Button PickButton, Func<bool> NoMatches) : IDisposable
    {
        public static AnyPicker Of<T>(PickerDialog<T> picker) where T : class =>
            new(picker, picker.FilterField, picker.PickButton, () => picker.NoMatches);

        public void Dispose() => Dialog.Dispose();
    }
}
