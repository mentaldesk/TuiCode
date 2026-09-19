using TuiCode.Abstractions;
using KeyBinding = TuiCode.Abstractions.KeyBinding;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// User overrides land in their command's scope (#141). Boots a TG Application — serialised (#77).
public class ScopedKeybindingsHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();
    private readonly KeybindingService _keybindings;

    public ScopedKeybindingsHostTests()
    {
        _keybindings = new KeybindingService(_commands);
    }

    [Fact]
    public void A_user_binding_for_CutFile_lands_in_Explorer()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, Override("Ctrl+K", CommandIds.CutFile));

        Assert.Contains(_keybindings.Bindings, b => Is(b, CommandScope.Explorer, "Ctrl+K", CommandIds.CutFile));
        Assert.DoesNotContain(_keybindings.Bindings, b => b.Scope == CommandScope.Global && b.CommandId == CommandIds.CutFile);
    }

    [Fact]
    public void Removing_an_explorer_default_leaves_the_Global_binding_on_the_same_key()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, Override("Esc", "-" + CommandIds.CancelCut));

        Assert.DoesNotContain(_keybindings.Bindings, b => b.CommandId == CommandIds.CancelCut);
        Assert.Contains(_keybindings.Bindings, b => Is(b, CommandScope.Global, "Esc", CommandIds.FocusEditorBody));
    }

    [Fact]
    public void An_existing_overrides_file_binds_the_explorer_keys_in_Explorer()
    {
        var path = _fs.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tui", "TuiCode.keybindings.json");
        _fs.AddFile(path, new MockFileData(
            $$"""
            [
              { "Keys": [{{Code("Ctrl+X")}}], "Label": "Ctrl+X", "Command": "{{CommandIds.CutFile}}" },
              { "Keys": [{{Code("Ctrl+V")}}], "Label": "Ctrl+V", "Command": "{{CommandIds.PasteFile}}" },
              { "Keys": [{{Code("Delete")}}], "Label": "Delete", "Command": "{{CommandIds.DeleteFile}}" }
            ]
            """));
        var settings = new DefaultSettingsService(_fs);
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, settings.KeybindingOverrides.ToArray());

        Assert.Equal(3, settings.KeybindingOverrides.Count);
        Assert.Contains(_keybindings.Bindings, b => Is(b, CommandScope.Explorer, "Ctrl+X", CommandIds.CutFile));
        Assert.Contains(_keybindings.Bindings, b => Is(b, CommandScope.Explorer, "Ctrl+V", CommandIds.PasteFile));
        Assert.Contains(_keybindings.Bindings, b => Is(b, CommandScope.Explorer, "Delete", CommandIds.DeleteFile));
        Assert.DoesNotContain(_keybindings.Bindings, b => b.Scope == CommandScope.Global
            && b.CommandId is CommandIds.CutFile or CommandIds.PasteFile or CommandIds.DeleteFile);
    }

    [Fact]
    public async Task A_saved_Ctrl_X_for_CutFile_no_longer_cuts_the_file_from_the_editor()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("abc"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, Override("Ctrl+X", CommandIds.CutFile));

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.Editor.Group.ActiveTab!.ContentHasFocus,
            () => host.App.InjectKey(Key.X.WithCtrl),
            () => { });

        Assert.Null(workbench.Sidebar.Explorer.PendingCut);
    }

    [Fact]
    public void Saving_the_unchanged_bindings_writes_no_overrides()
    {
        var settings = new InMemorySettingsService();
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, settings);

        host.ApplyEditedBindings(_keybindings.Bindings.ToArray());

        Assert.Empty(settings.KeybindingOverrides);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, params KeybindingOverride[] overrides)
    {
        var settings = new InMemorySettingsService();
        settings.SetKeybindingOverrides(overrides);
        return BuildHost(workbench, settings);
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, ISettingsService settings) =>
        new(workbench, _commands, _keybindings, new InputScopeStack(), settings, driverName: DriverRegistry.Names.ANSI);

    private static KeybindingOverride Override(string keys, string command) => new(TestKeys.Chord(keys), command);

    private static bool Is(KeyBinding binding, CommandScope scope, string keys, string command) =>
        binding.Scope == scope && binding.CommandId == command && binding.CanonicalId == KeyChord.Canonical(TestKeys.Chord(keys));

    private static uint Code(string key) => (uint)TestKeys.Chord(key)[0].KeyCode;
}
