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
        Height = 21;
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
        static string Row(string key, string desc) => $"  {key,-13}{desc}";

        return string.Join("\n",
            "Navigation",
            Row("Ctrl+1-9", "Focus editor tab 1-9"),
            Row("Ctrl+0", "Toggle sidebar"),
            Row("Esc", "Return to editor"),
            Row("Ctrl+Esc", "Focus tab strip"),
            "",
            "Files",
            Row("Ctrl+S", "Save active file"),
            Row("Ctrl+W", "Close active tab"),
            "",
            "Tools",
            Row("F1", "Command palette"),
            Row("Ctrl+,", "Settings"),
            Row("Ctrl+/", "Help (this dialog)"),
            Row("Ctrl+Q", "Quit"));
    }
}
