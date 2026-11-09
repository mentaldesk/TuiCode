using System.Text.Json.Nodes;
using MentalDesk.Tui.Layout;
using TuiCode.Abstractions;
using FocusRegion = MentalDesk.Tui.Focus.FocusRegion;
using LibraryScope = MentalDesk.Tui.Commands.CommandScope;

namespace TuiCode.Workbench;

/// <summary>The side panes as <see cref="SidePanes"/> knows them, their widths kept in the settings they've always been in.</summary>
public static class PaneWidths
{
    public const int SidebarDefault = 30;
    public const int AssistantDefault = 50;

    /// <summary>Upper bound of the Settings spinner, which can't know the terminal it will be applied to.</summary>
    public const int SettingsMax = 80;

    public static readonly FocusRegion Sidebar = new("Sidebar", new LibraryScope("Sidebar"));
    public static readonly FocusRegion Assistant = new("AI assistance", new LibraryScope("Assistant"));

    public static JsonObject Read(ISettingsService settings)
    {
        var widths = new JsonObject { [Sidebar.Name] = settings.SidebarWidth };
        if (settings.AssistantWidth is { } assistant) widths[Assistant.Name] = assistant;
        return widths;
    }

    public static void Write(ISettingsService settings, JsonObject widths)
    {
        var sidebar = (int?)widths[Sidebar.Name] ?? SidebarDefault;
        var assistant = (int?)widths[Assistant.Name];
        if (sidebar == settings.SidebarWidth && assistant == settings.AssistantWidth) return;
        settings.SidebarWidth = sidebar;
        settings.AssistantWidth = assistant;
        settings.Save();
    }
}
