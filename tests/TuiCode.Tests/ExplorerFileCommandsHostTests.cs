using MentalDesk.Tui.Dialogs;
using TuiCode.Abstractions;
using Terminal.Gui.Views;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Key-driven tests for deleting, renaming and moving from the explorer (#101). Boots a TG Application — serialised (#77).
public class ExplorerFileCommandsHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeTrash _trash;

    public ExplorerFileCommandsHostTests()
    {
        _trash = new FakeTrash(_fs);
    }

    public static TheoryData<Key> DeleteKeys => [Key.Delete, Key.D.WithCtrl];

    [Theory]
    [MemberData(nameof(DeleteKeys))]
    public async Task Delete_in_the_explorer_asks_first_and_Esc_keeps_the_file(Key deleteKey)
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var asked = "";

        await HostSteps.Run(host,
            () => SelectInExplorer(workbench, "a.txt"),
            () => host.App.InjectKey(deleteKey),
            () => Confirm(workbench) is not null,
            () => { asked = Message(Confirm(workbench)!); host.App.InjectKey(Key.Esc); },
            () => Confirm(workbench) is null);

        Assert.Equal("Delete 'a.txt'?\nYou can restore it from the Trash.", asked);
        Assert.True(_fs.File.Exists("/work/a.txt"));
        Assert.Empty(_trash.Moved);
        Assert.True(workbench.Sidebar.Explorer.HasFocus, "Focus should return to the explorer after cancelling");
    }

    [Fact]
    public async Task Confirming_delete_moves_the_file_to_the_Trash_and_closes_its_tab_despite_unsaved_changes()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        _fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var asked = "";
        var enterKeptFile = false;

        await HostSteps.Run(host,
            () =>
            {
                workbench.OpenFile(_fs.FileInfo.New("/work/b.txt"));
                workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
                workbench.Editor.Group.ActiveTab!.Content = "edited";
            },
            () => SelectInExplorer(workbench, "a.txt"),
            () => host.App.InjectKey(Key.Delete),
            () => Confirm(workbench) is not null,
            () => { asked = Message(Confirm(workbench)!); host.App.InjectKey(Key.Enter); },
            () =>
            {
                enterKeptFile = Confirm(workbench) is not null && _fs.File.Exists("/work/a.txt");
                host.App.InjectKey(Key.Esc);
            },
            () => Confirm(workbench) is null,
            () => host.App.InjectKey(Key.Delete),
            () => Confirm(workbench) is not null,
            ConfirmDialogs.Choose(host, workbench, "Delete"),
            () => Confirm(workbench) is null);

        Assert.Contains("1 open file has unsaved changes", asked);
        Assert.True(enterKeptFile, "Enter closed the prompt or deleted the file");
        Assert.Equal([_fs.Path.GetFullPath("/work/a.txt")], _trash.Moved);
        Assert.False(_fs.File.Exists("/work/a.txt"));
        Assert.Equal([_fs.Path.GetFullPath("/work/b.txt")], workbench.Editor.Group.Tabs.Select(t => t.File.FullName));
        Assert.Equal("b.txt", workbench.Sidebar.Explorer.SelectedObject?.Name);
        Assert.Equal("🗑 Moved 'a.txt' to the Trash", workbench.StatusBar.DisplayedText);
        Assert.True(workbench.Sidebar.Explorer.HasFocus, "Focus should return to the explorer after deleting");
    }

    [Fact]
    public async Task Delete_on_a_folder_asks_about_its_contents_and_names_the_Recycle_Bin_on_Windows()
    {
        _fs.AddFile("/work/fixtures/one.json", new MockFileData("{}"));
        _fs.AddFile("/work/fixtures/two.json", new MockFileData("{}"));
        var trash = new FakeTrash(_fs) { Name = "Recycle Bin" };
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        using var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, trash: trash);
        var asked = "";
        string[] buttons = [], kinds = [];

        await HostSteps.Run(host,
            () =>
            {
                workbench.OpenFile(_fs.FileInfo.New("/work/fixtures/one.json"));
                workbench.Editor.Group.ActiveTab!.Content = "edited";
            },
            () => SelectInExplorer(workbench, "fixtures"),
            () => host.App.InjectKey(Key.Delete),
            () => Confirm(workbench) is not null,
            () => { asked = Message(Confirm(workbench)!); buttons = Confirm(workbench)!.Buttons(); kinds = Confirm(workbench)!.Kinds(); },
            ConfirmDialogs.Choose(host, workbench, "Delete"),
            () => Confirm(workbench) is null);

        Assert.Equal(
            "Delete 'fixtures' and its contents?\n1 open file has unsaved changes, which will be lost.\nYou can restore it from the Recycle Bin.",
            asked);
        Assert.Equal(["Ctrl+Enter Delete", "Esc Cancel"], buttons);
        Assert.Equal(["Danger", "Secondary"], kinds);
        Assert.Equal([_fs.Path.GetFullPath("/work/fixtures")], trash.Moved);
        Assert.Empty(workbench.Editor.Group.Tabs);
        Assert.Equal("🗑 Moved 'fixtures' to the Recycle Bin", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task When_the_Trash_refuses_a_second_prompt_says_why_and_Cancel_there_keeps_the_file()
    {
        _fs.AddFile("/work/build.log", new MockFileData("log"));
        _trash.Refusal = new IOException("/work has no Trash folder.");
        var enterKeptFile = false;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var asked = "";
        var title = "";

        await HostSteps.Run(host,
            () => SelectInExplorer(workbench, "build.log"),
            () => host.App.InjectKey(Key.Delete),
            () => Confirm(workbench) is not null,
            ConfirmDialogs.Choose(host, workbench, "Delete"),
            () => Confirm(workbench) is { } second && Message(second).StartsWith("Couldn't", StringComparison.Ordinal),
            () =>
            {
                asked = Message(Confirm(workbench)!);
                title = Confirm(workbench)!.Title;
                host.App.InjectKey(Key.Enter);
            },
            () =>
            {
                enterKeptFile = Confirm(workbench) is not null && _fs.File.Exists("/work/build.log");
                host.App.InjectKey(Key.Esc);
            },
            () => Confirm(workbench) is null);

        Assert.Equal(
            "Couldn't move 'build.log' to the Trash:\n/work has no Trash folder.\nDelete it permanently instead? This can't be undone.",
            asked);
        Assert.Equal("Delete", title);
        Assert.True(enterKeptFile, "Enter closed the prompt or deleted the file");
        Assert.True(_fs.File.Exists("/work/build.log"));
        Assert.True(workbench.Sidebar.Explorer.HasFocus, "Focus should return to the explorer after cancelling");
    }

    [Fact]
    public async Task When_the_Trash_refuses_Delete_permanently_in_the_second_prompt_deletes_for_good()
    {
        _fs.AddFile("/work/build.log", new MockFileData("log"));
        _fs.AddFile("/work/b.txt", new MockFileData("b"));
        _trash.Refusal = new UnauthorizedAccessException("Access to the path is denied.");
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => SelectInExplorer(workbench, "build.log"),
            () => host.App.InjectKey(Key.Delete),
            () => Confirm(workbench) is not null,
            ConfirmDialogs.Choose(host, workbench, "Delete"),
            () => Confirm(workbench) is { } second && Message(second).StartsWith("Couldn't", StringComparison.Ordinal),
            ConfirmDialogs.Choose(host, workbench, "Delete permanently"),
            () => Confirm(workbench) is null);

        Assert.False(_fs.File.Exists("/work/build.log"));
        Assert.StartsWith("Deleted: ", workbench.StatusBar.DisplayedText);
        Assert.True(workbench.Sidebar.Explorer.HasFocus, "Focus should return to the explorer after deleting");
    }

    [Fact]
    public async Task Shift_Delete_asks_to_delete_permanently_and_never_uses_the_Trash()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        _fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var asked = "";
        var title = "";

        await HostSteps.Run(host,
            () => SelectInExplorer(workbench, "a.txt"),
            () => host.App.InjectKey(Key.Delete.WithShift),
            () => Confirm(workbench) is not null,
            () =>
            {
                asked = Message(Confirm(workbench)!);
                title = Confirm(workbench)!.Title;
            },
            ConfirmDialogs.Choose(host, workbench, "Delete"),
            () => Confirm(workbench) is null);

        Assert.Equal("Permanently delete 'a.txt'?\nThis action is irreversible!", asked);
        Assert.Equal("Delete permanently", title);
        Assert.False(_fs.File.Exists("/work/a.txt"));
        Assert.Empty(_trash.Moved);
        Assert.StartsWith("Deleted: ", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task The_dp_mnemonic_deletes_permanently_from_the_explorer()
    {
        _fs.AddDirectory("/work/src/lib");
        _fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var asked = "";

        await HostSteps.Run(host,
            () => SelectInExplorer(workbench, "src"),
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.D),
            () => host.App.InjectKey(Key.P),
            () => Confirm(workbench) is not null,
            () => { asked = Message(Confirm(workbench)!); host.App.InjectKey(Key.Esc); },
            () => Confirm(workbench) is null);

        Assert.StartsWith("Permanently delete 'src' and its contents?", asked);
        Assert.True(_fs.Directory.Exists("/work/src/lib"));
    }

    [Fact]
    public async Task F2_renames_the_selected_file_and_its_tab_follows()
    {
        _fs.AddFile("/work/src/old.cs", new MockFileData("class A {}"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        EditorTab? tab = null;
        var prefill = ("", "");

        await HostSteps.Run(host,
            () => { workbench.OpenFile(_fs.FileInfo.New("/work/src/old.cs")); tab = workbench.Editor.Group.ActiveTab; },
            () => SelectInExplorer(workbench, "src/old.cs"),
            () => host.App.InjectKey(Key.F2),
            () => Prompt(workbench) is not null,
            () =>
            {
                prefill = (Prompt(workbench)!.Path, Prompt(workbench)!.SelectedText);
                foreach (var c in "renamed") host.App.InjectKey(new Key(c));
            },
            () => host.App.InjectKey(Key.Enter),
            () => Prompt(workbench) is null);

        Assert.Equal(("src/old.cs", "old"), prefill);
        Assert.False(_fs.File.Exists("/work/src/old.cs"));
        Assert.True(_fs.File.Exists("/work/src/renamed.cs"));
        Assert.Equal(_fs.Path.GetFullPath("/work/src/renamed.cs"), tab!.File.FullName);
        Assert.Equal("renamed.cs", tab.Title);
        Assert.Equal("renamed.cs", workbench.Sidebar.Explorer.SelectedObject?.Name);
        Assert.True(workbench.Sidebar.Explorer.HasFocus, "Focus should return to the explorer after renaming");
    }

    [Fact]
    public async Task A_clashing_rename_keeps_the_prompt_open_with_the_reason()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        _fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var error = "";

        await HostSteps.Run(host,
            () => SelectInExplorer(workbench, "a.txt"),
            () => host.App.InjectKey(Key.F2),
            () => Prompt(workbench) is not null,
            () => host.App.InjectKey(Key.B),
            () => host.App.InjectKey(Key.Enter),
            () =>
            {
                error = string.Join('|', Prompt(workbench)!.SubViews.OfType<Label>().Select(l => l.Text));
                host.App.InjectKey(Key.Esc);
            },
            () => Prompt(workbench) is null);

        Assert.Contains("'b.txt' already exists.", error);
        Assert.True(_fs.File.Exists("/work/a.txt"));
    }

    [Theory]
    [MemberData(nameof(DeleteKeys))]
    public async Task Delete_in_the_editor_still_deletes_text(Key deleteKey)
    {
        _fs.AddFile("/work/a.txt", new MockFileData("abc"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () => { workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")); tab = workbench.Editor.Group.ActiveTab; },
            () => host.App.InjectKey(deleteKey),
            () => tab!.Content.StartsWith("bc", StringComparison.Ordinal));

        Assert.Null(Confirm(workbench));
        Assert.True(_fs.File.Exists("/work/a.txt"));
    }

    [Fact]
    public async Task The_df_mnemonic_acts_on_the_explorer_selection_when_launched_from_the_explorer()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        _fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var asked = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/b.txt")),
            () => SelectInExplorer(workbench, "a.txt"),
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.D),
            () => host.App.InjectKey(Key.F),
            () => Confirm(workbench) is not null,
            () => { asked = Message(Confirm(workbench)!); host.App.InjectKey(Key.Esc); },
            () => Confirm(workbench) is null);

        Assert.Contains("'a.txt'", asked);
    }

    // The leader no longer offers df outside the explorer (#285), so the launcher can't reach this branch;
    // the command itself still targets the active file when the explorer isn't the context.
    [Fact]
    public async Task Delete_acts_on_the_active_file_when_the_explorer_is_not_the_context()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        _fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var asked = "";

        await HostSteps.Run(host,
            () => { workbench.Sidebar.Explorer.SelectedObject = Node(workbench, "a.txt"); },
            () => workbench.OpenFile(_fs.FileInfo.New("/work/b.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => commands.TryExecute(CommandIds.DeleteFile),
            () => Confirm(workbench) is not null,
            () => { asked = Message(Confirm(workbench)!); host.App.InjectKey(Key.Esc); },
            () => Confirm(workbench) is null);

        Assert.Contains("'b.txt'", asked);
    }

    [Fact]
    public async Task The_root_cannot_be_deleted()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () =>
            {
                workbench.Sidebar.Explorer.SetFocus();
                workbench.Sidebar.Explorer.SelectedObject = workbench.Sidebar.Explorer.Root;
            },
            () => host.App.InjectKey(Key.Delete),
            () => { });

        Assert.Null(Confirm(workbench));
        Assert.Equal("The workspace root can't be deleted.", workbench.StatusBar.DisplayedText);
        Assert.True(_fs.Directory.Exists("/work"));
    }

    [Fact]
    public async Task Cut_and_paste_onto_a_folder_moves_the_file_into_it_and_its_tab_follows()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        _fs.AddDirectory("/work/dest");
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var explorer = workbench.Sidebar.Explorer;
        EditorTab? tab = null;
        var dimmed = "";

        await HostSteps.Run(host,
            () =>
            {
                workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
                tab = workbench.Editor.Group.ActiveTab;
                tab!.Content = "edited";
            },
            () => SelectInExplorer(workbench, "a.txt"),
            () => host.App.InjectKey(Key.X.WithCtrl),
            () => { dimmed = explorer.PendingCut?.Name ?? ""; SelectInExplorer(workbench, "dest"); },
            () => host.App.InjectKey(Key.V.WithCtrl),
            () => { });

        Assert.Equal("a.txt", dimmed);
        Assert.False(_fs.File.Exists("/work/a.txt"));
        Assert.True(_fs.File.Exists("/work/dest/a.txt"));
        Assert.Equal(_fs.Path.GetFullPath("/work/dest/a.txt"), tab!.File.FullName);
        Assert.Equal("edited", tab.Content);
        Assert.True(tab.IsDirty);
        Assert.Null(explorer.PendingCut);
        Assert.Equal(_fs.Path.GetFullPath("/work/dest/a.txt"), explorer.SelectedObject?.FullName);
        Assert.True(explorer.HasFocus);
        Assert.StartsWith("Moved: ", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task Pasting_onto_a_file_moves_the_cut_folder_next_to_it()
    {
        _fs.AddFile("/work/src/lib/a.cs", new MockFileData("a"));
        _fs.AddFile("/work/tests/b.cs", new MockFileData("b"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => SelectInExplorer(workbench, "src/lib"),
            () => host.App.InjectKey(Key.X.WithCtrl),
            () => SelectInExplorer(workbench, "tests/b.cs"),
            () => host.App.InjectKey(Key.V.WithCtrl),
            () => { });

        Assert.True(_fs.File.Exists("/work/tests/lib/a.cs"));
        Assert.False(_fs.Directory.Exists("/work/src/lib"));
        Assert.Equal("lib", workbench.Sidebar.Explorer.SelectedObject?.Name);
    }

    [Theory]
    [InlineData("taken", "'dest/a.txt' already exists.")]
    [InlineData("into-itself", "Can't move 'src' into itself.")]
    public async Task A_refused_paste_shows_the_reason_and_keeps_the_cut(string scenario, string reason)
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        _fs.AddFile("/work/dest/a.txt", new MockFileData("other"));
        _fs.AddDirectory("/work/src/sub");
        var (cut, onto) = scenario == "taken" ? ("a.txt", "dest") : ("src", "src/sub");
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => SelectInExplorer(workbench, cut),
            () => host.App.InjectKey(Key.X.WithCtrl),
            () => SelectInExplorer(workbench, onto),
            () => host.App.InjectKey(Key.V.WithCtrl),
            () => { });

        Assert.Equal(reason, workbench.StatusBar.DisplayedText);
        Assert.Equal(cut.Split('/')[^1], workbench.Sidebar.Explorer.PendingCut?.Name);
        Assert.True(_fs.File.Exists("/work/a.txt"));
        Assert.True(_fs.Directory.Exists("/work/src/sub"));
    }

    [Fact]
    public async Task Pasting_into_the_folder_the_item_is_in_does_nothing()
    {
        _fs.AddFile("/work/src/a.txt", new MockFileData("a"));
        _fs.AddFile("/work/src/b.txt", new MockFileData("b"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => SelectInExplorer(workbench, "src/a.txt"),
            () => host.App.InjectKey(Key.X.WithCtrl),
            () => SelectInExplorer(workbench, "src/b.txt"),
            () => host.App.InjectKey(Key.V.WithCtrl),
            () => { });

        Assert.True(_fs.File.Exists("/work/src/a.txt"));
        Assert.Null(workbench.Sidebar.Explorer.PendingCut);
        Assert.Equal(workbench.StatusBar.DefaultMessage, workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task Esc_in_the_explorer_cancels_a_pending_cut_and_keeps_focus_there()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => SelectInExplorer(workbench, "a.txt"),
            () => host.App.InjectKey(Key.X.WithCtrl),
            () => host.App.InjectKey(Key.Esc),
            () => { });

        Assert.Null(workbench.Sidebar.Explorer.PendingCut);
        Assert.True(workbench.Sidebar.Explorer.HasFocus);
        Assert.Equal(workbench.StatusBar.DefaultMessage, workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task Cancelling_a_cut_keeps_a_message_that_replaced_the_cut_message()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => SelectInExplorer(workbench, "a.txt"),
            () => host.App.InjectKey(Key.X.WithCtrl),
            () => workbench.StatusBar.SetMessage("Saved: /work/b.txt"),
            () => SelectInExplorer(workbench, "a.txt"),
            () => host.App.InjectKey(Key.Esc),
            () => { });

        Assert.Null(workbench.Sidebar.Explorer.PendingCut);
        Assert.Equal("Saved: /work/b.txt", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task Deleting_the_folder_holding_a_cut_item_clears_the_cut_message()
    {
        _fs.AddFile("/work/src/a.txt", new MockFileData("a"));
        _fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => SelectInExplorer(workbench, "src/a.txt"),
            () => host.App.InjectKey(Key.X.WithCtrl),
            () => SelectInExplorer(workbench, "src"),
            () => host.App.InjectKey(Key.Delete),
            () => Confirm(workbench) is not null,
            ConfirmDialogs.Choose(host, workbench, "Delete"),
            () => Confirm(workbench) is null);

        Assert.Null(workbench.Sidebar.Explorer.PendingCut);
        Assert.Equal("🗑 Moved 'src' to the Trash", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task Esc_in_the_explorer_without_a_cut_still_focuses_the_editor()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => SelectInExplorer(workbench, "a.txt"),
            () => host.App.InjectKey(Key.Esc),
            () => { });

        Assert.True(workbench.Editor.Group.ActiveTab!.ContentHasFocus);
    }

    [Fact]
    public async Task Ctrl_X_and_Ctrl_V_in_the_editor_still_cut_and_paste_text()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("abc"));
        _fs.AddDirectory("/work/dest");
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () => SelectInExplorer(workbench, "a.txt"),
            () => host.App.InjectKey(Key.X.WithCtrl),
            () => SelectInExplorer(workbench, "dest"),
            () =>
            {
                workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
                tab = workbench.Editor.Group.ActiveTab;
                host.App.Clipboard!.SetClipboardData("xy");
            },
            () => host.App.InjectKey(Key.V.WithCtrl),
            () => tab!.Content.StartsWith("xy", StringComparison.Ordinal));

        Assert.True(_fs.File.Exists("/work/a.txt"));
        Assert.Equal("a.txt", workbench.Sidebar.Explorer.PendingCut?.Name);
    }

    [Fact]
    public async Task The_xf_and_pf_mnemonics_cut_and_paste_from_the_explorer()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        _fs.AddDirectory("/work/dest");
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => SelectInExplorer(workbench, "a.txt"),
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.X),
            () => host.App.InjectKey(Key.F),
            () => SelectInExplorer(workbench, "dest"),
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.P),
            () => host.App.InjectKey(Key.F),
            () => { });

        Assert.True(_fs.File.Exists("/work/dest/a.txt"));
        Assert.True(workbench.Sidebar.Explorer.HasFocus);
    }

    [Fact]
    public async Task The_root_cannot_be_cut()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () =>
            {
                workbench.Sidebar.Explorer.SetFocus();
                workbench.Sidebar.Explorer.SelectedObject = workbench.Sidebar.Explorer.Root;
            },
            () => host.App.InjectKey(Key.X.WithCtrl),
            () => { });

        Assert.Null(workbench.Sidebar.Explorer.PendingCut);
        Assert.Equal("The workspace root can't be cut.", workbench.StatusBar.DisplayedText);
    }

    private static ConfirmDialog? Confirm(Workbench.Workbench workbench) => ConfirmDialogs.Of(workbench);

    private static PathPromptView? Prompt(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<PathPromptView>().SingleOrDefault();

    private static string Message(ConfirmDialog view) => view.Message();

    private static void SelectInExplorer(Workbench.Workbench workbench, string relativePath)
    {
        workbench.Sidebar.Explorer.SetFocus();
        workbench.Sidebar.Explorer.SelectedObject = Node(workbench, relativePath);
    }

    private static IFileSystemInfo Node(Workbench.Workbench workbench, string relativePath)
    {
        var explorer = workbench.Sidebar.Explorer;
        IFileSystemInfo current = explorer.Root!;
        foreach (var segment in relativePath.Split('/'))
        {
            explorer.Expand(current);
            current = explorer.GetChildren(current).Single(c => c.Name == segment);
        }
        return current;
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench) => BuildHost(workbench, out _);

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, trash: _trash);
    }
}
