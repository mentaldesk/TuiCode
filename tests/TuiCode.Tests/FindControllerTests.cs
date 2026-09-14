using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Workbench.Find;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class FindControllerTests : IDisposable
{
    private readonly MockFileSystem _fs = new();
    private readonly EditorGroup _group = new();
    private readonly InputScopeStack _scopes = new();
    private readonly KeybindingService _workbenchScope = new(new CommandService());
    private readonly FindController _find;

    public FindControllerTests()
    {
        _scopes.Push(_workbenchScope);
        _find = new FindController(_group, _scopes, _workbenchScope);
        // Mirror WorkbenchHost, which forwards the group's tab changes.
        _group.ActiveTabChanged += (_, tab) => _find.OnActiveTabChanged(tab);
    }

    [Fact]
    public void Typing_selects_the_first_match_at_or_after_the_cursor()
    {
        var tab = Open("/work/a.txt", "foo\nbar foo\nfoo\n");
        tab.MoveCursor(1, 0);

        _find.Open(replace: false);
        _find.Bar.Query = "foo";

        Assert.Equal(3, _find.Matches.Count);
        Assert.Equal(1, _find.CurrentIndex);
        Assert.Equal((1, 7), (tab.CursorRow, tab.CursorColumn));
        Assert.Equal("2 of 3", _find.Bar.Status);
    }

    [Fact]
    public void Refining_the_query_searches_from_the_original_cursor_not_the_last_match()
    {
        var tab = Open("/work/a.txt", "fo\nfoo\n");

        _find.Open(replace: false);
        _find.Bar.Query = "f";   // selects row 0
        _find.Bar.Query = "fo";  // still row 0 — typing doesn't ratchet forward

        Assert.Equal(0, _find.CurrentIndex);
    }

    [Fact]
    public void Next_and_Previous_step_through_matches_and_wrap()
    {
        Open("/work/a.txt", "x\nx\nx\n");
        _find.Open(replace: false);
        _find.Bar.Query = "x";

        _find.Next();
        _find.Next();
        Assert.Equal(2, _find.CurrentIndex);
        _find.Next();
        Assert.Equal(0, _find.CurrentIndex);
        _find.Previous();
        Assert.Equal(2, _find.CurrentIndex);
    }

    [Fact]
    public void No_matches_reports_no_results()
    {
        Open("/work/a.txt", "abc");
        _find.Open(replace: false);

        _find.Bar.Query = "zzz";

        Assert.Equal(-1, _find.CurrentIndex);
        Assert.Equal("No results", _find.Bar.Status);
    }

    [Fact]
    public void Hint_explains_the_navigation_keys_only_while_there_are_matches()
    {
        Open("/work/a.txt", "foo\nfoo\n");
        var raised = new List<string?>();
        _find.HintChanged += (_, hint) => raised.Add(hint);
        _find.Open(replace: false);
        Assert.Null(_find.Hint);

        _find.Bar.Query = "foo";
        Assert.Equal("Enter next match · Shift+Enter previous match · Esc close", _find.Hint);

        _find.Bar.Query = "zzz";
        Assert.Null(_find.Hint);

        _find.Bar.Query = "foo";
        _find.Close();
        Assert.Null(_find.Hint);
        Assert.Equal([
            "Enter next match · Shift+Enter previous match · Esc close", null,
            "Enter next match · Shift+Enter previous match · Esc close", null,
        ], raised);
    }

    [Fact]
    public void Hint_mentions_replace_all_and_the_replace_field_when_the_replace_row_is_showing()
    {
        Open("/work/a.txt", "foo");
        _find.Open(replace: true);

        _find.Bar.Query = "foo";

        Assert.Equal("Enter next · Shift+Enter previous · Ctrl+Enter replace all · Tab replace field", _find.Hint);
    }

    [Fact]
    public void Opening_seeds_the_query_from_a_single_line_selection()
    {
        var tab = Open("/work/a.txt", "one two two\n");
        tab.Select(new TextMatch(0, 8, 3));

        _find.Open(replace: false);

        Assert.Equal("two", _find.Bar.Query);
        // The selected occurrence stays current rather than jumping to the next one.
        Assert.Equal(1, _find.CurrentIndex);
    }

    [Fact]
    public void ReplaceOne_replaces_the_current_match_and_moves_to_the_next()
    {
        var tab = Open("/work/a.txt", "a a a\n");
        _find.Open(replace: true);
        _find.Bar.Query = "a";
        _find.Bar.Replacement = "aa";

        _find.ReplaceOne();

        Assert.Equal("aa a a", tab.Lines[0]);
        // Next match is the original second "a", not the one inside the replacement.
        Assert.Equal(new TextMatch(0, 3, 1), _find.Matches[_find.CurrentIndex]);
    }

    [Fact]
    public void ReplaceAll_replaces_every_match_in_the_buffer()
    {
        var tab = Open("/work/a.txt", "cat\nconcat cat\n");
        _find.Open(replace: true);
        _find.Bar.Query = "cat";
        _find.Bar.Replacement = "dog";

        _find.ReplaceAll();

        Assert.Equal(["dog", "condog dog", ""], tab.Lines);
        Assert.Empty(_find.Matches);
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public void Replace_is_a_noop_while_the_replace_row_is_hidden()
    {
        var tab = Open("/work/a.txt", "cat");
        _find.Open(replace: false);
        _find.Bar.Query = "cat";

        _find.ReplaceAll();

        Assert.Equal("cat", tab.Lines[0]);
    }

    [Fact]
    public void Editing_the_buffer_while_open_refreshes_the_matches()
    {
        var tab = Open("/work/a.txt", "x\n");
        _find.Open(replace: false);
        _find.Bar.Query = "x";

        tab.Content = "x x x\n";

        Assert.Equal(3, _find.Matches.Count);
    }

    [Fact]
    public void Open_pushes_a_scope_and_Close_pops_it_and_detaches_the_bar()
    {
        var tab = Open("/work/a.txt", "x");
        var closed = false;
        _find.Closed += (_, _) => closed = true;

        _find.Open(replace: false);
        Assert.True(_find.IsOpen);
        Assert.Same(tab, _find.Bar.SuperView);

        _find.Close();

        Assert.False(_find.IsOpen);
        Assert.Null(_find.Bar.SuperView);
        Assert.True(closed);
        // Only the workbench scope is left: popping it must succeed.
        _scopes.Pop(_workbenchScope);
    }

    [Fact]
    public void Switching_tabs_moves_the_bar_and_re_runs_the_query()
    {
        Open("/work/a.txt", "needle");
        _find.Open(replace: false);
        _find.Bar.Query = "needle";

        var second = Open("/work/b.txt", "needle needle");

        Assert.Same(second, _find.Bar.SuperView);
        Assert.Equal(2, _find.Matches.Count);
    }

    [Fact]
    public void Closing_the_last_tab_closes_find_without_disposing_the_bar()
    {
        Open("/work/a.txt", "x");
        _find.Open(replace: false);

        _group.CloseActive();

        Assert.False(_find.IsOpen);
        // The bar survived the tab's disposal and can be shown again.
        Open("/work/b.txt", "x");
        _find.Open(replace: false);
        Assert.True(_find.IsOpen);
    }

    [Fact]
    public void Open_without_an_active_editor_does_nothing()
    {
        _find.Open(replace: false);

        Assert.False(_find.IsOpen);
    }

    private EditorTab Open(string path, string content)
    {
        _fs.AddFile(path, new MockFileData(content));
        return _group.OpenOrFocus(_fs.FileInfo.New(path));
    }

    public void Dispose()
    {
        _find.Dispose();
        _group.Dispose();
    }
}
