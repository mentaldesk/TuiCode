using TuiCode.Abstractions;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Navigation;

namespace TuiCode.Tests;

public class CompletionsTests
{
    private static readonly HashSet<string> Dot = ["."];

    [Theory]
    [InlineData("    sb.", 7, ".")]
    [InlineData("    C", 5, "")]
    [InlineData("    _", 5, "")]
    [InlineData("    Co", 6, "")]
    [InlineData("    x1", 6, "")]
    [InlineData("    sb.A", 8, "")]
    [InlineData("    1", 5, null)]
    [InlineData("    x ", 6, null)]
    [InlineData("", 0, null)]
    public void Trigger_opens_after_a_servers_character_or_a_letter_of_a_name(string line, int character, string? expected) =>
        Assert.Equal(expected, Completions.Trigger(line, character, Dot));

    [Fact]
    public void Trigger_prefers_the_longest_character_the_server_names()
    {
        HashSet<string> rust = [":", "::"];

        Assert.Equal("::", Completions.Trigger("std::", 5, rust));
    }

    [Theory]
    [InlineData("    sb.Ap", 9, 7)]
    [InlineData("    Con", 7, 4)]
    [InlineData("x = ", 4, 4)]
    [InlineData("a_b1", 4, 0)]
    public void WordStart_is_where_the_name_before_the_caret_starts(string line, int character, int expected) =>
        Assert.Equal(expected, Completions.WordStart(line, character));

    [Fact]
    public void Narrow_keeps_what_matches_best_first_and_marks_the_matching_letters()
    {
        CompletionItem[] items =
        [
            new("ToString"), new("append", SortText: "1"), new("AppendLine", SortText: "3"), new("Append", SortText: "2"),
            new("TryAppendFormat", SortText: "0"), new("Capacity"),
        ];

        var matches = Completions.Narrow(items, "Ap");

        Assert.Equal(["Append", "AppendLine", "append", "TryAppendFormat"], matches.Select(m => m.Item.Label));
        Assert.Equal([0, 1], matches[0].Marks);
        Assert.Equal([3, 4], matches[3].Marks);
    }

    [Fact]
    public void Narrow_matches_on_the_servers_filter_text_and_orders_by_its_sort_text()
    {
        CompletionItem[] items = [new("Beta", FilterText: "zeta", SortText: "2"), new("Alpha", FilterText: "zed", SortText: "1"), new("Zoo")];

        Assert.Equal(["Alpha", "Beta", "Zoo"], Completions.Narrow(items, "z").Select(m => m.Item.Label));
    }

    [Fact]
    public void Narrow_with_nothing_typed_keeps_everything_in_the_servers_order()
    {
        CompletionItem[] items = [new("b", SortText: "2"), new("a", SortText: "3"), new("c", SortText: "1")];

        Assert.Equal(["c", "b", "a"], Completions.Narrow(items, "").Select(m => m.Item.Label));
    }

    [Fact]
    public void Apply_replaces_what_was_typed_of_the_name_with_the_label_or_insert_text()
    {
        Assert.Equal((new TextMatch(3, 7, 2), "Append"), Completions.Apply(new CompletionItem("Append"), 3, 7, 9, 9));
        Assert.Equal((new TextMatch(3, 7, 2), "Append()"), Completions.Apply(new CompletionItem("Append", InsertText: "Append()"), 3, 7, 9, 9));
    }

    [Fact]
    public void Apply_uses_the_servers_range_moved_on_by_what_was_typed_since_it_asked()
    {
        var item = new CompletionItem("Append", Edit: new CompletionEdit(3, 6, 8, "@Append"));

        Assert.Equal((new TextMatch(3, 6, 4), "@Append"), Completions.Apply(item, 3, 7, 8, 10));
    }

    [Fact]
    public void Apply_ignores_a_range_on_another_line()
    {
        var item = new CompletionItem("Append", Edit: new CompletionEdit(2, 0, 1, "x"));

        Assert.Equal((new TextMatch(3, 7, 2), "Append"), Completions.Apply(item, 3, 7, 9, 9));
    }

    [Fact]
    public void A_row_is_as_wide_as_its_longest_name_and_detail()
    {
        CompletionMatch[] matches = [new(new CompletionItem("Append", Detail: "void Append()"), []), new(new CompletionItem("Go"), [])];

        Assert.Equal((6, 1 + 2 + 6 + 2 + 13 + 1), Completions.Widths(matches));
    }

    [Fact]
    public void Methods_show_as_f_and_other_kinds_as_a_letter()
    {
        Assert.Equal("ƒ", Completions.KindMark(2));
        Assert.Equal("p", Completions.KindMark(10));
        Assert.Equal(" ", Completions.KindMark(0));
    }
}
