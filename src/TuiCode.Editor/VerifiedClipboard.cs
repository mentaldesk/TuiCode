namespace TuiCode.Editor;

/// <summary>What a copy or cut did (#318): what reached the clipboard, or why nothing did.</summary>
public abstract record CopyOutcome
{
    public sealed record Copied(int Lines, int Characters) : CopyOutcome;

    public sealed record Failed(string Reason) : CopyOutcome;
}

internal static class VerifiedClipboard
{
    // Checked by reading back, not IClipboard.IsSupported, which is false on macOS while the clipboard works (#210).
    public static CopyOutcome Write(IClipboard? clipboard, string text)
    {
        if (clipboard is null) return new CopyOutcome.Failed("there is no clipboard");
        string? readBack;
        try
        {
            clipboard.SetClipboardData(text);
            readBack = clipboard.GetClipboardData();
        }
        catch (Exception e)
        {
            return new CopyOutcome.Failed($"{FirstLine(e.Message)} ({Name(clipboard)})");
        }
        if (readBack?.ReplaceLineEndings("\n") != text.ReplaceLineEndings("\n"))
            return new CopyOutcome.Failed($"the clipboard didn't take the text ({Name(clipboard)})");

        var stats = DocumentStats.Of(Lines(text));
        return new CopyOutcome.Copied(stats.Lines, stats.Characters);
    }

    private static string[] Lines(string text)
    {
        text = text.ReplaceLineEndings("\n");
        return (text.EndsWith('\n') ? text[..^1] : text).Split('\n');
    }

    private static string FirstLine(string message) =>
        message.Split('\n', 2)[0].Trim() is { Length: > 0 } line ? line : "the clipboard failed";

    private static string Name(IClipboard clipboard) => clipboard.GetType().Name switch
    {
        "MacOSXClipboard" => "macOS clipboard",
        "WindowsClipboard" => "Windows clipboard",
        "WSLClipboard" => "WSL clipboard",
        "FakeClipboard" => "TuiCode's own clipboard",
        var name => name,
    };
}
