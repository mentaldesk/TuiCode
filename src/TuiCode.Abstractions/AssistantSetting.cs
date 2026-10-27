namespace TuiCode.Abstractions;

/// <summary>The coding agent the AI assistance tab talks to (#566): a preset's id, or <see cref="Custom"/> with its own command line, and whether its tool calls start shown in full (#618).</summary>
public sealed record AssistantSetting(string Agent, string Command = "", bool ToolCallsInFull = false)
{
    public const string Custom = "custom";

    public static AssistantSetting Default { get; } = new("claude");
}
