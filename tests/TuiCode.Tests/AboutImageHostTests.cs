using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Workbench;
using TuiCode.Workbench.About;
using TuiCode.Workbench.Diagnostics;
using TuiCode.Workbench.Parts;
using TuiCode.Explorer;
using TuiCode.Workbench.Services;
using Size = System.Drawing.Size;
using SizeF = System.Drawing.SizeF;

namespace TuiCode.Tests;

public class ImageProtocolsTests
{
    [Theory]
    [InlineData(true, false, false, "Kitty")]
    [InlineData(true, false, null, "Kitty")]
    [InlineData(true, false, true, "Kitty")]
    [InlineData(false, false, true, "Sixel")]
    [InlineData(false, false, false, "None")]
    [InlineData(true, true, true, "Sixel")]
    [InlineData(true, true, false, "None")]
    [InlineData(false, false, null, null)]
    [InlineData(true, true, null, null)]
    public void Choose_prefers_kitty_graphics_outside_tmux_then_sixel_then_the_ascii_art(
        bool kitty, bool inTmux, bool? sixel, string? expected) =>
        Assert.Equal(expected, ImageProtocols.Choose(kitty, inTmux, sixel)?.ToString());

    [Theory]
    [InlineData("Kitty", "kitty graphics")]
    [InlineData("Sixel", "sixel")]
    [InlineData("None", "none (ASCII art)")]
    public void Describe_names_how_About_draws_the_artwork(string protocol, string expected) =>
        Assert.Equal(expected, ImageProtocols.Describe(Enum.Parse<ImageProtocol>(protocol)));

    [Fact]
    public void Diagnostics_shows_the_image_protocol_on_its_own_row()
    {
        using var view = new DiagnosticsView("ansi", "No", imageProtocol: "kitty graphics");
        view.Layout(new Size(200, 100));

        var labels = view.SubViews.OfType<Label>().ToList();
        var heading = labels.Single(label => label.Text == "Image protocol");
        var value = labels.Single(label => label.Text == "kitty graphics");
        var cellSize = labels.Single(label => label.Text == "Cell size");

        Assert.Equal(heading.Frame.Y, value.Frame.Y);
        Assert.Equal(cellSize.Frame.Bottom, heading.Frame.Y);
    }
}

// Boots a TG Application via WorkbenchHost — process-global TG state, must be serialised (issue #77).
public class AboutImageHostTests : StaticConfigurationTest
{
    [Fact]
    public async Task Configure_gives_the_driver_our_sixel_result_and_cell_size_and_turns_kitty_off()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        using var host = BuildHost(workbench, commands, new FakeEnvironment());
        SixelSupportResult? sixel = null;
        KittyGraphicsSupportResult? kitty = null;
        var useKitty = true;
        var cell = Size.Empty;

        await HostSteps.Run(host, () =>
        {
            var driver = host.App.Driver!;
            driver.SetKittyGraphicsSupport(new KittyGraphicsSupportResult { IsSupported = true });
            cell = ImageProtocols.Configure(driver, ImageProtocol.Sixel, new SizeF(16.4f, 35));
            (sixel, kitty, useKitty) = (driver.SixelSupport, driver.KittyGraphicsSupport, driver.GetOutput().UseKittyGraphics);
        });

        Assert.Equal(new Size(16, 35), cell);
        Assert.True(sixel?.IsSupported);
        Assert.Equal(new Size(16, 35), sixel?.Resolution);
        Assert.False(kitty?.IsSupported);
        Assert.False(useKitty);
    }

    [Fact]
    public async Task Configure_gives_the_driver_our_cell_size_for_kitty_graphics()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        using var host = BuildHost(workbench, commands, new FakeEnvironment());
        KittyGraphicsSupportResult? kitty = null;
        var useKitty = false;

        await HostSteps.Run(host, () =>
        {
            var driver = host.App.Driver!;
            ImageProtocols.Configure(driver, ImageProtocol.Kitty, new SizeF(9, 19.6f));
            (kitty, useKitty) = (driver.KittyGraphicsSupport, driver.GetOutput().UseKittyGraphics);
        });

        Assert.True(kitty?.IsSupported);
        Assert.Equal(new Size(9, 20), kitty?.Resolution);
        Assert.True(useKitty);
    }

    [Theory]
    [InlineData(null, "kitty graphics")]
    [InlineData("/tmp/tmux-501/default,1,0", null)]
    public async Task Diagnostics_says_kitty_graphics_only_outside_tmux(string? tmux, string? expected)
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        using var host = BuildHost(workbench, commands, new FakeEnvironment().Set("TMUX", tmux));
        string? shown = null;

        await HostSteps.Run(host,
            () =>
            {
                host.App.Driver!.SetKittyGraphicsSupport(new KittyGraphicsSupportResult { IsSupported = true });
                commands.TryExecute(CommandIds.ShowDiagnostics);
            },
            () => { shown = workbench.SubViews.OfType<DiagnosticsView>().Single().ImageProtocolText; },
            () => host.App.InjectKey(Key.Esc));

        if (expected is null) Assert.NotEqual("kitty graphics", shown);
        else Assert.Equal(expected, shown);
    }

    [Fact]
    public async Task About_shows_the_image_through_kitty_graphics_and_straight_away_when_opened_again()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        using var host = BuildHost(workbench, commands, new FakeEnvironment());
        AboutView About() => workbench.SubViews.OfType<AboutView>().Single();
        var cell = new Size(10, 20);
        bool raster = false, again = false;

        await HostSteps.Run(host,
            () =>
            {
                ImageProtocols.Configure(host.App.Driver!, ImageProtocol.Kitty, cell);
                commands.TryExecute(CommandIds.ShowAbout);
            },
            () => About().ShowImage(cell),
            () => About().ShowsImage,
            () => raster = About().SubViews.OfType<ImageView>().Single() is { Visible: true, IsUsingRasterGraphics: true },
            () => host.App.InjectKey(Key.Esc),
            () => commands.TryExecute(CommandIds.ShowAbout),
            () => { About().ShowImage(cell); again = About().ShowsImage; },
            () => host.App.InjectKey(Key.Esc));

        Assert.True(raster, "About didn't draw the artwork as a raster image");
        Assert.True(again, "About didn't show the image straight away on a second open");
    }

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench, CommandService commands, IEnvironment environment) =>
        new(workbench, commands, new KeybindingService(commands), new InputScopeStack(), new InMemorySettingsService(),
            environment: environment, driverName: DriverRegistry.Names.ANSI);

    private static Workbench.Workbench BuildWorkbench() =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
}
