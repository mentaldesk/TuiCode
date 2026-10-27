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

    /// <summary>
    /// The widest the sidebar may be drawn, leaving <paramref name="beside"/> columns to the AI assistance sidebar.
    /// Below <see cref="Min"/> + <see cref="EditorFloor"/> the sidebar's floor wins.
    /// </summary>
    public static int MaxFor(int terminalWidth, int beside = 0) => Math.Max(Min, terminalWidth - EditorFloor - beside);

    public static int Clamp(int width, int terminalWidth, int beside = 0) => Math.Clamp(width, Min, MaxFor(terminalWidth, beside));

    /// <summary>
    /// Both sidebars' drawn widths, 0 for a hidden one (#619): the editor keeps <see cref="EditorFloor"/>, the AI
    /// sidebar narrowing first, down to <see cref="AssistantSizing.Min"/>, and then the left one.
    /// </summary>
    public static (int Left, int Right) Fit(int terminalWidth, int? left, int? right)
    {
        var drawnLeft = left is { } l ? Clamp(l, terminalWidth, right is null ? 0 : AssistantSizing.Min) : 0;
        var drawnRight = right is { } r ? AssistantSizing.Clamp(r, terminalWidth, drawnLeft) : 0;
        return (drawnLeft, drawnRight);
    }
}
