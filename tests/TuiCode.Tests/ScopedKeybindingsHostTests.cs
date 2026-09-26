using System.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using KeyBinding = TuiCode.Abstractions.KeyBinding;
using TuiCode.Editor;
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

    // The ten document-changing commands are Editor-scoped (#283), so their keys don't reach the tab you
    // can see but aren't in. Seven chords cover them: the three occurrence commands have no default keys.
    private static readonly (string Name, Key[] Chord)[] EditingChords =
    [
        ("Alt+CursorUp", [Key.CursorUp.WithAlt]),
        ("Alt+CursorDown", [Key.CursorDown.WithAlt]),
        ("Alt+Shift+CursorUp", [Key.CursorUp.WithAlt.WithShift]),
        ("Alt+Shift+CursorDown", [Key.CursorDown.WithAlt.WithShift]),
        ("Ctrl+Alt+CursorUp", [Key.CursorUp.WithCtrl.WithAlt]),
        ("Ctrl+Alt+CursorDown", [Key.CursorDown.WithCtrl.WithAlt]),
        ("Ctrl+T C", [Key.T.WithCtrl, Key.C]),
    ];

    private const string ThreeLines = "one\ntwo\nthree";

    [Fact]
    public async Task The_editing_chords_leave_the_file_and_the_focus_alone_from_the_explorer()
    {
        var seen = await PressTheEditingChordsAway(CommandIds.FocusSidebar, "Explorer");

        Assert.Equal(EditingChords.Select(c => (c.Name, ThreeLines, 1, false, false, "Explorer")), seen);
    }

    [Fact]
    public async Task The_editing_chords_leave_the_file_and_the_focus_alone_from_the_find_inputs()
    {
        var seen = await PressTheEditingChordsAway(CommandIds.FindGlobally, "Find");

        Assert.Equal(EditingChords.Select(c => (c.Name, ThreeLines, 1, false, false, "Find")), seen);
    }

    // A diff tab's ActiveTab is null, so the edits were already no-ops there; Ctrl+T C was not.
    [Fact]
    public async Task The_editing_chords_leave_a_diff_and_its_source_alone_and_the_Alt_arrows_still_step_changes()
    {
        _fs.AddFile("/work/a.txt", new MockFileData(ThreeLines));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var group = workbench.Editor.Group;
        var seen = new List<(string Chord, string Lines, int Change, bool ColumnSelect, string Focus)>();

        var steps = new List<Delegate>
        {
            (Action)(() =>
            {
                workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
                group.ActiveTab!.Content = "one\nTWO\nTHREE";
            }),
            (Action)(() => _commands.TryExecute(CommandIds.CompareToSaved)),
            (Func<bool>)(() => workbench.StatusBar.DisplayedFocus == "Diff"),
        };
        steps.AddRange(PressEachChord(host, name => seen.Add((name, string.Join('\n', group.DiffTabs[0].Source!.Lines),
            group.DiffTabs[0].CurrentChange, group.ColumnSelect, workbench.StatusBar.DisplayedFocus))));

        await HostSteps.Run(host, [.. steps]);

        // The diff opens above its first change: Alt+CursorUp holds at 0, Alt+CursorDown steps to 1.
        Assert.Equal(
            [("Alt+CursorUp", "one\nTWO\nTHREE", 0, false, "Diff"),
                ("Alt+CursorDown", "one\nTWO\nTHREE", 1, false, "Diff"),
                ("Alt+Shift+CursorUp", "one\nTWO\nTHREE", 1, false, "Diff"),
                ("Alt+Shift+CursorDown", "one\nTWO\nTHREE", 1, false, "Diff"),
                ("Ctrl+Alt+CursorUp", "one\nTWO\nTHREE", 1, false, "Diff"),
                ("Ctrl+Alt+CursorDown", "one\nTWO\nTHREE", 1, false, "Diff"),
                ("Ctrl+T C", "one\nTWO\nTHREE", 1, false, "Diff")],
            seen);
    }

    // FocusService.ScopeOf maps both of these to Editor on purpose, so the keys still act on the file beneath.
    [Theory]
    [InlineData(CommandIds.FocusEditorTabStrip, "Tabs")]
    [InlineData(CommandIds.FindInFile, "Find")]
    public async Task The_editing_chords_still_reach_the_editor_from_a_region_that_scopes_to_it(string focusCommand, string word)
    {
        _fs.AddFile("/work/a.txt", new MockFileData(ThreeLines));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        EditorTab? tab = null;
        var seen = new List<(string Chord, string Lines, int Row, bool Cursors, bool ColumnSelect)>();

        var steps = new List<Delegate>
        {
            (Action)(() => { tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.txt")); tab.FocusContent(); }),
            (Func<bool>)(() => workbench.StatusBar.DisplayedFocus == "Editor"),
        };
        foreach (var (name, chord) in EditingChords)
        {
            // Re-focused each time: a chord that lands pulls focus into the editor body on its way out.
            steps.Add((Action)(() =>
            {
                tab!.Content = ThreeLines;
                tab.MoveCursor(1, 0);
                tab.RemoveSecondaryCursors();
                workbench.Editor.Group.ColumnSelect = false;
                _commands.TryExecute(focusCommand);
            }));
            steps.Add((Func<bool>)(() => workbench.StatusBar.DisplayedFocus == word));
            foreach (var key in chord)
            {
                var k = key;
                steps.Add((Action)(() => host.App.InjectKey(k)));
            }
            var chordName = name;
            steps.Add((Action)(() => seen.Add((chordName, string.Join('\n', tab!.Lines), tab.CursorRow,
                tab.HasSecondaryCursors, workbench.Editor.Group.ColumnSelect))));
        }

        await HostSteps.Run(host, [.. steps]);

        Assert.Equal(
            [("Alt+CursorUp", "two\none\nthree", 0, false, false),
                ("Alt+CursorDown", "one\nthree\ntwo", 2, false, false),
                ("Alt+Shift+CursorUp", "one\ntwo\ntwo\nthree", 1, false, false),
                ("Alt+Shift+CursorDown", "one\ntwo\ntwo\nthree", 2, false, false),
                ("Ctrl+Alt+CursorUp", ThreeLines, 1, true, false),
                ("Ctrl+Alt+CursorDown", ThreeLines, 1, true, false),
                ("Ctrl+T C", ThreeLines, 1, false, true)],
            seen);
    }

    // The tab-level commands stay Global because they're genuinely useful from the file tree (#283).
    [Fact]
    public async Task Ctrl_S_from_the_explorer_still_saves_the_tab_behind_it()
    {
        _fs.AddFile("/work/a.txt", new MockFileData(ThreeLines));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () => { tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.txt")); tab.Content = "edited"; },
            () => _commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => !tab!.IsDirty);

        Assert.Equal("edited\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    // Registering without a scope is the quiet way to give the editor's keys to every region (#283).
    [Fact]
    public void Every_command_that_keeps_no_scope_is_one_whose_key_belongs_in_every_region()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        string[] expected =
        [
            CommandIds.ChangeGrammar, CommandIds.CloseActiveEditor, CommandIds.CompareToOtherFile,
            CommandIds.CompareToRevision, CommandIds.CompareToSaved, CommandIds.CreateComment,
            CommandIds.FindGlobally, CommandIds.FindInFile, CommandIds.FocusEditorBody,
            CommandIds.FocusEditorTabStrip, CommandIds.FocusReview, CommandIds.FocusSidebar,
            CommandIds.GoToLine, CommandIds.NarrowSidebar, CommandIds.NavigateBack, CommandIds.NavigateForward,
            CommandIds.New, CommandIds.NextEditor, CommandIds.Open, CommandIds.OpenPullRequest,
            CommandIds.OpenSettings, CommandIds.PreviousEditor, CommandIds.PullRequestOverview,
            CommandIds.Quit, CommandIds.ReplaceGlobally, CommandIds.ReplaceInFile, CommandIds.SaveActiveEditor,
            CommandIds.ShowAbout, CommandIds.ShowActions, CommandIds.ShowDiagnostics,
            CommandIds.ShowDocumentInfo, CommandIds.ShowExplorer, CommandIds.ShowHelp, CommandIds.ShowMnemonics,
            CommandIds.SubmitReview, CommandIds.ToggleGutter, CommandIds.ToggleSidebar, CommandIds.WidenSidebar,
            .. Enumerable.Range(1, 9).Select(CommandIds.FocusEditorByIndex),
        ];

        var actual = _commands.Registered.Where(c => c.Scope == CommandScope.Global).Select(c => c.Id).ToArray();

        var extra = actual.Except(expected, StringComparer.Ordinal).ToArray();
        var gone = expected.Except(actual, StringComparer.Ordinal).ToArray();

        Assert.True(extra.Length == 0,
            $"Registered Global: {string.Join(", ", extra)}. A command whose key only makes sense in one region "
            + "needs that CommandScope, or its key acts on the file you aren't in; if the key really does belong "
            + "everywhere, add it to this list.");
        Assert.True(gone.Length == 0,
            $"No longer registered Global: {string.Join(", ", gone)}. Drop it from this list.");
    }

    [Fact]
    public void The_re_scoped_editing_commands_read_as_Editor_in_the_When_column()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        var rows = KeybindingRows.Build(_commands.Registered, [.. _keybindings.Bindings], "editor");
        var rescoped = rows.Where(r => r.CommandId.StartsWith("editor.action.", StringComparison.Ordinal)
            && r.CommandId != CommandIds.RemoveSecondaryCursors).ToArray();

        Assert.Equal(
            ["Add cursor above", "Add cursor below", "Duplicate line down", "Duplicate line up", "Move line down",
                "Move line up", "Select all occurrences", "Select next occurrence", "Select previous occurrence",
                "Toggle column select"],
            rescoped.Select(r => r.Label));
        Assert.All(rescoped, r => Assert.Equal(CommandScope.Editor, r.Scope));
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

    // One step per key, then one that records what the chord did; a chord's keys need their own iterations.
    private static IEnumerable<Delegate> PressEachChord(WorkbenchHost host, Action<string> record)
    {
        foreach (var (name, chord) in EditingChords)
        {
            foreach (var key in chord)
            {
                var k = key;
                yield return (Action)(() => host.App.InjectKey(k));
            }
            var chordName = name;
            yield return (Action)(() => record(chordName));
        }
    }

    private async Task<List<(string Chord, string Lines, int Row, bool Cursors, bool ColumnSelect, string Focus)>>
        PressTheEditingChordsAway(string focusCommand, string word)
    {
        _fs.AddFile("/work/a.txt", new MockFileData(ThreeLines));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        EditorTab? tab = null;
        var seen = new List<(string Chord, string Lines, int Row, bool Cursors, bool ColumnSelect, string Focus)>();

        var steps = new List<Delegate>
        {
            (Action)(() => { tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.txt")); tab.MoveCursor(1, 0); }),
            (Action)(() => _commands.TryExecute(focusCommand)),
            (Func<bool>)(() => workbench.StatusBar.DisplayedFocus == word),
        };
        steps.AddRange(PressEachChord(host, name => seen.Add((name, string.Join('\n', tab!.Lines), tab.CursorRow,
            tab.HasSecondaryCursors, workbench.Editor.Group.ColumnSelect, workbench.StatusBar.DisplayedFocus))));

        await HostSteps.Run(host, [.. steps]);

        Assert.False(tab!.IsDirty, "the hidden tab was edited");
        return seen;
    }

    private static KeybindingOverride Override(string keys, string command) => new(TestKeys.Chord(keys), command);

    private static bool Is(KeyBinding binding, CommandScope scope, string keys, string command) =>
        binding.Scope == scope && binding.CommandId == command && binding.CanonicalId == KeyChord.Canonical(TestKeys.Chord(keys));

    private static uint Code(string key) => (uint)TestKeys.Chord(key)[0].KeyCode;
}
