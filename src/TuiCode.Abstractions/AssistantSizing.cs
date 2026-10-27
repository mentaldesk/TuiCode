namespace TuiCode.Abstractions;

/// <summary>The AI assistance sidebar's width in columns (#619).</summary>
public static class AssistantSizing
{
    public const int Default = 50;
    public const int Min = 30;

    /// <summary>The width it first opens at: <see cref="Default"/>, or 40% of the terminal if that's less.</summary>
    public static int DefaultFor(int terminalWidth) => Math.Max(Min, Math.Min(Default, terminalWidth * 2 / 5));

    /// <summary>The widest it may be drawn beside a left sidebar <paramref name="left"/> columns wide.</summary>
    public static int MaxFor(int terminalWidth, int left) => Math.Max(Min, terminalWidth - SidebarSizing.EditorFloor - left);

    public static int Clamp(int width, int terminalWidth, int left) => Math.Clamp(width, Min, MaxFor(terminalWidth, left));
}
