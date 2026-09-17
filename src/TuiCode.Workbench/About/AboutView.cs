using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
using Terminal.Gui.Drivers;
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
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Size, string> EncodedImages = new();

    private IApplication? _app;
    private SixelToRender? _sixel;
    private bool _disposed;

    public IKeybindingService Scope => _scopeKeybindings;

    internal bool IsLoading => _art.Mode == ArtMode.Loading;
    internal bool ShowsAsciiArt => _art.Mode == ArtMode.Ascii;

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

    /// <summary>
    /// Shows the artwork as a sixel image if <paramref name="support"/> allows, else the ASCII art. Null means
    /// detection is still running. Call once the view is added.
    /// </summary>
    internal void Present(SixelSupport? support)
    {
        if (_disposed || App is not { Driver: { } driver } app) return;

        if (support is null)
        {
            StartLoading(app);
            return;
        }

        if (!support.IsSupported)
        {
            _art.Mode = ArtMode.Ascii;
            _art.SetNeedsDraw();
            return;
        }

        var columns = Art.Max(line => line.Length);
        var (pixels, rows) = AboutImage.Fit(AboutImage.ReadSize(), support.CellPixels, columns);
        _art.Height = rows;
        Height = rows + ChromeHeight;

        if (EncodedImages.TryGetValue(pixels, out var cached))
        {
            ShowSixel(app, driver, cached);
            return;
        }

        StartLoading(app);
        Task.Run(() =>
        {
            var data = EncodedImages.GetOrAdd(pixels, size => new SixelEncoder().EncodeSixel(AboutImage.Cover(AboutImage.Load(), size)));
            app.Invoke(() => ShowSixel(app, driver, data));
        });
    }

    private void ShowSixel(IApplication app, IDriver driver, string data)
    {
        if (_disposed) return;
        _app = app;
        _sixel = new SixelToRender { Id = "about", SixelData = data };
        _art.Sixel = _sixel;
        _art.Mode = ArtMode.Image;
        driver.GetOutput().GetSixels().Enqueue(_sixel);
        _art.SetNeedsDraw();
    }

    private void StartLoading(IApplication app)
    {
        if (_art.Mode == ArtMode.Loading) return;
        _art.Mode = ArtMode.Loading;
        _art.SetNeedsDraw();
        app.AddTimeout(TimeSpan.FromMilliseconds(80), () =>
        {
            if (_disposed || _art.Mode != ArtMode.Loading) return false;
            _art.SpinnerFrame++;
            _art.SetNeedsDraw();
            return true;
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

    private enum ArtMode { Ascii, Loading, Image }

    private sealed class AboutArt : View
    {
        private static readonly Color Green = new(0x2E, 0xA0, 0x43);
        private const string Spinner = "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏";

        public ArtMode Mode { get; set; }
        public int SpinnerFrame { get; set; }
        public SixelToRender? Sixel { get; set; }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            SetAttribute(GetAttributeForRole(VisualRole.Normal) with { Foreground = Green });
            switch (Mode)
            {
                case ArtMode.Image:
                    Sixel!.ScreenPosition = ViewportToScreen(Point.Empty);
                    break;
                case ArtMode.Loading:
                    Move(Viewport.Width / 2, Viewport.Height / 2);
                    AddStr(Spinner[SpinnerFrame % Spinner.Length].ToString());
                    break;
                default:
                    for (var row = 0; row < Art.Length && row < Viewport.Height; row++)
                    {
                        Move(0, row);
                        AddStr(Art[row]);
                    }
                    break;
            }
            return true;
        }
    }
}
