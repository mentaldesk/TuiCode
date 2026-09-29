using System.Globalization;
using System.Text.RegularExpressions;
using Terminal.Gui.Drivers;
using SizeF = System.Drawing.SizeF;

namespace TuiCode.Workbench.About;

internal sealed record SixelSupport(bool IsSupported, SizeF CellPixels)
{
    public static readonly SixelSupport Unsupported = new(false, SizeF.Empty);
}

internal enum CellSizeSource { Iterm2Report, CellResolutionReply, Assumed }

internal sealed record CellMeasurement(SizeF Pixels, CellSizeSource Source, float Scale = 1);

internal static class SixelProbe
{
    private static readonly SizeF DefaultCellPixels = new(10, 20);

    /// <summary>Asks the terminal whether it draws sixel images.</summary>
    public static void Detect(IDriver driver, Action<bool> found)
    {
        if (driver.IsLegacyConsole)
        {
            found(false);
            return;
        }

        Queue(driver, EscSeqUtils.CSI_SendDeviceAttributes, response => found(IndicatesSixel(response)), () => found(false));
    }

    // iTerm2 answers only its own query and most other terminals only CSI 16 t. An unanswered query takes TG a
    // second to abandon, so ask both at once and take the first answer.
    /// <summary>Asks how many pixels a cell is.</summary>
    public static void MeasureCell(IDriver driver, Action<CellMeasurement> found)
    {
        if (driver.IsLegacyConsole)
        {
            found(new CellMeasurement(DefaultCellPixels, CellSizeSource.Assumed));
            return;
        }

        MeasureCell(driver.QueueAnsiRequest, found);
    }

    internal static void MeasureCell(Action<AnsiEscapeSequenceRequest> queue, Action<CellMeasurement> found)
    {
        var settled = false;
        var misses = 0;

        queue(new AnsiEscapeSequenceRequest
        {
            Request = $"{EscSeqUtils.OSC}1337;ReportCellSize{EscSeqUtils.ST}",
            Value = "1337",
            Terminator = EscSeqUtils.ST,
            ResponseReceived = response => Answer(ParseIterm2CellSize(response)),
            Abandoned = () => Answer(null),
        });
        Queue(queue, EscSeqUtils.CSI_RequestSixelResolution,
            response => Answer(ParseCellResolution(response) is { } size
                ? new CellMeasurement(size, CellSizeSource.CellResolutionReply)
                : null),
            () => Answer(null));

        void Answer(CellMeasurement? measurement)
        {
            if (settled) return;
            if (measurement is not null)
            {
                settled = true;
                found(measurement);
            }
            else if (++misses == 2)
            {
                settled = true;
                found(new CellMeasurement(DefaultCellPixels, CellSizeSource.Assumed));
            }
        }
    }

    private static void Queue(IDriver driver, AnsiEscapeSequence sequence, Action<string?> received, Action abandoned) =>
        Queue(driver.QueueAnsiRequest, sequence, received, abandoned);

    private static void Queue(Action<AnsiEscapeSequenceRequest> queue, AnsiEscapeSequence sequence, Action<string?> received, Action abandoned) =>
        queue(new AnsiEscapeSequenceRequest
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

    /// <summary>Reads iTerm2's <c>OSC 1337 ; ReportCellSize=height;width;scale ST</c>, in points times the scale.</summary>
    internal static CellMeasurement? ParseIterm2CellSize(string? response)
    {
        var match = Regex.Match(response ?? "", @"ReportCellSize=([\d.]+);([\d.]+)(?:;([\d.]+))?");
        if (!match.Success || Positive(match.Groups[2].Value, match.Groups[1].Value) is not { } points) return null;
        var scale = float.TryParse(match.Groups[3].Value, CultureInfo.InvariantCulture, out var s) ? Math.Clamp(s, 1, 4) : 1;
        var pixels = scale == 1 ? points : new SizeF(MathF.Round(points.Width * scale), MathF.Round(points.Height * scale));
        return new CellMeasurement(pixels, CellSizeSource.Iterm2Report, scale);
    }

    private static SizeF? Positive(string width, string height) =>
        float.TryParse(width, CultureInfo.InvariantCulture, out var w) && w > 0
        && float.TryParse(height, CultureInfo.InvariantCulture, out var h) && h > 0
            ? new SizeF(w, h)
            : null;
}
