using System.Text;

namespace TuiCode.Editor;

/// <summary>Sends every copy through the terminal (OSC 52) as well as to the platform clipboard, so it reaches the user's machine over SSH (#443).</summary>
public sealed class TerminalClipboard(IClipboard? platform, Action<string> write) : IClipboard
{
    // tmux drops a sequence longer than 1 MiB, and the base64 of this many bytes stays under it.
    public const int MaxBytes = 750_000;

    public const string TooLarge = "too large to send through the terminal (the limit is 750 KB)";

    public IClipboard? Platform => platform;

    public bool IsSupported => true;

    /// <summary>False when the text is too large to send.</summary>
    public bool Send(string text)
    {
        if (Sequence(text) is not { } sequence) return false;
        write(sequence);
        return true;
    }

    internal static string? Sequence(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return bytes.Length > MaxBytes ? null : $"\e]52;c;{Convert.ToBase64String(bytes)}\a";
    }

    public string GetClipboardData() => platform?.GetClipboardData() ?? string.Empty;

    public void SetClipboardData(string text)
    {
        Send(text);
        platform?.SetClipboardData(text);
    }

    public bool TryGetClipboardData(out string result)
    {
        result = string.Empty;
        return platform?.TryGetClipboardData(out result) ?? false;
    }

    public bool TrySetClipboardData(string text)
    {
        var sent = Send(text);
        return (platform?.TrySetClipboardData(text) ?? false) || sent;
    }
}
