using System.Text.Json.Nodes;
using Terminal.Gui.Drawing;
using TuiCode.Editor;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Navigation;

namespace TuiCode.Tests;

public class ProblemsTests
{
    private static readonly string Widget = Path.GetFullPath("/work/Widget.cs");

    [Fact]
    public void Parse_reads_each_problems_range_severity_code_and_message()
    {
        var parsed = LspDiagnostics.Parse(Notification(
            Diagnostic(2, 4, 2, 7, 1, "CS0103", "The name 'nam' does not exist"),
            Diagnostic(5, 0, 6, 3, 2, 168, "Unused\nvariable")))!;

        Assert.Equal(Widget, parsed.Path);
        Assert.Equal(
            [
                new Problem(2, 4, 2, 7, ProblemSeverity.Error, "CS0103", "The name 'nam' does not exist"),
                new Problem(5, 0, 6, 3, ProblemSeverity.Warning, "168", "Unused variable"),
            ],
            parsed.Problems);
    }

    [Fact]
    public void Parse_takes_a_missing_severity_as_an_error_and_leaves_hints_out()
    {
        var missing = Diagnostic(0, 0, 0, 1, null, null, "No severity");
        var parsed = LspDiagnostics.Parse(Notification(missing, Diagnostic(1, 0, 1, 1, 3, null, "Info"), Diagnostic(2, 0, 2, 1, 4, null, "Hint")))!;

        Assert.Equal([ProblemSeverity.Error, ProblemSeverity.Information], parsed.Problems.Select(p => p.Severity));
        Assert.Null(parsed.Problems[0].Code);
    }

    [Fact]
    public void Parse_of_an_empty_list_clears_the_files_problems()
    {
        var parsed = LspDiagnostics.Parse(Notification());

        Assert.Equal(Widget, parsed!.Path);
        Assert.Empty(parsed.Problems);
    }

    [Fact]
    public void Parse_of_a_notification_for_no_file_is_null()
    {
        Assert.Null(LspDiagnostics.Parse(new JsonObject { ["uri"] = "untitled:1", ["diagnostics"] = new JsonArray() }));
    }

    [Fact]
    public void A_problem_over_several_lines_is_split_over_each_and_one_ending_at_a_lines_start_leaves_it_out()
    {
        var lines = ProblemLines.Of([new Problem(1, 4, 3, 0, ProblemSeverity.Warning, null, "")]);

        Assert.Equal([1, 2], lines.Keys.Order());
        Assert.Equal(new ProblemSpan(4, int.MaxValue, ProblemSeverity.Warning), lines[1].Spans.Single());
        Assert.Equal(new ProblemSpan(0, int.MaxValue, ProblemSeverity.Warning), lines[2].Spans.Single());
    }

    [Fact]
    public void A_lines_number_takes_its_worst_problem()
    {
        var lines = ProblemLines.Of(
        [
            new Problem(0, 0, 0, 1, ProblemSeverity.Information, null, ""),
            new Problem(0, 2, 0, 3, ProblemSeverity.Error, null, ""),
            new Problem(0, 4, 0, 5, ProblemSeverity.Warning, null, ""),
            new Problem(1, 0, 1, 1, ProblemSeverity.Information, null, ""),
            new Problem(1, 0, 1, 1, ProblemSeverity.Warning, null, ""),
        ]);

        Assert.Equal(ProblemSeverity.Error, lines[0].Worst);
        Assert.Equal(ProblemSeverity.Warning, lines[1].Worst);
    }

    [Theory]
    [InlineData(3, 6, 2, null)]
    [InlineData(3, 6, 3, ProblemSeverity.Error)]
    [InlineData(3, 6, 5, ProblemSeverity.Error)]
    [InlineData(3, 6, 6, null)]
    public void At_marks_the_chars_inside_the_span(int start, int end, int at, ProblemSeverity? expected)
    {
        var line = ProblemLines.Of([new Problem(0, start, 0, end, ProblemSeverity.Error, null, "")])[0];

        Assert.Equal(expected, ProblemLines.At(line, at, at + 1, 10));
    }

    [Fact]
    public void At_marks_the_grapheme_an_empty_span_starts_at_and_the_last_one_for_a_span_past_the_end()
    {
        var empty = ProblemLines.Of([new Problem(0, 2, 0, 2, ProblemSeverity.Error, null, "")])[0];
        var past = ProblemLines.Of([new Problem(0, 40, 0, 50, ProblemSeverity.Error, null, "")])[0];

        Assert.Equal(ProblemSeverity.Error, ProblemLines.At(empty, 2, 3, 10));
        Assert.Null(ProblemLines.At(empty, 3, 4, 10));
        Assert.Equal(ProblemSeverity.Error, ProblemLines.At(past, 9, 10, 10));
        Assert.Null(ProblemLines.At(past, 8, 9, 10));
    }

    [Fact]
    public void At_counts_a_surrogate_pair_as_the_two_chars_the_server_does()
    {
        // "😀 x": the emoji is chars 0-1, so the server puts "x" at 3.
        var line = ProblemLines.Of([new Problem(0, 3, 0, 4, ProblemSeverity.Error, null, "")])[0];

        Assert.Null(ProblemLines.At(line, 0, 2, 4));
        Assert.Equal(ProblemSeverity.Error, ProblemLines.At(line, 3, 4, 4));
    }

    [Fact]
    public void The_caret_is_on_a_problem_from_its_start_to_just_after_its_end()
    {
        var problem = new Problem(1, 4, 1, 7, ProblemSeverity.Error, null, "");

        Assert.Equal([false, true, true, false], new[] { (1, 3), (1, 4), (1, 7), (1, 8) }.Select(p => problem.Covers(p.Item1, p.Item2)));
    }

    [Fact]
    public void Colours_come_from_the_token_theme_first()
    {
        var theme = new Dictionary<string, Color>
        {
            [ProblemColours.ErrorKey] = new(1, 0, 0),
            [ProblemColours.WarningKey] = new(2, 0, 0),
            [ProblemColours.InformationKey] = new(3, 0, 0),
        };

        var colours = ProblemColours.Resolve(key => theme.TryGetValue(key, out var c) ? c : null, _ => new Color(9, 9, 9));

        Assert.Equal(new ProblemColours(new(1, 0, 0), new(2, 0, 0), new(3, 0, 0)), colours);
    }

    [Fact]
    public void Without_them_colours_fall_back_to_the_Error_Warning_and_Accent_schemes()
    {
        var schemes = new Dictionary<string, Color> { ["Error"] = new(1, 0, 0), ["Warning"] = new(2, 0, 0), ["Accent"] = new(3, 0, 0) };

        var colours = ProblemColours.Resolve(_ => null, name => schemes.TryGetValue(name, out var c) ? c : null);

        Assert.Equal(new ProblemColours(new(1, 0, 0), new(2, 0, 0), new(3, 0, 0)), colours);
    }

    [Fact]
    public void Without_a_Warning_scheme_a_warning_takes_the_error_colour()
    {
        var colours = ProblemColours.Resolve(_ => null, name => name == "Error" ? new Color(1, 0, 0) : null);

        Assert.Equal(new Color(1, 0, 0), colours.Warning);
    }

    [Fact]
    public void Every_bundled_token_theme_has_its_own_problem_colours()
    {
        foreach (var theme in new[] { "midnight.json", "daylight.json", "turbo-pascal.json", "modern-borland.json" })
        {
            var highlighter = new TuiCode.Syntax.SyntaxHighlighter(TuiCode.Syntax.GrammarBundle.Load());
            highlighter.UseTheme(theme);
            Assert.All([ProblemColours.ErrorKey, ProblemColours.WarningKey, ProblemColours.InformationKey],
                key => Assert.True(highlighter.EditorColors.ContainsKey(key), $"{theme} has no {key}"));
        }
    }

    [Fact]
    public void Quick_info_lays_one_problem_alone_with_its_mark_and_code()
    {
        var info = QuickInfo.Lay([Error("CS0103", "The name 'nam' does not exist")], [], 80);

        Assert.Equal(["✖ CS0103 The name 'nam' does not exist"], info.Rows.Select(r => r.Text));
        Assert.Equal(ProblemSeverity.Error, info.Rows[0].Mark);
    }

    [Fact]
    public void Quick_info_hangs_a_long_problems_wrapped_lines_under_its_message()
    {
        var info = QuickInfo.Lay([Error("CS0103", "one two three four")], [], 14);

        Assert.Equal(["✖ CS0103 one", "  two three", "  four"], info.Rows.Select(r => r.Text));
        Assert.Equal([ProblemSeverity.Error, null, null], info.Rows.Select(r => r.Mark));
    }

    [Fact]
    public void Quick_info_lays_several_problems_then_a_rule_then_the_hover()
    {
        var warning = new Problem(0, 0, 0, 1, ProblemSeverity.Warning, null, "Unused");
        var info = QuickInfo.Lay([Error("CS0103", "Missing"), warning], [new HoverPart("csharp", "int x"), new HoverPart(null, "Docs.")], 80);

        Assert.Equal(["✖ CS0103 Missing", "⚠ Unused", "", "int x", "", "Docs."], info.Rows.Select(r => r.Text));
        Assert.Equal([false, false, true, false, false, false], info.Rows.Select(r => r.Rule));
        Assert.True(info.Rows[3].IsCode);
    }

    [Fact]
    public void Quick_info_with_a_hover_and_no_problems_has_no_rule()
    {
        var info = QuickInfo.Lay([], [new HoverPart("csharp", "int x")], 80);

        Assert.DoesNotContain(info.Rows, r => r.Rule);
    }

    private static Problem Error(string code, string message) => new(0, 0, 0, 1, ProblemSeverity.Error, code, message);

    private static JsonObject Notification(params JsonObject[] diagnostics) => new()
    {
        ["uri"] = LspLocations.ToUri(Widget),
        ["diagnostics"] = new JsonArray([.. diagnostics.Cast<JsonNode>()]),
    };

    private static JsonObject Diagnostic(int startLine, int startCharacter, int endLine, int endCharacter, int? severity, JsonNode? code, string message)
    {
        var diagnostic = new JsonObject
        {
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject { ["line"] = startLine, ["character"] = startCharacter },
                ["end"] = new JsonObject { ["line"] = endLine, ["character"] = endCharacter },
            },
            ["message"] = message,
        };
        if (severity is { } s) diagnostic["severity"] = s;
        if (code is not null) diagnostic["code"] = code;
        return diagnostic;
    }
}
