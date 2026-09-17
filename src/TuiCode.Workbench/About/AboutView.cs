using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
using SizeF = System.Drawing.SizeF;
using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.About;

public sealed class AboutView : Window
{
    internal const string RepositoryUrl = "https://github.com/mentaldesk/TuiCode";

    internal static readonly string[] Art =
    [
        "                 _.--._",
        "               ,'  o   `==-     ████████╗ ██╗   ██╗ ██╗",
        "              /   ,--.          ╚══██╔══╝ ██║   ██║ ██║",
        "             /   ( @@ )            ██║    ██║   ██║ ██║",
        "            /  ,' `--'             ██║    ██║   ██║ ██║",
        "          ,'  /  / |               ██║    ╚██████╔╝ ██║",
        "        ,'  ,'  /  |               ╚═╝     ╚═════╝  ╚═╝",
        "      ,'  ,'  ,'  /",
        "    ,'  ,'  ,'  ,'                 c o d e : : e d i t o r",
        "  ,'  ,'__,'__,'",
        " /__,'   _||_",
        "═════════╪══╪══════════",
    ];

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;
    private const int ChromeHeight = 8;

    private readonly AboutArt _art;
    private IApplication? _app;
    private SixelToRender? _sixel;
    private bool _disposed;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Closed;

    public AboutView(string version)
    {
        Title = "About";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = Art.Max(line => line.Length) + 4;
        Height = Art.Length + ChromeHeight;
        CanFocus = true;

        var art = _art = new AboutArt
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Art.Length,
        };

        var details = new Label
        {
            X = Pos.Center(),
            Y = Pos.Bottom(art) + 1,
            Text = $"Version {version}\n{RepositoryUrl}",
            TextAlignment = Alignment.Center,
        };

        var footer = new Label
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1),
            Text = "Esc · Enter  close",
        };

        Add(art, details, footer);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.AboutClose, () => Closed?.Invoke(this, EventArgs.Empty));
        _scopeKeybindings.Bind("Esc", CommandIds.AboutClose);
        _scopeKeybindings.Bind("Enter", CommandIds.AboutClose);
    }

    /// <summary>Swaps the ASCII art for the sixel artwork once it's encoded in the background.</summary>
    public void ShowImage(SixelSupportResult support, SizeF cellPixels)
    {
        if (!support.IsSupported || App is not { Driver: { } driver } app) return;
        _app = app;

        var columns = Art.Max(line => line.Length);
        Task.Run(() =>
        {
            var source = AboutImage.Load();
            var (pixels, rows) = AboutImage.Fit(new Size(source.GetLength(0), source.GetLength(1)), cellPixels, columns);
            var encoder = new SixelEncoder();
            encoder.Quantizer.MaxColors = Math.Min(encoder.Quantizer.MaxColors, support.MaxPaletteColors);
            var data = encoder.EncodeSixel(AboutImage.Cover(source, pixels));

            app.Invoke(() =>
            {
                if (_disposed) return;
                _sixel = new SixelToRender { Id = "about", SixelData = data };
                _art.Sixel = _sixel;
                _art.Height = rows;
                Height = rows + ChromeHeight;
                driver.GetOutput().GetSixels().Enqueue(_sixel);
                SetNeedsLayout();
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed && _sixel is not null && _app?.Driver is { } driver)
        {
            var sixels = driver.GetOutput().GetSixels();
            var others = sixels.Where(s => !ReferenceEquals(s, _sixel)).ToList();
            sixels.Clear();
            foreach (var other in others) sixels.Enqueue(other);
            // TG only rewrites cells whose contents changed, which would leave the image on screen.
            _app.ClearScreenNextIteration = true;
        }
        _disposed = true;
        base.Dispose(disposing);
    }

    private sealed class AboutArt : View
    {
        private static readonly Color Green = new(0x2E, 0xA0, 0x43);

        public SixelToRender? Sixel { get; set; }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            if (Sixel is not null)
            {
                Sixel.ScreenPosition = ViewportToScreen(Point.Empty);
                return true;
            }

            SetAttribute(GetAttributeForRole(VisualRole.Normal) with { Foreground = Green });
            for (var row = 0; row < Art.Length && row < Viewport.Height; row++)
            {
                Move(0, row);
                AddStr(Art[row]);
            }
            return true;
        }
    }
}
