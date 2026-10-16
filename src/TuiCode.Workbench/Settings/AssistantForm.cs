using TuiCode.Abstractions;
using TuiCode.Workbench.Assistant;

namespace TuiCode.Workbench.Settings;

/// <summary>What Settings › AI Assistance shows and saves (#566): an agent preset, or a custom command. TG-free for testing.</summary>
internal sealed class AssistantForm
{
    private readonly Func<string, bool> _isInstalled;

    public AssistantForm(AssistantSetting current, Func<string, bool> isInstalled)
    {
        _isInstalled = isInstalled;
        Options = [.. AgentPresets.All.Select(a => a.Name), "Custom"];
        Selected = current.Agent == AssistantSetting.Custom
            ? Custom
            : Math.Max(AgentPresets.All.ToList().FindIndex(a => a.Id == current.Agent), 0);
        Command = current.Command;
    }

    public IReadOnlyList<string> Options { get; }

    public int Selected { get; set; }

    public int Custom => Options.Count - 1;

    /// <summary>The custom command line.</summary>
    public string Command { get; set; }

    public AssistantSetting Setting => Selected == Custom
        ? new AssistantSetting(AssistantSetting.Custom, Command.Trim())
        : new AssistantSetting(AgentPresets.All[Selected].Id);

    /// <summary>A preset's row beside its name: whether its program is on <c>PATH</c>, and the command it runs.</summary>
    public string Row(int option)
    {
        var agent = AgentPresets.All[option];
        return $"{(_isInstalled(agent.Program) ? "✓" : "✗")} {agent.CommandLine}";
    }

    /// <summary>What the chosen agent runs, whether it's there, and how to sign in to it.</summary>
    public IReadOnlyList<string> Status()
    {
        if (AgentPresets.Resolve(Setting) is not { } agent) return ["Type the command that starts an ACP agent."];
        var lines = new List<string> { $"Runs: {agent.CommandLine}" };
        if (_isInstalled(agent.Program)) lines.Add($"✓ {agent.Program} is on PATH.");
        else if (agent.Needs is { } needs) lines.Add($"✗ {agent.Program} isn't on PATH: {agent.Name} needs {needs}.");
        else lines.Add($"✗ {agent.Program} isn't on PATH.");
        if (agent.SignIn is { } signIn) lines.Add($"Sign in once in a terminal: {signIn}");
        return lines;
    }
}
