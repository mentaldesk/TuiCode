using TextMateSharp.Registry;

namespace TuiCode.Syntax;

/// <summary><c>--smoke-syntax</c>: CI runs it on each RID's AOT binary, where a broken static Oniguruma link only fails at runtime.</summary>
public static class SyntaxSmoke
{
    private const string SampleLine = "/* comment */ let x = \"text\" + 42; <tag attr='v'> # heading";

    public static int Run(TextWriter output)
    {
        var bundle = GrammarBundle.Load();
        var failures = 0;

        var registry = new Registry(bundle);
        foreach (var scope in bundle.ScopeNames)
        {
            try
            {
                var grammar = registry.LoadGrammar(scope) ?? throw new InvalidOperationException("grammar not found");
                grammar.TokenizeLine(SampleLine, null, TimeSpan.FromSeconds(5));
            }
            catch (Exception e)
            {
                failures++;
                output.WriteLine($"FAIL grammar {scope}: {e.Message}");
            }
        }

        foreach (var theme in new[] { GrammarBundle.DarkTheme, GrammarBundle.LightTheme, GrammarBundle.BorlandTheme })
        {
            try
            {
                registry.SetTheme(bundle.GetTheme(theme) ?? throw new InvalidOperationException("theme not found"));
                if (registry.GetColorMap().Count == 0)
                    throw new InvalidOperationException("theme has no colours");
            }
            catch (Exception e)
            {
                failures++;
                output.WriteLine($"FAIL theme {theme}: {e.Message}");
            }
        }

        output.WriteLine($"Syntax smoke: {bundle.ScopeNames.Count} grammars, {bundle.Languages.Count} languages, {failures} failures");
        return failures == 0 ? 0 : 1;
    }
}
