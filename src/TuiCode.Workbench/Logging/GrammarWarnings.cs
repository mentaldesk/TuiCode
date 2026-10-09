using Microsoft.Extensions.Logging;
using TuiCode.Syntax;

namespace TuiCode.Workbench.Logging;

/// <summary>Logs the user grammar packages that couldn't be read, and each grammar that fails as it loads.</summary>
public static class GrammarWarnings
{
    public static void Log(SyntaxHighlighter syntax, string userGrammars, ILogger logger)
    {
        foreach (var problem in syntax.Problems)
            logger.LogWarning("Skipped a grammar package in {Directory}: {Problem}", userGrammars, problem);
        syntax.GrammarFailed += (_, failure) =>
            logger.LogWarning(failure.Exception, "Couldn't load the {Language} grammar from {File}",
                failure.Language.Name, failure.File ?? "TuiCode's built-in grammars");
    }
}
