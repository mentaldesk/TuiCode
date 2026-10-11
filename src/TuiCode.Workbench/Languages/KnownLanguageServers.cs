using TuiCode.Abstractions;

namespace TuiCode.Workbench.Languages;

/// <summary>A language server TuiCode can offer by name, with what to run and how to install it.</summary>
public sealed record KnownLanguageServer(string Name, string Command, IReadOnlyList<string> Arguments, string Install)
{
    public LanguageServerSetting Setting { get; } = new(Command, Arguments);
}

/// <summary>The servers Settings › Language Servers offers for each language, and each language's default (#456).</summary>
public static class KnownLanguageServers
{
    public static readonly KnownLanguageServer CSharpLs = new("csharp-ls", "csharp-ls", [], "dotnet tool install -g csharp-ls");

    private static readonly KnownLanguageServer Roslyn = new(
        "roslyn-language-server", "roslyn-language-server", ["--stdio", "--autoLoadProjects"],
        "dotnet tool install -g roslyn-language-server --prerelease");

    private static readonly KnownLanguageServer Gopls = new("gopls", "gopls", [], "go install golang.org/x/tools/gopls@latest");

    private static readonly KnownLanguageServer RustAnalyzer = new("rust-analyzer", "rust-analyzer", [], "rustup component add rust-analyzer");

    private static readonly KnownLanguageServer TypeScript = new(
        "typescript-language-server", "typescript-language-server", ["--stdio"], "npm install -g typescript-language-server typescript");

    private static readonly KnownLanguageServer Pyright = new("pyright", "pyright-langserver", ["--stdio"], "npm install -g pyright");

    private static readonly KnownLanguageServer Pylsp = new("pylsp", "pylsp", [], "pip install python-lsp-server");

    private static readonly Dictionary<string, IReadOnlyList<KnownLanguageServer>> ByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = [CSharpLs, Roslyn],
        ["go"] = [Gopls],
        ["rust"] = [RustAnalyzer],
        ["typescript"] = [TypeScript],
        ["typescriptreact"] = [TypeScript],
        ["javascript"] = [TypeScript],
        ["javascriptreact"] = [TypeScript],
        ["python"] = [Pyright, Pylsp],
    };

    public static IReadOnlyList<KnownLanguageServer> For(string languageId) => ByLanguage.GetValueOrDefault(languageId) ?? [];

    public static LanguageServerSetting Default(string languageId) =>
        string.Equals(languageId, "csharp", StringComparison.OrdinalIgnoreCase) ? CSharpLs.Setting : LanguageServerSetting.None;

    /// <summary>The server <paramref name="languageId"/> runs: the user's choice, or else the default.</summary>
    public static LanguageServerSetting Chosen(string languageId, IReadOnlyDictionary<string, LanguageServerSetting> chosen) =>
        chosen.TryGetValue(languageId, out var setting) ? setting : Default(languageId);

    /// <summary>The known server <paramref name="setting"/> runs, if it's one of <paramref name="languageId"/>'s.</summary>
    public static KnownLanguageServer? Matching(string languageId, LanguageServerSetting setting) =>
        For(languageId).FirstOrDefault(known => known.Setting == setting);

    /// <summary>The known server whose command is <paramref name="command"/>, for its install hint.</summary>
    public static KnownLanguageServer? WithCommand(string languageId, string command) =>
        For(languageId).FirstOrDefault(known => known.Command == command);
}
