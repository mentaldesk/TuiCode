using MentalDesk.Tui;
using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Fields;
using TuiCode.Abstractions;
using TuiCode.Workbench.Controls;

namespace TuiCode.Workbench.Review;

/// <summary>
/// Modal for Create comment (<c>cc</c>, #188): what the draft on this line says, and the buttons to add,
/// delete or forget it. A draft passed in is being edited, so it can be deleted as well. On a thread it's
/// a reply instead (#189), which the host posts straight away and reports back on through
/// <see cref="ShowError"/>, so a refused reply keeps the dialog and its text.
/// </summary>
public sealed class CommentView : AppDialog
{
    private const int DialogWidth = 70;
    private const int BodyRows = 8 + MultiLineBox.Frame;
    private const int ContentRows = BodyRows + 3;

    private readonly MultiLineField _body;
    private readonly VerbatimLabel _label;
    private readonly Button _confirm;
    private readonly string _field;

    /// <summary>What the draft or reply should say; the host adds it, replaces the one being edited, or posts it.</summary>
    public event EventHandler<string>? Added;

    /// <summary>Raised by Delete or <c>Ctrl+D</c>, which only an existing draft has.</summary>
    public event EventHandler? Deleted;

    public CommentView(string path, int line, DraftComment? draft = null)
        : this($"Comment on {path}:{line}", "Add", "Comment", draft?.Body, deletable: draft is not null)
    {
    }

    /// <summary>Replying to <paramref name="thread"/> (#189): the same dialog, posted rather than drafted.</summary>
    public CommentView(GitHubReviewThread thread)
        : this($"Reply to {thread.First?.Author}", "Reply", "Reply", null, deletable: false)
    {
    }

    private CommentView(string title, string confirm, string field, string? written, bool deletable)
        : base(title, DialogWidth, ContentRows)
    {
        _field = field;
        _label = new VerbatimLabel { X = 1, Y = 0, Text = field };
        _body = new MultiLineField
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = BodyRows,
            Text = written ?? string.Empty,
        }.HeavyWhileFocused();
        Add(_label, _body);
        Check(_body, _label, () => string.IsNullOrWhiteSpace(Body) ? $"{field} is needed" : null);

        Commands.Register(CommandIds.CommentConfirm, confirm, OnAdd);
        Keys.Bind("Ctrl+Enter", CommandIds.CommentConfirm);
        _confirm = CommandButton(ButtonKind.Primary, CommandIds.CommentConfirm);
        _confirm.IsDefault = false;
        var cancel = CommandButton(ButtonKind.Secondary, CancelId);
        if (deletable)
        {
            Commands.Register(CommandIds.CommentDelete, "Delete", OnDelete);
            Keys.Bind("Ctrl+D", CommandIds.CommentDelete);
            AddButtonRow(ContentRows - 1, _confirm, CommandButton(ButtonKind.Danger, CommandIds.CommentDelete), cancel);
        }
        else
        {
            AddButtonRow(ContentRows - 1, _confirm, cancel);
        }
    }

    public string Body => _body.Text ?? string.Empty;

    internal VerbatimLabel FieldLabel => _label;

    internal MultiLineField BodyBox => _body;

    /// <summary>Editing starts where the draft left off: the caret only stays put once the box has focus.</summary>
    public bool FocusBody()
    {
        var focused = _body.SetFocus();
        _body.CaretOffset = _body.Document!.TextLength;
        return focused;
    }

    /// <summary>Marks the box with why GitHub refused a reply; the dialog stays open with whatever was typed (#189).</summary>
    public void ShowError(string message)
    {
        _confirm.Enabled = true;
        Refuse(_body, $"{_field}: {message}");
    }

    /// <summary>Shows that the reply is on its way, and refuses a second one while it is (#189).</summary>
    public void ShowBusy()
    {
        ShowAlert("Replying…", Severity.Info);
        _confirm.Enabled = false;
    }

    /// <summary>Closes the dialog once the host has done what it was asked.</summary>
    public void Finish() => Accept();

    private void OnDelete() => Deleted?.Invoke(this, EventArgs.Empty);

    private void OnAdd()
    {
        if (_confirm.Enabled && Checked()) Added?.Invoke(this, Body.Trim());
    }
}
