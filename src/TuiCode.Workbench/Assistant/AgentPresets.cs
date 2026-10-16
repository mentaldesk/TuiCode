using TuiCode.Abstractions;
using TuiCode.Workbench.Settings;

namespace TuiCode.Workbench.Assistant;

/// <summary>An agent the AI assistance tab can run over ACP, and how to sign in to it.</summary>
/// <param name="Needs">What the program comes with, where that isn't the agent itself (Node, for <c>npx</c>).</param>
public sealed record AgentSpec(string Id, string Name, string Program, IReadOnlyList<string> Arguments, string? SignIn, string? Needs = null)
{
    public string CommandLine => ArgumentLine.Join([Program, .. Arguments]);
}

/// <summary>The agents Settings › AI Assistance offers (#566), from the ACP registry's launch commands.</summary>
public static class AgentPresets
{
    public static readonly AgentSpec ClaudeCode = new(
        "claude", "Claude Code", "npx", ["-y", "@agentclientprotocol/claude-agent-acp"], "claude auth login", "Node");

    public static readonly IReadOnlyList<AgentSpec> All =
    [
        ClaudeCode,
        new("codex", "Codex", "npx", ["-y", "@agentclientprotocol/codex-acp"], "codex login", "Node"),
        new("gemini", "Gemini CLI", "gemini", ["--acp"], "gemini"),
        new("copilot", "Copilot CLI", "copilot", ["--acp"], "copilot login"),
        new("goose", "goose", "goose", ["acp"], "goose configure"),
    ];

    /// <summary>The agent <paramref name="setting"/> names; null for a custom command that's blank.</summary>
    public static AgentSpec? Resolve(AssistantSetting setting)
    {
        if (setting.Agent != AssistantSetting.Custom)
            return All.FirstOrDefault(a => a.Id == setting.Agent) ?? ClaudeCode;
        var words = ArgumentLine.Split(setting.Command);
        if (words.Count == 0) return null;
        var name = Path.GetFileNameWithoutExtension(words[0]);
        return new AgentSpec(AssistantSetting.Custom, name, words[0], [.. words.Skip(1)], null);
    }
}
