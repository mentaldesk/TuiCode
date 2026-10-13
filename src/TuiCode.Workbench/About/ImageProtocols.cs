using Terminal.Gui.Drivers;
using Size = System.Drawing.Size;
using SizeF = System.Drawing.SizeF;

namespace TuiCode.Workbench.About;

internal enum ImageProtocol { None, Sixel, Kitty }

internal static class ImageProtocols
{
    /// <summary>Kitty graphics where Terminal.Gui found it outside tmux, else sixel where our probe found it. Null while that's unknown.</summary>
    public static ImageProtocol? Choose(bool kittyDetected, bool inTmux, bool? sixel) =>
        kittyDetected && !inTmux ? ImageProtocol.Kitty
        : sixel switch
        {
            true => ImageProtocol.Sixel,
            false => ImageProtocol.None,
            null => null,
        };

    public static string Describe(ImageProtocol? protocol) => protocol switch
    {
        ImageProtocol.Kitty => "kitty graphics",
        ImageProtocol.Sixel => "sixel",
        ImageProtocol.None => "none (ASCII art)",
        _ => "asking the terminal…",
    };

    /// <summary>Tells the driver what our probe found, so Terminal.Gui's own detection doesn't decide. Returns the cell size it was given.</summary>
    public static Size Configure(IDriver driver, ImageProtocol protocol, SizeF cellPixels)
    {
        var cell = new Size((int)Math.Round(cellPixels.Width), (int)Math.Round(cellPixels.Height));
        if (protocol == ImageProtocol.Kitty)
        {
            driver.SetKittyGraphicsSupport(new KittyGraphicsSupportResult { IsSupported = true, Resolution = cell });
            return cell;
        }

        if (driver.KittyGraphicsSupport is { IsSupported: true })
            driver.SetKittyGraphicsSupport(new KittyGraphicsSupportResult { IsSupported = false, Resolution = cell });
        driver.SetSixelSupport(new SixelSupportResult { IsSupported = protocol == ImageProtocol.Sixel, Resolution = cell });
        return cell;
    }
}
