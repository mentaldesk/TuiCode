using Size = System.Drawing.Size;
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
    private readonly ImageView _image;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Size, Color[,]> ScaledImages = new();

    private bool _disposed;

    public IKeybindingService Scope => _scopeKeybindings;

    internal bool IsLoading => _art.Mode == ArtMode.Loading;
    internal bool ShowsAsciiArt => _art.Mode == ArtMode.Ascii;
    internal bool ShowsImage => _art.Mode == ArtMode.Image;

    public event EventHandler? Closed;

    /// <param name="image">Kept by the caller across opens, so it doesn't encode the image again.</param>
    public AboutView(string version, ImageView image)
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

        _image = image;
        _image.X = 1;
        _image.Y = 1;
        _image.Width = Dim.Fill(1);
        _image.Visible = false;

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

        Add(art, _image, details, footer);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.AboutClose, () => Closed?.Invoke(this, EventArgs.Empty));
        _scopeKeybindings.Bind("Esc", CommandIds.AboutClose);
        _scopeKeybindings.Bind("Enter", CommandIds.AboutClose);
    }

    /// <summary>An <see cref="ImageView"/> for <see cref="AboutView"/>: a picture, without pan and zoom.</summary>
    public static ImageView CreateImage()
    {
        var image = new ImageView
        {
            CanFocus = false,
            UseBackgroundRendering = true,
            MaxSixelPaletteColors = 256,
        };
        image.MouseBindings.Clear();
        return image;
    }

    internal void ShowAsciiArt()
    {
        _image.Visible = false;
        _art.Mode = ArtMode.Ascii;
        _art.SetNeedsDraw();
    }

    /// <summary>Shows the artwork through whichever protocol the driver has been given, <paramref name="cellPixels"/> to a cell.</summary>
    internal void ShowImage(Size cellPixels)
    {
        if (_disposed || App is not { } app) return;

        var columns = Art.Max(line => line.Length);
        var (pixels, rows) = AboutImage.Fit(AboutImage.ReadSize(), cellPixels, columns);
        _art.Height = rows;
        _image.Height = rows;
        Height = rows + ChromeHeight;

        if (ScaledImages.TryGetValue(pixels, out var cached))
        {
            Show(cached);
            return;
        }

        ShowLoading();
        Task.Run(() =>
        {
            var image = ScaledImages.GetOrAdd(pixels, size => AboutImage.Cover(AboutImage.Load(), size));
            app.Invoke(() => Show(image));
        });
    }

    private void Show(Color[,] image)
    {
        if (_disposed) return;
        if (!ReferenceEquals(_image.Image, image)) _image.Image = image;
        _image.Visible = true;
        _art.Mode = ArtMode.Image;
        _art.SetNeedsDraw();
    }

    internal void ShowLoading()
    {
        if (_disposed || App is not { } app || _art.Mode == ArtMode.Loading) return;
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
        if (disposing && !_disposed)
        {
            Remove(_image);
            // Some terminals keep a sixel until the cells under it are written again.
            if (_art.Mode == ArtMode.Image && App is { } app) app.ClearScreenNextIteration = true;
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

        protected override bool OnDrawingContent(DrawContext? context)
        {
            SetAttribute(GetAttributeForRole(VisualRole.Normal) with { Foreground = Green });
            switch (Mode)
            {
                case ArtMode.Image:
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
