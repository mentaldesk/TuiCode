using System.Globalization;
using System.Text.RegularExpressions;
using Terminal.Gui.Drivers;
using SizeF = System.Drawing.SizeF;

namespace TuiCode.Workbench.About;

internal static class SixelProbe
{
    /// <summary>Asks the terminal whether it draws sixel images and how many pixels a cell is; calls back on the UI thread.</summary>
    public static void Detect(IApplication app, Action<SixelSupportResult, SizeF> found)
    {
        var driver = app.Driver!;
        new SixelSupportDetector(driver).Detect(result =>
        {
            if (!result.IsSupported)
            {
                Found(result.Resolution);
                return;
            }
            // iTerm2 doesn't answer CSI 16 t, and the detector's fallback counts the title bar and margins as cells.
            driver.QueueAnsiRequest(new AnsiEscapeSequenceRequest
            {
                Request = $"{EscSeqUtils.OSC}1337;ReportCellSize{EscSeqUtils.ST}",
                Value = "1337",
                Terminator = EscSeqUtils.ST,
                ResponseReceived = response => Found(ParseIterm2CellSize(response) ?? result.Resolution),
                Abandoned = () => Found(result.Resolution),
            });

            void Found(SizeF cellPixels) => app.Invoke(() => found(result, cellPixels));
        });
    }

    /// <summary>Reads iTerm2's <c>OSC 1337 ; ReportCellSize=height;width;scale ST</c>.</summary>
    internal static SizeF? ParseIterm2CellSize(string? response)
    {
        var match = Regex.Match(response ?? "", @"ReportCellSize=([\d.]+);([\d.]+)");
        if (!match.Success
            || !float.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var height)
            || !float.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out var width)
            || width <= 0 || height <= 0)
            return null;
        return new SizeF(width, height);
    }
}
