namespace TuiCode.Abstractions;

/// <summary>The sidebar's width in columns (#209): the limits the commands, Settings and the layout all obey.</summary>
public static class SidebarSizing
{
    public const int Default = 30;
    public const int Min = 15;

    /// <summary>Columns a single widen/narrow moves.</summary>
    public const int Step = 5;

    /// <summary>Columns left to the editor, unless the terminal is too narrow for that and <see cref="Min"/> both.</summary>
    public const int EditorFloor = 40;

    /// <summary>Upper bound of the Settings spinner, which can't know the terminal it will be applied to.</summary>
    public const int SettingsMax = 80;

    /// <summary>The widest the sidebar may be drawn. Below <see cref="Min"/> + <see cref="EditorFloor"/> the sidebar's floor wins.</summary>
    public static int MaxFor(int terminalWidth) => Math.Max(Min, terminalWidth - EditorFloor);

    public static int Clamp(int width, int terminalWidth) => Math.Clamp(width, Min, MaxFor(terminalWidth));
}
