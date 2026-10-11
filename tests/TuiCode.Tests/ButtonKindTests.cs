using System.IO.Abstractions.TestingHelpers;
using MentalDesk.Tui.Theming;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench.DocumentInfo;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

public class ButtonKindTests
{
    [Fact]
    public void A_new_comment_offers_Add_and_Cancel()
    {
        Assert.Equal(
            [("Add", SchemeNames.ButtonPrimary), ("Cancel", SchemeNames.ButtonSecondary)],
            Kinds(new CommentView("src/a.txt", 1)));
    }

    [Fact]
    public void A_draft_also_offers_Delete_as_a_danger()
    {
        Assert.Equal(
            [("Add", SchemeNames.ButtonPrimary), ("Delete", SchemeNames.ButtonDanger), ("Cancel", SchemeNames.ButtonSecondary)],
            Kinds(new CommentView("src/a.txt", 1, new DraftComment("src/a.txt", 1, "Rename this?"))));
    }

    [Fact]
    public void A_reply_offers_Reply_and_Cancel()
    {
        var thread = new GitHubReviewThread("src/a.txt", 1, false, false, [new GitHubComment("octocat", default, "Hm.")]);

        Assert.Equal(
            [("Reply", SchemeNames.ButtonPrimary), ("Cancel", SchemeNames.ButtonSecondary)],
            Kinds(new CommentView(thread)));
    }

    [Fact]
    public void Document_info_closes_with_a_primary_button()
    {
        var view = new DocumentInfoView("a.txt", "Plain Text", new DocumentStats(1, 1, 1, 1), null);

        Assert.Equal([("Close", SchemeNames.ButtonPrimary)], Kinds(view));
    }

    [Fact]
    public void The_conflict_box_has_a_primary_OK_that_Enter_presses()
    {
        var host = new View { Width = 80, Height = 24, CanFocus = true };
        host.BeginInit();
        host.EndInit();
        host.SetFocus();
        var closed = 0;
        KeybindingConflictDialog.Refuse(host, new InputScopeStack(), "Ctrl+Q can't be rebound.", () => closed++);

        var ok = host.SubViews.OfType<Dialog>().Single().SubViews.OfType<Button>().Single();
        Assert.Equal(("OK", SchemeNames.ButtonPrimary), Kind(ok));
        Assert.True(ok.HasFocus);

        host.NewKeyDownEvent(Key.Enter);

        Assert.Equal(1, closed);
    }

    [Fact]
    public void Open_Folder_in_an_empty_explorer_is_primary()
    {
        var sidebar = new SidebarPart(new FileExplorerView());

        Assert.Equal(("Open Folder", SchemeNames.ButtonPrimary), Kind(sidebar.OpenFolderButton));
    }

    [Fact]
    public void Open_this_folder_is_secondary()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/work");

        Assert.Equal([("Open this folder", SchemeNames.ButtonSecondary)], Kinds(new OpenView(fs.DirectoryInfo.New("/work"))));
    }

    [Fact]
    public void Terminal_integration_actions_are_primary_except_Remove()
    {
        Assert.Equal(
            [
                ("Install", SchemeNames.ButtonPrimary),
                ("Reinstall", SchemeNames.ButtonPrimary),
                ("Update", SchemeNames.ButtonPrimary),
                ("Remove", SchemeNames.ButtonSecondary),
            ],
            new[] { TerminalIntegrationAction.Install, TerminalIntegrationAction.Reinstall, TerminalIntegrationAction.Update, TerminalIntegrationAction.Remove }
                .Select(a => Kind(TerminalIntegrationPickerView.ButtonFor(a))));
    }

    [Fact]
    public void Terminal_integration_buttons_do_not_overlap()
    {
        var picker = new TerminalIntegrationPickerView(
            [new Integration(TerminalIntegrationStatus.Installed)], new FakeEnvironment());
        picker.Width = 80;
        picker.Height = 20;
        picker.BeginInit();
        picker.EndInit();
        picker.Layout();

        var buttons = picker.SubViews.OfType<Button>().ToList();

        Assert.Equal(["Reinstall", "Remove"], buttons.Select(b => b.Text.Trim()));
        Assert.True(buttons[0].Frame.Right < buttons[1].Frame.X, $"{buttons[0].Frame} runs into {buttons[1].Frame}");
    }

    private static List<(string, string)> Kinds(View view) =>
        [.. view.SubViews.OfType<Button>().Select(Kind)];

    private static (string, string) Kind(Button button) => (button.Text.Trim(), button.SchemeName!);

    private sealed class Integration(TerminalIntegrationStatus status) : ITerminalIntegration
    {
        public string Id => "iterm2";
        public string DisplayName => "iTerm2";
        public string? ClipboardInstructions => null;
        public bool IsAvailable() => true;
        public TerminalIntegrationStatus GetStatus() => status;
        public void Install() { }
        public void Uninstall() { }
    }
}
