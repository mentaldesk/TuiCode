using System.Globalization;
using System.Text.RegularExpressions;
using Terminal.Gui.Drivers;

namespace TuiCode.Workbench.Diagnostics;

internal static partial class CursorColourProbe
{
    /// <summary>Asks the terminal for its cursor colour with <c>OSC 12 ; ?</c>; <paramref name="found"/> gets null if it doesn't answer.</summary>
    public static void Read(IDriver driver, Action<string?> found)
    {
        if (driver.IsLegacyConsole)
        {
            found(null);
            return;
        }

        var settled = false;
        var misses = 0;

        // Terminals end the reply with BEL or ST, and TG matches one terminator per request, so ask once and expect both.
        Expect($"{EscSeqUtils.OSC}12;?\a", "\a");
        Expect("", EscSeqUtils.ST);

        void Expect(string request, string terminator) =>
            driver.QueueAnsiRequest(new AnsiEscapeSequenceRequest
            {
                Request = request,
                Value = "12",
                Terminator = terminator,
                ResponseReceived = response => Answer(ParseReply(response)),
                Abandoned = () => Answer(null),
            });

        void Answer(string? colour)
        {
            if (settled) return;
            if (colour is not null || ++misses == 2)
            {
                settled = true;
                found(colour);
            }
        }
    }

    /// <summary>Reads an <c>OSC 12</c> reply as <c>#RRGGBB</c>, scaling wider channels down to 8 bits.</summary>
    internal static string? ParseReply(string? reply)
    {
        var match = Reply().Match(reply ?? "");
        if (!match.Success) return null;
        if (match.Groups["hex"].Success) return "#" + match.Groups["hex"].Value.ToUpperInvariant();

        return "#" + Channel(match.Groups["r"].Value) + Channel(match.Groups["g"].Value) + Channel(match.Groups["b"].Value);
    }

    /// <summary>True when every channel of two <c>#RRGGBB</c> colours is within <see cref="Tolerance"/> 8-bit steps.</summary>
    internal static bool Matches(string asked, string reported) =>
        Enumerable.Range(0, 3).All(i => Math.Abs(ChannelAt(asked, i) - ChannelAt(reported, i)) <= Tolerance);

    // iTerm2 converts the profile's colour between colour spaces and reports it a step off.
    private const int Tolerance = 2;

    private static int ChannelAt(string hex, int index) =>
        int.Parse(hex.AsSpan(1 + 2 * index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string Channel(string digits)
    {
        var value = int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var max = (1 << (4 * digits.Length)) - 1;
        return ((int)Math.Round(value * 255.0 / max)).ToString("X2", CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"\]12;(?:rgb:(?<r>[0-9a-fA-F]{1,4})/(?<g>[0-9a-fA-F]{1,4})/(?<b>[0-9a-fA-F]{1,4})|#(?<hex>[0-9a-fA-F]{6}))(?:\a|\x1b\\)?$")]
    private static partial Regex Reply();
}
