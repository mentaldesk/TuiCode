using System.Globalization;
using System.Text.RegularExpressions;
using Terminal.Gui.Drivers;
using SizeF = System.Drawing.SizeF;

namespace TuiCode.Workbench.About;

internal sealed record SixelSupport(bool IsSupported, SizeF CellPixels)
{
    public static readonly SixelSupport Unsupported = new(false, SizeF.Empty);
}

internal static class SixelProbe
{
    private static readonly SizeF DefaultCellPixels = new(10, 20);

    /// <summary>Asks the terminal whether it draws sixel images and how many pixels a cell is.</summary>
    public static void Detect(IDriver driver, Action<SixelSupport> found)
    {
        if (driver.IsLegacyConsole)
        {
            found(SixelSupport.Unsupported);
            return;
        }

        Queue(driver, EscSeqUtils.CSI_SendDeviceAttributes,
            response =>
            {
                if (IndicatesSixel(response))
                    FindCellPixels(driver, cellPixels => found(new SixelSupport(true, cellPixels)));
                else
                    found(SixelSupport.Unsupported);
            },
            () => found(SixelSupport.Unsupported));
    }

    // iTerm2 answers only its own query and most other terminals only CSI 16 t. An unanswered query takes TG a
    // second to abandon, so ask both at once and take the first answer.
    private static void FindCellPixels(IDriver driver, Action<SizeF> found)
    {
        var settled = false;
        var misses = 0;

        driver.QueueAnsiRequest(new AnsiEscapeSequenceRequest
        {
            Request = $"{EscSeqUtils.OSC}1337;ReportCellSize{EscSeqUtils.ST}",
            Value = "1337",
            Terminator = EscSeqUtils.ST,
            ResponseReceived = response => Answer(ParseIterm2CellSize(response)),
            Abandoned = () => Answer(null),
        });
        Queue(driver, EscSeqUtils.CSI_RequestSixelResolution,
            response => Answer(ParseCellResolution(response)),
            () => Answer(null));

        void Answer(SizeF? cellPixels)
        {
            if (settled) return;
            if (cellPixels is { } size)
            {
                settled = true;
                found(size);
            }
            else if (++misses == 2)
            {
                settled = true;
                found(DefaultCellPixels);
            }
        }
    }

    private static void Queue(IDriver driver, AnsiEscapeSequence sequence, Action<string?> received, Action abandoned) =>
        driver.QueueAnsiRequest(new AnsiEscapeSequenceRequest
        {
            Request = sequence.Request,
            Value = sequence.Value,
            Terminator = sequence.Terminator,
            ResponseReceived = received,
            Abandoned = abandoned,
        });

    /// <summary>Attribute 4 in a primary device attributes reply means sixel graphics.</summary>
    internal static bool IndicatesSixel(string? response) =>
        response is not null && response.TrimEnd('c').Split(';').Contains("4");

    /// <summary>Reads a <c>CSI 6 ; height ; width t</c> reply.</summary>
    internal static SizeF? ParseCellResolution(string? response)
    {
        var match = Regex.Match(response ?? "", @"\[6;(\d+);(\d+)t$");
        return match.Success ? Positive(match.Groups[2].Value, match.Groups[1].Value) : null;
    }

    /// <summary>Reads iTerm2's <c>OSC 1337 ; ReportCellSize=height;width;scale ST</c>.</summary>
    internal static SizeF? ParseIterm2CellSize(string? response)
    {
        var match = Regex.Match(response ?? "", @"ReportCellSize=([\d.]+);([\d.]+)");
        return match.Success ? Positive(match.Groups[2].Value, match.Groups[1].Value) : null;
    }

    private static SizeF? Positive(string width, string height) =>
        float.TryParse(width, CultureInfo.InvariantCulture, out var w) && w > 0
        && float.TryParse(height, CultureInfo.InvariantCulture, out var h) && h > 0
            ? new SizeF(w, h)
            : null;
}
