using System.Text.Json.Nodes;
using TuiCode.Workbench;

namespace TuiCode.Tests;

public class PaneWidthsTests
{
    private readonly InMemorySettingsService _settings = new();

    [Fact]
    public void Read_gives_the_library_the_widths_saved_by_the_current_release()
    {
        _settings.SidebarWidth = 45;
        _settings.AssistantWidth = 62;

        var widths = PaneWidths.Read(_settings);

        Assert.Equal(45, (int)widths[PaneWidths.Sidebar.Name]!);
        Assert.Equal(62, (int)widths[PaneWidths.Assistant.Name]!);
    }

    [Fact]
    public void Read_leaves_out_an_AI_assistance_width_nobody_chose()
    {
        Assert.False(PaneWidths.Read(_settings).ContainsKey(PaneWidths.Assistant.Name));
    }

    [Fact]
    public void A_width_written_reads_back_and_is_saved()
    {
        PaneWidths.Write(_settings, new JsonObject { [PaneWidths.Sidebar.Name] = 20, [PaneWidths.Assistant.Name] = 17 });

        Assert.Equal((20, 17), (_settings.SidebarWidth, _settings.AssistantWidth));
        Assert.Equal(1, _settings.SaveCount);
        Assert.Equal(20, (int)PaneWidths.Read(_settings)[PaneWidths.Sidebar.Name]!);
    }

    [Fact]
    public void A_width_the_library_forgot_goes_back_to_the_default()
    {
        _settings.SidebarWidth = 45;
        _settings.AssistantWidth = 62;

        PaneWidths.Write(_settings, []);

        Assert.Equal(PaneWidths.SidebarDefault, _settings.SidebarWidth);
        Assert.Null(_settings.AssistantWidth);
    }

    [Fact]
    public void Writing_the_widths_already_saved_saves_nothing()
    {
        _settings.SidebarWidth = 45;

        PaneWidths.Write(_settings, PaneWidths.Read(_settings));

        Assert.Equal(0, _settings.SaveCount);
    }
}
