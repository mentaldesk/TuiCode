using TuiCode.Abstractions;
using TuiCode.Syntax;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// Settings › Language Servers (#456): the pane's rows and the dialog's form, TG-free.
public class LanguageServerSettingsTests
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());
    private static readonly LanguageServerSetting Gopls = new("gopls", []);

    [Fact]
    public void Every_highlighted_language_has_a_row_and_only_CSharp_has_a_server_by_default()
    {
        var rows = LanguageServerRows.Build(Syntax.Languages, Chosen(), _ => true);

        Assert.Equal(Syntax.Languages.Count, rows.Count);
        var csharp = Assert.Single(rows, r => r.Server != LanguageServerRows.NoServer);
        Assert.Equal(("csharp", "csharp-ls (default)", (bool?)true), (csharp.Language.Id, csharp.Server, csharp.Installed));
        Assert.All(rows.Where(r => r != csharp), r => Assert.Null(r.Installed));
    }

    [Fact]
    public void A_row_names_a_known_server_a_custom_command_or_none_and_whether_it_is_on_PATH()
    {
        var chosen = Chosen(
            ("go", Gopls),
            ("rust", new LanguageServerSetting("/opt/ra", ["--log", "my file"])),
            ("csharp", LanguageServerSetting.None));

        var rows = LanguageServerRows.Build(Syntax.Languages, chosen, command => command == "gopls");

        Assert.Equal(("gopls", (bool?)true), Row(rows, "go"));
        Assert.Equal(("/opt/ra --log \"my file\"", (bool?)false), Row(rows, "rust"));
        Assert.Equal((LanguageServerRows.NoServer, (bool?)null), Row(rows, "csharp"));
    }

    [Fact]
    public void Rows_line_their_columns_up_under_the_header()
    {
        var rows = LanguageServerRows.Build(Syntax.Languages, Chosen(("go", Gopls)), _ => false);

        var header = LanguageServerRows.Header(rows);
        var go = LanguageServerRows.Display(rows, rows.Single(r => r.Language.Id == "go"));

        Assert.Equal(header.IndexOf("Server", StringComparison.Ordinal), go.IndexOf("gopls", StringComparison.Ordinal));
        Assert.Equal(header.IndexOf("Installed", StringComparison.Ordinal), go.IndexOf('✗'));
    }

    [Fact]
    public void Choosing_a_known_server_fills_in_its_command_and_arguments()
    {
        var form = new LanguageServerForm("python", LanguageServerSetting.None, _ => true);

        Assert.Equal(["None", "pyright", "pylsp", "Custom"], form.Options);
        form.Select(1);

        Assert.Equal(("pyright-langserver", "--stdio"), (form.Command, form.Arguments));
        Assert.Equal(new LanguageServerSetting("pyright-langserver", ["--stdio"]), form.Setting);
    }

    [Fact]
    public void Custom_keeps_what_was_typed_and_None_clears_it()
    {
        var form = new LanguageServerForm("go", Gopls, _ => true);
        Assert.Equal(1, form.Selected);

        form.Select(form.Custom);
        form.Edit("gopls", "-remote=auto");
        Assert.Equal(new LanguageServerSetting("gopls", ["-remote=auto"]), form.Setting);

        form.Select(0);
        Assert.Equal(("", ""), (form.Command, form.Arguments));
        Assert.Equal(LanguageServerSetting.None, form.Setting);
    }

    [Fact]
    public void Typing_picks_the_known_server_the_fields_describe_or_else_Custom()
    {
        var form = new LanguageServerForm("go", LanguageServerSetting.None, _ => true);

        form.Edit("gop", "");
        Assert.Equal(form.Custom, form.Selected);
        form.Edit("gopls", "");
        Assert.Equal(1, form.Selected);
    }

    [Fact]
    public void A_saved_custom_server_opens_on_Custom_with_its_command()
    {
        var form = new LanguageServerForm("go", new LanguageServerSetting("/opt/gopls", ["serve"]), _ => true);

        Assert.Equal((form.Custom, "/opt/gopls", "serve"), (form.Selected, form.Command, form.Arguments));
    }

    [Fact]
    public void The_status_says_whether_the_command_is_on_PATH_and_how_to_install_a_known_server()
    {
        var installed = new HashSet<string> { "pylsp" };
        var form = new LanguageServerForm("go", LanguageServerSetting.None, installed.Contains);
        Assert.Empty(form.Status());

        form.Select(1);
        Assert.Equal(["✗ gopls isn't on PATH.", "  Install: go install golang.org/x/tools/gopls@latest"], form.Status());

        form.Edit("pylsp", "");
        Assert.Equal(["✓ pylsp is on PATH."], form.Status());

        form.Edit("mine", "");
        Assert.Equal(["✗ mine isn't on PATH."], form.Status());

        form.Edit("", "");
        Assert.Equal(["Type the command that starts the server."], form.Status());
        Assert.Equal(LanguageServerSetting.None, form.Setting);
    }

    [Fact]
    public void Arguments_split_on_spaces_and_keep_quoted_ones_whole()
    {
        Assert.Equal(["--stdio", "--log", "a b", ""], ArgumentLine.Split("  --stdio --log \"a b\" \"\""));
        Assert.Equal("--stdio --log \"a b\" \"\"", ArgumentLine.Join(["--stdio", "--log", "a b", ""]));
    }

    [Fact]
    public void A_command_is_found_on_PATH_or_by_its_path()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/usr/bin/gopls", new MockFileData(""));
        fs.AddFile("/opt/ra/rust-analyzer", new MockFileData(""));
        var environment = new FakeEnvironment().Set("PATH", "/usr/local/bin:/usr/bin");

        Assert.True(CommandPath.Exists("gopls", fs, environment));
        Assert.False(CommandPath.Exists("pylsp", fs, environment));
        Assert.True(CommandPath.Exists("/opt/ra/rust-analyzer", fs, environment));
        Assert.False(CommandPath.Exists("/opt/ra/missing", fs, environment));
        Assert.False(CommandPath.Exists("", fs, environment));
    }

    private static (string, bool?) Row(IReadOnlyList<LanguageServerRow> rows, string id) =>
        rows.Single(r => r.Language.Id == id) is var row ? (row.Server, row.Installed) : default;

    private static Dictionary<string, LanguageServerSetting> Chosen(params (string Id, LanguageServerSetting Setting)[] chosen) =>
        chosen.ToDictionary(c => c.Id, c => c.Setting, StringComparer.OrdinalIgnoreCase);
}
