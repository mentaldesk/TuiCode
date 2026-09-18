using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Icons;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// Boots a TG Application — serialised (#77).
public class FileIconsSettingsTests : StaticConfigurationTest
{
    private readonly FileIcons _icons = new(() => new FontDetection(true, "test"));
    private readonly InMemorySettingsService _settings = new();

    [Fact]
    public async Task Choosing_a_style_applies_it_live_and_Save_persists_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, OpenFileIconsPicker(host, workbench),
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorDown),
            () => _icons.Setting == FileIconStyle.Off,
            () => host.App.InjectKey(Key.Enter.WithCtrl));

        Assert.Equal(FileIconStyle.Off, _settings.FileIcons);
        Assert.Equal(1, _settings.SaveCount);
    }

    [Fact]
    public async Task Cancelling_restores_the_original_style()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host, OpenFileIconsPicker(host, workbench),
            () => host.App.InjectKey(Key.CursorDown),
            () => _icons.Setting == FileIconStyle.NerdFont,
            () => host.App.InjectKey(Key.Esc));

        Assert.Empty(workbench.SubViews.OfType<SettingsView>());
        Assert.Equal(FileIconStyle.Auto, _icons.Setting);
        Assert.Equal(0, _settings.SaveCount);
    }

    // Opens Settings and moves from the categories into the File Icons panel, which lists Auto first.
    private Func<bool> OpenFileIconsPicker(WorkbenchHost host, Workbench.Workbench workbench)
    {
        var step = 0;
        return () =>
        {
            switch (step++)
            {
                case 0: host.App.InjectKey(TestKeys.Chord("Ctrl+,")[0]); return false;
                case 1: host.App.InjectKey(Key.CursorDown); return false;
                case 2: host.App.InjectKey(Key.CursorRight); return false;
                default:
                    return workbench.SubViews.OfType<SettingsView>().Single()
                        .SubViews.OfType<FileIconsPickerView>().Single() is { Visible: true } picker
                        && picker.SubViews.Any(v => v.HasFocus);
            }
        };
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(), _settings,
            driverName: DriverRegistry.Names.ANSI, icons: _icons);
    }

    private Workbench.Workbench BuildWorkbench() =>
        new(new SidebarPart(new FileExplorerView(_icons)), new EditorPart(), new StatusBarPart());
}
