using TuiCode.Abstractions;
using TuiCode.Editor;

namespace TuiCode.Tests;

public class EditorConfigFileTests
{
    [Fact]
    public void Parse_reads_sections_in_order_and_skips_comments_and_blank_lines()
    {
        var file = EditorConfigFile.Parse("""
            # top
            root = true

            [*]
            ; a comment
            Indent_Style = space
            [*.md]
            indent_size = 2
            """);

        Assert.True(file.Root);
        Assert.Equal(["*", "*.md"], file.Sections.Select(s => s.Glob));
        Assert.Equal([new("indent_style", "space")], file.Sections[0].Properties);
        Assert.Equal([new("indent_size", "2")], file.Sections[1].Properties);
    }

    [Fact]
    public void Parse_takes_root_only_before_the_first_section()
    {
        Assert.False(EditorConfigFile.Parse("[*]\nroot = true\n").Root);
        Assert.True(EditorConfigFile.Parse("ROOT = True\r\n[*]\r\n").Root);
    }
}

public class EditorConfigGlobTests
{
    [Theory]
    [InlineData("*.c", "a.c", true)]
    [InlineData("*.c", "dir/sub/a.c", true)]
    [InlineData("*.c", "a.cc", false)]
    [InlineData("a*e.c", "abcde.c", true)]
    [InlineData("a*e.c", "a/e.c", false)]
    [InlineData("a**z.c", "a/b/z.c", true)]
    [InlineData("a**z.c", "az.c", true)]
    [InlineData("a/**/z.c", "a/z.c", true)]
    [InlineData("a/**/z.c", "a/b/c/z.c", true)]
    [InlineData("a/**/z.c", "b/a/z.c", false)]
    [InlineData("som?.c", "some.c", true)]
    [InlineData("som?.c", "so.c", false)]
    [InlineData("som?.c", "som/.c", false)]
    [InlineData("[ab].a", "b.a", true)]
    [InlineData("[ab].a", "c.a", false)]
    [InlineData("[!ab].b", "c.b", true)]
    [InlineData("[!ab].b", "a.b", false)]
    [InlineData("[a-c].c", "b.c", true)]
    [InlineData("[a-c].c", "d.c", false)]
    [InlineData("{a,bc}.x", "bc.x", true)]
    [InlineData("{a,bc}.x", "b.x", false)]
    [InlineData("{word,{also},this}.g", "{also}.g", true)]
    [InlineData("{word,{also},this}.g", "this.g", true)]
    [InlineData("*.{csproj,props}", "src/App.props", true)]
    [InlineData("{single}.b", "{single}.b", true)]
    [InlineData("{single}.b", "single.b", false)]
    [InlineData("{}.c", "{}.c", true)]
    [InlineData("{1..3}.d", "1.d", true)]
    [InlineData("{1..3}.d", "3.d", true)]
    [InlineData("{1..3}.d", "4.d", false)]
    [InlineData("{10..1}.d", "7.d", true)]
    [InlineData("{-3..3}.d", "-2.d", true)]
    [InlineData("{-3..3}.d", "-4.d", false)]
    [InlineData(@"\*.c", "*.c", true)]
    [InlineData(@"\*.c", "a.c", false)]
    [InlineData("/top.c", "top.c", true)]
    [InlineData("/top.c", "sub/top.c", false)]
    [InlineData("sub/*.c", "sub/a.c", true)]
    [InlineData("sub/*.c", "x/sub/a.c", false)]
    [InlineData("sub/*.c", "sub/deeper/a.c", false)]
    [InlineData("*", "any/thing", true)]
    [InlineData("**.verified.txt", "tests/Api.Run.verified.txt", true)]
    public void Matches_follows_the_editorconfig_glob_rules(string glob, string path, bool matches) =>
        Assert.Equal(matches, EditorConfigGlob.Matches(glob, path));
}

public class EditorConfigLookupTests
{
    private readonly MockFileSystem _fs = new();

    private string Full(string path) => _fs.Path.GetFullPath(path);

    [Fact]
    public void A_closer_file_and_a_later_section_win()
    {
        _fs.AddFile("/repo/.editorconfig", new MockFileData("[*]\nindent_size = 4\nend_of_line = lf\n[*.xml]\nindent_size = 3\n"));
        _fs.AddFile("/repo/src/.editorconfig", new MockFileData("[*.xml]\nindent_size = 2\n[*]\nend_of_line = crlf\n"));
        _fs.AddFile("/repo/src/a.xml", new MockFileData(""));

        var properties = EditorConfig.PropertiesFor(_fs, Full("/repo/src/a.xml"));

        Assert.Equal("2", properties["indent_size"]);
        Assert.Equal("crlf", properties["end_of_line"]);
    }

    [Fact]
    public void Lookup_stops_at_the_file_marked_root()
    {
        _fs.AddFile("/.editorconfig", new MockFileData("[*]\ninsert_final_newline = false\n"));
        _fs.AddFile("/repo/.editorconfig", new MockFileData("root = true\n[*]\nindent_style = tab\n"));
        _fs.AddFile("/repo/a.txt", new MockFileData(""));

        var properties = EditorConfig.PropertiesFor(_fs, Full("/repo/a.txt"));

        Assert.Equal(new Dictionary<string, string> { ["indent_style"] = "tab" }, properties);
    }

    [Fact]
    public void A_glob_with_a_slash_is_relative_to_its_own_file()
    {
        _fs.AddFile("/repo/.editorconfig", new MockFileData("[src/*.cs]\nindent_size = 2\n"));
        _fs.AddFile("/repo/src/a.cs", new MockFileData(""));
        _fs.AddFile("/repo/lib/src/b.cs", new MockFileData(""));

        Assert.Equal("2", EditorConfig.PropertiesFor(_fs, Full("/repo/src/a.cs"))["indent_size"]);
        Assert.Empty(EditorConfig.PropertiesFor(_fs, Full("/repo/lib/src/b.cs")));
    }

    [Fact]
    public void Unset_takes_a_property_back_off()
    {
        _fs.AddFile("/repo/.editorconfig", new MockFileData("[*]\nindent_size = 2\n[*.md]\nindent_size = unset\n"));
        _fs.AddFile("/repo/a.md", new MockFileData(""));

        Assert.Empty(EditorConfig.PropertiesFor(_fs, Full("/repo/a.md")));
    }

    [Fact]
    public void No_editorconfig_means_no_rules()
    {
        _fs.AddFile("/repo/a.txt", new MockFileData(""));

        Assert.Equal(EditorConfigRules.None, EditorConfigRules.For(_fs, Full("/repo/a.txt")));
    }

    [Fact]
    public void Keys_and_values_are_case_insensitive()
    {
        _fs.AddFile("/repo/.editorconfig", new MockFileData("[*]\nIndent_Style = TAB\nEnd_Of_Line = CRLF\nInsert_Final_Newline = False\n"));
        _fs.AddFile("/repo/a.txt", new MockFileData(""));

        var rules = EditorConfigRules.For(_fs, Full("/repo/a.txt"));

        Assert.False(rules.InsertSpaces);
        Assert.Equal(LineEnding.CRLF, rules.LineEnding);
        Assert.False(rules.InsertFinalNewline);
    }
}

public class EditorConfigRulesTests
{
    private static EditorConfigRules From(params (string Key, string Value)[] properties) =>
        EditorConfigRules.From(properties.ToDictionary(p => p.Key, p => p.Value));

    [Fact]
    public void Indent_size_tab_takes_the_tab_width()
    {
        var rules = From(("indent_style", "tab"), ("indent_size", "tab"), ("tab_width", "8"));

        Assert.Equal(8, rules.IndentSize);
        Assert.Equal(8, rules.TabWidth);
    }

    [Fact]
    public void Tab_indentation_without_a_size_takes_the_tab_width()
    {
        Assert.Equal(3, From(("indent_style", "tab"), ("tab_width", "3")).IndentSize);
    }

    [Fact]
    public void An_indent_size_without_a_tab_width_is_the_tab_width_too()
    {
        Assert.Equal(2, From(("indent_size", "2")).TabWidth);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("12", 8)]
    [InlineData("4", 4)]
    public void Sizes_are_clamped_to_one_to_eight(string size, int expected)
    {
        Assert.Equal(expected, From(("indent_size", size)).IndentSize);
    }

    [Fact]
    public void Values_it_doesnt_know_are_ignored()
    {
        var rules = From(("indent_style", "both"), ("indent_size", "wide"), ("end_of_line", "cr"), ("insert_final_newline", "maybe"));

        Assert.False(rules.SetsIndentation);
        Assert.Null(rules.LineEnding);
        Assert.Null(rules.InsertFinalNewline);
    }
}

public class IndentGuessTests
{
    [Fact]
    public void Mostly_tabs_is_tabs()
    {
        Assert.Equal(new IndentGuess(false, 0), IndentGuess.Of(["all:", "\tgcc a.c", "\tgcc b.c", "  x"]));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void Spaces_take_the_step_they_mostly_use(int size)
    {
        var pad = new string(' ', size);
        string[] lines = ["<a>", pad + "<b>", pad + pad + "<c/>", pad + "</b>", pad + "<d/>", "</a>"];

        Assert.Equal(new IndentGuess(true, size), IndentGuess.Of(lines));
    }

    [Fact]
    public void A_few_outlier_lines_dont_change_the_guess()
    {
        string[] lines =
        [
            "class A", "{", "    /**", "     * doc", "     */", "    void M()", "    {", "        x();",
            "            .y();", "    }", "    int P;", "}",
        ];

        Assert.Equal(new IndentGuess(true, 4), IndentGuess.Of(lines));
    }

    [Fact]
    public void Mixed_spaces_and_tabs_go_by_whichever_most_lines_use()
    {
        Assert.Equal(new IndentGuess(true, 2), IndentGuess.Of(["a", "  b", "  c", "\td", "e"]));
        Assert.Equal(new IndentGuess(false, 0), IndentGuess.Of(["a", "  b", "\tc", "\td", "e"]));
    }

    [Fact]
    public void Nothing_indented_is_no_guess()
    {
        Assert.Null(IndentGuess.Of([]));
        Assert.Null(IndentGuess.Of(["a", "", "   ", "b"]));
    }
}

public class FileFormatTests
{
    private static readonly IndentGuess TwoSpaces = new(true, 2);

    [Fact]
    public void Editorconfig_wins_over_the_files_contents()
    {
        var format = FileFormat.Of(EditorSettings.Default, new EditorConfigRules { InsertSpaces = false, TabWidth = 8, IndentSize = 8 }, TwoSpaces);

        Assert.Equal((false, 8, IndentSource.EditorConfig), (format.InsertSpaces, format.IndentSize, format.IndentFrom));
    }

    [Fact]
    public void The_files_contents_win_over_Settings()
    {
        var format = FileFormat.Of(EditorSettings.Default with { IndentSize = 4 }, EditorConfigRules.None, TwoSpaces);

        Assert.Equal((true, 2, IndentSource.Detected), (format.InsertSpaces, format.IndentSize, format.IndentFrom));
    }

    [Fact]
    public void Turning_detection_off_falls_back_to_Settings()
    {
        var format = FileFormat.Of(EditorSettings.Default with { DetectIndentation = false }, EditorConfigRules.None, TwoSpaces);

        Assert.Equal((true, 4, IndentSource.Settings), (format.InsertSpaces, format.IndentSize, format.IndentFrom));
    }

    [Fact]
    public void Detected_tabs_are_drawn_at_the_Settings_width()
    {
        var format = FileFormat.Of(EditorSettings.Default with { IndentSize = 3 }, EditorConfigRules.None, new IndentGuess(false, 0));

        Assert.Equal((false, 3, IndentSource.Detected), (format.InsertSpaces, format.IndentSize, format.IndentFrom));
    }

    [Fact]
    public void An_editorconfig_style_without_a_size_takes_the_detected_size()
    {
        var format = FileFormat.Of(EditorSettings.Default, new EditorConfigRules { InsertSpaces = true }, TwoSpaces);

        Assert.Equal((true, 2, IndentSource.EditorConfig), (format.InsertSpaces, format.IndentSize, format.IndentFrom));
    }

    [Fact]
    public void Line_endings_and_final_newline_come_from_editorconfig_before_Settings()
    {
        var settings = EditorSettings.Default with { LineEnding = LineEnding.LF, InsertFinalNewline = true };

        var configured = FileFormat.Of(settings, new EditorConfigRules { LineEnding = LineEnding.CRLF, InsertFinalNewline = false }, null);
        var fromSettings = FileFormat.Of(settings, EditorConfigRules.None, null);

        Assert.Equal((LineEnding.CRLF, false, true), (configured.LineEnding, configured.InsertFinalNewline, configured.FinalNewlineFromEditorConfig));
        Assert.Equal((LineEnding.LF, true, false), (fromSettings.LineEnding, fromSettings.InsertFinalNewline, fromSettings.FinalNewlineFromEditorConfig));
    }

    [Theory]
    [InlineData(true, 2, IndentSource.Detected, "2 spaces (detected)")]
    [InlineData(true, 1, IndentSource.Settings, "1 space (Settings)")]
    [InlineData(false, 4, IndentSource.EditorConfig, "tabs (.editorconfig)")]
    [InlineData(true, 4, IndentSource.Settings, "4 spaces (Settings)")]
    public void IndentDescription_says_what_and_where_from(bool spaces, int size, IndentSource from, string expected)
    {
        Assert.Equal(expected, new FileFormat(size, spaces, from, LineEnding.Auto, true, false).IndentDescription);
    }

    [Fact]
    public void FinalNewlineDescription_appears_only_when_editorconfig_sets_it()
    {
        Assert.Null(new FileFormat(4, true, IndentSource.Settings, LineEnding.Auto, true, false).FinalNewlineDescription);
        Assert.Equal("no final newline (.editorconfig)",
            new FileFormat(4, true, IndentSource.Settings, LineEnding.Auto, false, true).FinalNewlineDescription);
        Assert.Equal("final newline (.editorconfig)",
            new FileFormat(4, true, IndentSource.Settings, LineEnding.Auto, true, true).FinalNewlineDescription);
    }
}

public class EditorTabFileFormatTests
{
    private const string Csproj = "<Project>\n  <PropertyGroup>\n    <A>1</A>\n  </PropertyGroup>\n</Project>\n";
    private readonly MockFileSystem _fs = new();

    private EditorGroup Open(string path, string content, EditorSettings? settings = null)
    {
        _fs.AddFile(path, new MockFileData(content));
        var group = new EditorGroup { Settings = settings ?? EditorSettings.Default };
        group.OpenOrFocus(_fs.FileInfo.New(path));
        return group;
    }

    [Fact]
    public void Tab_on_a_new_line_in_a_two_space_file_inserts_two_spaces()
    {
        using var group = Open("/repo/App.csproj", Csproj);
        var tab = group.ActiveTab!;
        tab.MoveCursor(5, 0);

        tab.IndentLines();

        Assert.Equal("  ", tab.Lines[5]);
        Assert.Equal("2 spaces (detected)", tab.Format.IndentDescription);
    }

    [Fact]
    public void Shift_Tab_and_indent_lines_move_a_two_space_file_by_two()
    {
        using var group = Open("/repo/App.csproj", Csproj);
        var tab = group.ActiveTab!;
        tab.MoveCursor(2, 0);

        tab.OutdentLines();
        Assert.Equal("  <A>1</A>", tab.Lines[2]);

        tab.Code.SetCarets([new Caret(new System.Drawing.Point(0, 2), new System.Drawing.Point(0, 1))]);
        tab.IndentLines();
        Assert.Equal("    <PropertyGroup>", tab.Lines[1]);
    }

    [Fact]
    public void A_four_space_file_still_indents_by_four()
    {
        using var group = Open("/repo/A.cs", "class A\n{\n    void M() { }\n}\n");
        var tab = group.ActiveTab!;
        tab.MoveCursor(4, 0);

        tab.IndentLines();

        Assert.Equal("    ", tab.Lines[4]);
    }

    [Fact]
    public void Tab_in_a_tab_indented_file_inserts_a_tab_drawn_at_the_editorconfig_width()
    {
        _fs.AddFile("/repo/.editorconfig", new MockFileData("[Makefile]\ntab_width = 8\n"));
        using var group = Open("/repo/Makefile", "all:\n\tgcc a.c\n");
        var tab = group.ActiveTab!;
        tab.MoveCursor(2, 0);

        tab.IndentLines();

        Assert.Equal("\t", tab.Lines[2]);
        Assert.Equal(8, tab.Code.TabWidth);
    }

    [Fact]
    public void An_unindented_or_empty_file_uses_the_Settings_defaults()
    {
        var settings = EditorSettings.Default with { IndentSize = 3, InsertSpaces = false };
        using var group = Open("/repo/a.txt", "one\ntwo\n", settings);
        _fs.AddFile("/repo/b.txt", new MockFileData(""));
        group.OpenOrFocus(_fs.FileInfo.New("/repo/b.txt"));

        foreach (var tab in group.Tabs)
            Assert.Equal((false, 3, IndentSource.Settings), (tab.Format.InsertSpaces, tab.Format.IndentSize, tab.Format.IndentFrom));
    }

    [Fact]
    public void Editorconfig_indentation_wins_over_the_files_own()
    {
        _fs.AddFile("/repo/.editorconfig", new MockFileData("[*.csproj]\nindent_style = space\nindent_size = 4\n"));
        using var group = Open("/repo/App.csproj", Csproj);
        var tab = group.ActiveTab!;
        tab.MoveCursor(5, 0);

        tab.IndentLines();

        Assert.Equal("    ", tab.Lines[5]);
        Assert.Equal("4 spaces (.editorconfig)", tab.Format.IndentDescription);
    }

    [Fact]
    public void Without_detection_a_file_with_no_editorconfig_rules_uses_Settings()
    {
        using var group = Open("/repo/App.csproj", Csproj, EditorSettings.Default with { DetectIndentation = false });

        Assert.Equal("4 spaces (Settings)", group.ActiveTab!.Format.IndentDescription);
    }

    [Theory]
    [InlineData("false", "hello")]
    [InlineData("true", "hello\n")]
    public void Save_follows_editorconfigs_final_newline(string insert, string expected)
    {
        _fs.AddFile("/repo/.editorconfig", new MockFileData($"[*.verified.txt]\ninsert_final_newline = {insert}\n"));
        using var group = Open("/repo/Api.verified.txt", "hello", EditorSettings.Default with { InsertFinalNewline = insert != "true" });
        group.ActiveTab!.Content = "hello";

        group.SaveActive();

        Assert.Equal(expected, _fs.File.ReadAllText("/repo/Api.verified.txt"));
    }

    [Theory]
    [InlineData("crlf", "a\nb\n", "a\r\nb\r\n")]
    [InlineData("lf", "a\r\nb\r\n", "a\nb\n")]
    public void Save_writes_editorconfigs_line_ending(string endOfLine, string original, string expected)
    {
        _fs.AddFile("/repo/.editorconfig", new MockFileData($"[*]\nend_of_line = {endOfLine}\n"));
        using var group = Open("/repo/a.txt", original);

        group.SaveActive();

        Assert.Equal(expected, _fs.File.ReadAllText("/repo/a.txt"));
    }

    [Fact]
    public void Save_reads_editorconfig_again()
    {
        using var group = Open("/repo/a.txt", "a\nb");
        _fs.AddFile("/repo/.editorconfig", new MockFileData("[*]\nend_of_line = crlf\ninsert_final_newline = false\nindent_size = 2\n"));

        group.SaveActive();

        Assert.Equal("a\r\nb", _fs.File.ReadAllText("/repo/a.txt"));
        Assert.Equal("no final newline (.editorconfig)", group.ActiveTab!.Format.FinalNewlineDescription);
        Assert.Equal(IndentSource.Settings, group.ActiveTab.Format.IndentFrom);
    }
}
