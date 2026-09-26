using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Help;

public sealed class HelpView : Window
{
    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Closed;

    public HelpView()
    {
        Title = "Getting Started";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 58;
        Height = 12;
        CanFocus = true;

        var content = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
            Text = BuildContent(),
        };

        var footer = new Label
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1),
            Text = "Esc · Enter  close",
        };

        Add(content, footer);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();
    }

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.HelpClose, () => Closed?.Invoke(this, EventArgs.Empty));
        _scopeKeybindings.Bind("Esc", CommandIds.HelpClose);
        _scopeKeybindings.Bind("Enter", CommandIds.HelpClose);
    }

    private static string BuildContent()
    {
        static string Row(string key, string desc) => $"  {key,-16}{desc}";

        return string.Join("\n",
            Row("Ctrl+E", "Command palette: what applies here"),
            Row("Ctrl+Space", "Run a command by its mnemonic"),
            Row("Ctrl+O", "Open file or folder"),
            Row("Ctrl+N", "New file or folder"),
            Row("Ctrl+S", "Save"),
            Row("Ctrl+W", "Close tab"),
            Row("Ctrl+F", "Find in file"),
            Row("Ctrl+Q", "Quit"));
    }
}
