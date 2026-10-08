using Terminal.Gui.Drivers;

namespace TuiCode.Editor;

/// <summary>What a copy or cut did (#318): what reached the clipboard, or why nothing did.</summary>
public abstract record CopyOutcome
{
    /// <param name="ThroughTerminal">Only the terminal took it (#443): the platform clipboard didn't, or there isn't one.</param>
    public sealed record Copied(int Lines, int Characters, bool ThroughTerminal = false) : CopyOutcome;

    public sealed record Failed(string Reason) : CopyOutcome;
}

internal static class VerifiedClipboard
{
    // Checked by reading back, not IClipboard.IsSupported, which is false on macOS while the clipboard works (#210).
    public static CopyOutcome Write(IClipboard? clipboard, string text, ClipboardTools? fallback = null)
    {
        var terminal = clipboard as TerminalClipboard;
        var platform = terminal is null ? clipboard : terminal.Platform;
        bool? sent = terminal?.Send(text);

        var failed = TryWrite(platform, text);
        // TG's clipboard on Linux is in-process, so only TuiCode can paste from it.
        var heldOnlyInApp = failed is null && platform is FakeClipboard;
        if (failed is null && !heldOnlyInApp) return Copied(text);
        if (fallback is not null)
        {
            failed = fallback.Write(text);
            if (failed is null) return Copied(text);
        }

        return sent switch
        {
            true => Copied(text, throughTerminal: true),
            false => new CopyOutcome.Failed(TerminalClipboard.TooLarge),
            null when heldOnlyInApp => Copied(text),
            null => failed!,
        };
    }

    private static CopyOutcome.Copied Copied(string text, bool throughTerminal = false)
    {
        var stats = DocumentStats.Of(Lines(text));
        return new CopyOutcome.Copied(stats.Lines, stats.Characters, throughTerminal);
    }

    private static CopyOutcome.Failed? TryWrite(IClipboard? clipboard, string text)
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
        return readBack?.ReplaceLineEndings("\n") == text.ReplaceLineEndings("\n")
            ? null
            : new CopyOutcome.Failed($"the clipboard didn't take the text ({Name(clipboard)})");
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
