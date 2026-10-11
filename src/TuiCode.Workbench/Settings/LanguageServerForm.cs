using TuiCode.Abstractions;
using TuiCode.Workbench.Languages;

namespace TuiCode.Workbench.Settings;

/// <summary>What the language server dialog shows and saves (#456): None, the known servers, or a custom command. TG-free for testing.</summary>
internal sealed class LanguageServerForm
{
    private readonly string _languageId;
    private readonly IReadOnlyList<KnownLanguageServer> _known;
    private readonly Func<string, bool> _isInstalled;

    public LanguageServerForm(string languageId, LanguageServerSetting current, Func<string, bool> isInstalled)
    {
        _languageId = languageId;
        _known = KnownLanguageServers.For(languageId);
        _isInstalled = isInstalled;
        Options = ["None", .. _known.Select(k => k.Name), "Custom"];
        if (current.IsNone) return;
        Command = current.Command;
        Arguments = ArgumentLine.Join(current.Arguments);
        Selected = MatchingOption();
    }

    public IReadOnlyList<string> Options { get; }

    public int Selected { get; private set; }

    public int Custom => Options.Count - 1;

    public string Command { get; private set; } = "";

    public string Arguments { get; private set; } = "";

    public LanguageServerSetting Setting =>
        Selected == 0 || Command.Trim().Length == 0
            ? LanguageServerSetting.None
            : new LanguageServerSetting(Command.Trim(), ArgumentLine.Split(Arguments));

    /// <summary>Picks an option; a known server fills in its command and arguments, None clears them.</summary>
    public void Select(int option)
    {
        Selected = Math.Clamp(option, 0, Custom);
        if (Selected == 0)
        {
            Command = "";
            Arguments = "";
        }
        else if (Selected < Custom)
        {
            var known = _known[Selected - 1];
            Command = known.Command;
            Arguments = ArgumentLine.Join(known.Arguments);
        }
    }

    /// <summary>Typing in the fields: picks the known server they now describe, or else Custom.</summary>
    public void Edit(string command, string arguments)
    {
        if (command == Command && arguments == Arguments) return;
        Command = command;
        Arguments = arguments;
        Selected = MatchingOption();
    }

    private int MatchingOption()
    {
        var setting = new LanguageServerSetting(Command.Trim(), ArgumentLine.Split(Arguments));
        var index = _known.ToList().FindIndex(k => k.Setting == setting);
        return index < 0 ? Custom : index + 1;
    }

    /// <summary>Whether the command is on <c>PATH</c>, and how to install a known server that isn't.</summary>
    public IReadOnlyList<string> Status()
    {
        if (Selected == 0) return [];
        var command = Command.Trim();
        if (command.Length == 0) return ["Type the command that starts the server."];
        if (_isInstalled(command)) return [$"✓ {command} is on PATH."];
        return KnownLanguageServers.WithCommand(_languageId, command) is { } known
            ? [$"✗ {command} isn't on PATH.", $"  Install: {known.Install}"]
            : [$"✗ {command} isn't on PATH."];
    }
}
