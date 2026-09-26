namespace TuiCode.Workbench.Configuration;

/// <summary>
/// The terminal half of <c>tuicode &lt;path&gt;</c>'s create confirmation (#263). Asked before
/// <c>Application.Init</c>, so a typo is caught on a terminal nothing has drawn on yet. Anything
/// but yes — including a redirected stdin at EOF — declines.
/// </summary>
public static class CreatePrompt
{
    public static bool Ask(TextReader input, TextWriter output, string fullPath, bool directory)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine($"tuicode: {fullPath} does not exist.");
        output.Write($"Create {(directory ? "folder" : "file")}? [y/N] ");
        output.Flush();

        var answer = input.ReadLine()?.Trim();
        if (answer is null) output.WriteLine();
        return answer is not null
            && (answer.Equals("y", StringComparison.OrdinalIgnoreCase)
                || answer.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }
}
