using System.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using KeyBinding = TuiCode.Abstractions.KeyBinding;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

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

    [Fact]
    public void Saving_a_binding_for_a_scoped_command_writes_it_and_reloads_it_in_that_scope()
    {
        var settings = new DefaultSettingsService(_fs);
        using (var workbench = BuildWorkbench())
        using (var host = BuildHost(workbench, settings))
        {
            var cutFile = new KeyBinding(TestKeys.Chord("Ctrl+K"), CommandIds.CutFile, CommandScope.Explorer);
            host.ApplyEditedBindings([.. _keybindings.Bindings, cutFile]);
            settings.Save();
        }
        var saved = _keybindings.Bindings.ToHashSet();

        var reloaded = new DefaultSettingsService(_fs);
        using var workbench2 = BuildWorkbench();
        using var host2 = BuildHost(workbench2, reloaded);

        var o = Assert.Single(reloaded.KeybindingOverrides);
        Assert.Equal((KeyChord.Canonical(TestKeys.Chord("Ctrl+K")), CommandIds.CutFile), (KeyChord.Canonical(o.Keys), o.Command));
        Assert.Equal(saved, _keybindings.Bindings.ToHashSet());
        Assert.Contains(_keybindings.Bindings, b => Is(b, CommandScope.Explorer, "Ctrl+K", CommandIds.CutFile));
    }

    [Fact]
    public void Removing_an_explorer_default_in_the_picker_keeps_the_Global_binding_on_the_same_key()
    {
        var settings = new InMemorySettingsService();
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, settings);

        host.ApplyEditedBindings(_keybindings.Bindings.Where(b => b.CommandId != CommandIds.CancelCut).ToArray());

        var o = Assert.Single(settings.KeybindingOverrides);
        Assert.Equal("-" + CommandIds.CancelCut, o.Command);
        Assert.DoesNotContain(_keybindings.Bindings, b => b.CommandId == CommandIds.CancelCut);
        Assert.Contains(_keybindings.Bindings, b => Is(b, CommandScope.Global, "Esc", CommandIds.FocusEditorBody));
    }

    [Fact]
    public async Task Binding_a_Global_command_to_an_explorer_key_warns_in_a_dialog_that_fits_80_columns()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, new InMemorySettingsService());
        KeybindingsPickerView? picker = null;
        SettingsView? settings = null;
        (string Title, View? Host, int Width, string Message)? dialog = null;

        await HostSteps.Run(host,
            () => host.App.InjectKey(new Key(',').WithCtrl),
            () => { host.App.InjectKey(Key.CursorDown); host.App.InjectKey(Key.CursorDown); },
            () => host.App.InjectKey(Key.CursorRight),
            () =>
            {
                picker = workbench.SubViewsDeep().OfType<KeybindingsPickerView>().Single();
                settings = (SettingsView)picker.SuperView!;
                host.App.InjectKey(Key.Enter); // "About TuiCode", Global and unbound
            },
            () => host.App.InjectKey(Key.X.WithCtrl),
            () => host.App.InjectKey(Key.Enter),
            () =>
            {
                if (workbench.SubViewsDeep().OfType<Dialog>().SingleOrDefault() is not { } d) return false;
                dialog = (d.Title, d.SuperView, d.Frame.Width, d.SubViews.OfType<Label>().Single().Text);
                return true;
            },
            () => host.App.InjectKey(Key.Tab.WithShift),
            () => host.App.InjectKey(Key.Enter),
            () => host.App.InjectKey(Key.Esc));

        // The screen size isn't settable on Windows CI, so check against the settings view's 78 columns inside its border at 80.
        const int innerWidthAt80 = 78;
        var (title, dialogHost, width, message) = dialog!.Value;
        Assert.Equal("Shortcut in use", title);
        Assert.Same(settings, dialogHost);
        Assert.InRange(width, message.Split('\n').Max(l => l.Length) + 3, innerWidthAt80);
        Assert.Equal(KeybindingRowsTests.WidthAt80, innerWidthAt80 - (settings!.Viewport.Width - picker!.Viewport.Width));
        Assert.Contains(picker.CurrentBindings, b => Is(b, CommandScope.Global, "Ctrl+X", CommandIds.ShowAbout));
        Assert.Contains(picker.CurrentBindings, b => Is(b, CommandScope.Explorer, "Ctrl+X", CommandIds.CutFile));
    }

    [Fact]
    public void The_shortcuts_header_follows_the_pane_width()
    {
        var picker = new KeybindingsPickerView(_commands, _keybindings, new InputScopeStack());
        var header = picker.SubViews.OfType<Label>().First();

        picker.Layout(new Size(KeybindingRowsTests.WidthAt80, 20));
        Assert.Equal(KeybindingRows.Header(KeybindingRowsTests.WidthAt80), header.Text);

        picker.Layout(new Size(171, 20));
        Assert.Equal(KeybindingRows.Header(171), header.Text);
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
