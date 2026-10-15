using TuiCode.Abstractions;
using TuiCode.Workbench.Assistant;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

public class AgentPresetsTests
{
    [Fact]
    public void Claude_Code_is_the_default_and_runs_its_ACP_adapter_through_npx()
    {
        var agent = AgentPresets.Resolve(AssistantSetting.Default)!;

        Assert.Equal(("Claude Code", "npx", "npx -y @agentclientprotocol/claude-agent-acp"), (agent.Name, agent.Program, agent.CommandLine));
    }

    [Theory]
    [InlineData("codex", "Codex", "npx -y @agentclientprotocol/codex-acp")]
    [InlineData("gemini", "Gemini CLI", "gemini --acp")]
    [InlineData("copilot", "Copilot CLI", "copilot --acp")]
    [InlineData("goose", "goose", "goose acp")]
    public void Each_preset_resolves_to_its_command(string id, string name, string command)
    {
        var agent = AgentPresets.Resolve(new AssistantSetting(id))!;

        Assert.Equal((name, command), (agent.Name, agent.CommandLine));
    }

    [Fact]
    public void A_custom_command_is_split_into_program_and_arguments_and_named_after_the_program()
    {
        var agent = AgentPresets.Resolve(new AssistantSetting(AssistantSetting.Custom, "/opt/bin/my-agent --acp \"a b\""))!;

        Assert.Equal(("my-agent", "/opt/bin/my-agent"), (agent.Name, agent.Program));
        Assert.Equal(["--acp", "a b"], agent.Arguments);
    }

    [Fact]
    public void A_blank_custom_command_is_no_agent() =>
        Assert.Null(AgentPresets.Resolve(new AssistantSetting(AssistantSetting.Custom, "  ")));

    [Fact]
    public void The_settings_form_says_Claude_Code_needs_Node_when_npx_isnt_on_PATH()
    {
        var form = new AssistantForm(AssistantSetting.Default, _ => false);

        Assert.Equal(
            ["Runs: npx -y @agentclientprotocol/claude-agent-acp", "✗ npx isn't on PATH: Claude Code needs Node.", "Sign in once in a terminal: claude auth login"],
            form.Status());
        Assert.Equal("✗ npx -y @agentclientprotocol/claude-agent-acp", form.Row(0));
    }

    [Fact]
    public void The_settings_form_marks_a_preset_found_on_PATH()
    {
        var form = new AssistantForm(new AssistantSetting("goose"), command => command == "goose");

        Assert.Equal(4, form.Selected);
        Assert.Equal("✓ goose acp", form.Row(4));
        Assert.Contains("✓ goose is on PATH.", form.Status());
    }

    [Fact]
    public void Choosing_custom_saves_the_typed_command()
    {
        var form = new AssistantForm(AssistantSetting.Default, _ => true) { Selected = AgentPresets.All.Count, Command = " my-agent acp " };

        Assert.Equal(new AssistantSetting(AssistantSetting.Custom, "my-agent acp"), form.Setting);
    }
}
