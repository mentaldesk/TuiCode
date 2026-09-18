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
        Height = 33;
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
            "Navigation",
            Row("Ctrl+1-9", "Focus editor tab 1-9"),
            Row("Ctrl+G L", "Go to line:column"),
            Row("Ctrl+G P/N", "Previous / next cursor position"),
            Row("Ctrl+F / H", "Find / replace in file"),
            Row("Ctrl+Shift+F/H", "Find / replace globally"),
            "",
            "Files",
            Row("Ctrl+O", "Open file or folder"),
            Row("Ctrl+N", "New file or folder"),
            Row("F2 / Delete", "Rename / delete (in the explorer)"),
            Row("Ctrl+S", "Save active file"),
            Row("Ctrl+W", "Close active tab"),
            "",
            "Editing",
            Row("Alt+Up/Dn", "Move line"),
            Row("Alt+Shift+Up/Dn", "Duplicate line"),
            Row("Ctrl+Alt+Up/Dn", "Add cursor above / below"),
            Row("Alt+Click", "Add / remove cursor (iTerm2: Cmd+Click)"),
            Row("Ctrl+T C", "Toggle column select"),
            Row("Esc", "Back to one cursor"),
            "",
            "Tools",
            Row("Ctrl+Space", "Run command (mnemonic)"),
            Row("Ctrl+E", "Command palette"),
            Row("Ctrl+,", "Settings"),
            Row("F1", "Help (this dialog)"),
            Row("F12", "Diagnostics"),
            Row("Ctrl+Q", "Quit"));
    }
}
