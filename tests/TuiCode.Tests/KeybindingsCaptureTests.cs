using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// Boots a TG Application via WorkbenchHost — process-global TG state, must be serialised (issue #77).
public class KeybindingsCaptureTests : StaticConfigurationTest
{
    [Fact]
    public async Task Capture_ignores_modifier_only_keypresses()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        var scopes = new InputScopeStack();
        var settings = new InMemorySettingsService();
        using var host = new WorkbenchHost(workbench, commands, keybindings, scopes, settings, driverName: DriverRegistry.Names.ANSI);

        KeybindingsPickerView? picker = null;
        var step = 0;
        string? failure = null;

        host.App.Iteration += OnIteration;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.RunAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "RunAsync timed out");

        Assert.Null(failure);
        Assert.NotNull(picker);

        // The injected sequence was: Ctrl-only, Ctrl+Shift-only, Ctrl+Alt+Shift-only, Ctrl+Alt+Shift+G, e, Enter.
        // Modifier-only events should be filtered, leaving "Ctrl+Alt+Shift+G e" (or similar).
        var defaults = keybindings.Bindings.Select(b => b.CanonicalId).ToHashSet(StringComparer.Ordinal);
        var added = picker!.CurrentBindings.Where(b => !defaults.Contains(b.CanonicalId)).ToList();
        var dump = string.Join("\n  ", picker.CurrentBindings.Select(b => $"{b.Display} -> {b.CommandId}"));
        Assert.True(added.Count == 1, $"expected exactly 1 new binding, got {added.Count}. All bindings:\n  {dump}");
        Assert.Contains("G", added[0].Display, StringComparison.Ordinal);

        void OnIteration(object? sender, EventArgs<IApplication?> e)
        {
            step++;
            switch (step)
            {
                case 1: Inject("Ctrl+,"); break;
                case 2: Inject("CursorDown"); break;       // select Keyboard Shortcuts
                case 3: Inject("CursorRight"); break;       // drill into picker
                case 4:
                    picker = workbench.SubViews.OfType<SettingsView>().FirstOrDefault()?
                        .SubViews.OfType<KeybindingsPickerView>().FirstOrDefault();
                    if (picker is null) { failure = "picker not found"; Quit(); return; }
                    Inject("Enter");                        // start capture for first row
                    break;
                case 5: InjectRaw(KeyCode.CtrlMask); break;                                            // modifier only
                case 6: InjectRaw(KeyCode.CtrlMask | KeyCode.ShiftMask); break;                       // modifier only
                case 7: InjectRaw(KeyCode.CtrlMask | KeyCode.AltMask | KeyCode.ShiftMask); break;     // modifier only
                case 8: InjectRaw(KeyCode.CtrlMask | KeyCode.AltMask | KeyCode.ShiftMask | KeyCode.G); break; // real key
                case 9: InjectRaw(KeyCode.E); break;        // chord step 2 (will normalize to lowercase)
                case 10: Inject("Enter"); break;            // commit capture
                case 11: Quit(); break;
            }
        }

        void Inject(string seq)
        {
            if (Key.TryParse(seq, out var k)) host.App.InjectKey(k);
        }
        void InjectRaw(KeyCode code) => host.App.InjectKey(new Key(code));
        void Quit()
        {
            if (Key.TryParse("Ctrl+Q", out var k)) host.App.InjectKey(k);
        }
    }

    [Fact]
    public async Task Capture_rejects_a_first_key_that_types_text_but_allows_it_later_in_the_chord()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
        var defaults = keybindings.Bindings.Select(b => b.CanonicalId).ToHashSet(StringComparer.Ordinal);
        KeybindingsPickerView? picker = null;

        await HostSteps.Run(host,
            () => host.App.InjectKey(new Key(',').WithCtrl),
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorRight),
            () =>
            {
                picker = workbench.SubViewsDeep().OfType<KeybindingsPickerView>().Single();
                host.App.InjectKey(Key.Enter);
            },
            () => host.App.InjectKey(new Key('T')),
            () => host.App.InjectKey(new Key('t')),
            () => host.App.InjectKey(Key.T.WithCtrl),
            () => host.App.InjectKey(new Key('g')),
            () => host.App.InjectKey(Key.Enter));

        var added = Assert.Single(picker!.CurrentBindings, b => !defaults.Contains(b.CanonicalId));
        Assert.Equal([Key.T.WithCtrl, Key.G], added.Chord);
    }

    [Theory]
    [InlineData((uint)KeyCode.T)]
    [InlineData((uint)(KeyCode.T | KeyCode.ShiftMask))]
    [InlineData((uint)KeyCode.D1)]
    [InlineData((uint)KeyCode.Space)]
    [InlineData((uint)'/')]
    public void TypesText_is_true_for_keys_that_insert_characters(uint keyCode) =>
        Assert.True(KeybindingsPickerView.TypesText(new Key((KeyCode)keyCode)));

    [Theory]
    [InlineData((uint)(KeyCode.T | KeyCode.CtrlMask))]
    [InlineData((uint)(KeyCode.T | KeyCode.AltMask))]
    [InlineData((uint)(KeyCode.Space | KeyCode.CtrlMask))]
    [InlineData((uint)KeyCode.F1)]
    [InlineData((uint)KeyCode.Esc)]
    [InlineData((uint)KeyCode.Enter)]
    [InlineData((uint)KeyCode.Tab)]
    [InlineData((uint)KeyCode.CursorUp)]
    [InlineData((uint)KeyCode.Delete)]
    public void TypesText_is_false_for_modified_and_non_printing_keys(uint keyCode) =>
        Assert.False(KeybindingsPickerView.TypesText(new Key((KeyCode)keyCode)));

    private static Workbench.Workbench BuildWorkbench() =>
        new(
            new SidebarPart(new FileExplorerView()),
            new EditorPart(),
            new StatusBarPart());
}
