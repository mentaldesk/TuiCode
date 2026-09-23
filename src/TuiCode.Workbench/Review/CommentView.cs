using TuiCode.Abstractions;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Review;

/// <summary>
/// Modal for Create comment (<c>cc</c>, #188): what the draft on this line says, and the buttons to add,
/// delete or forget it. A draft passed in is being edited, so it can be deleted as well. On a thread it's
/// a reply instead (#189), which the host posts straight away and reports back on through
/// <see cref="ShowError"/>, so a refused reply keeps the dialog and its text.
/// </summary>
public sealed class CommentView : Window
{
    private const string MissingBody = "A comment needs something to say.";
    private const string MissingReply = "A reply needs something to say.";
    private const int DialogWidth = 70;
    private const int DialogHeight = 12 + InputView.Frame;

    private readonly InputView _body;
    private readonly System.Drawing.Point _end;
    private readonly AlertView _alert;
    private readonly Button[] _buttons;
    private readonly Button _confirm;
    private readonly string _missing;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;

    /// <summary>What the draft or reply should say; the host adds it, replaces the one being edited, or posts it.</summary>
    public event EventHandler<string>? Added;

    /// <summary>Raised by <c>[ Delete ]</c>, which only an existing draft has.</summary>
    public event EventHandler? Deleted;

    public CommentView(string path, int line, DraftComment? draft = null)
        : this($"Comment on {path}:{line}", "Add", MissingBody, draft?.Body, deletable: draft is not null)
    {
    }

    /// <summary>Replying to <paramref name="thread"/> (#189): the same dialog, posted rather than drafted.</summary>
    public CommentView(GitHubReviewThread thread)
        : this($"Reply to {thread.First?.Author}", "Reply", MissingReply, null, deletable: false)
    {
    }

    private CommentView(string title, string confirm, string missing, string? written, bool deletable)
    {
        Title = title;
        _missing = missing;
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        Height = DialogHeight;
        CanFocus = true;

        written ??= string.Empty;
        _body = new InputView
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
            Text = written,
        };

        var lines = written.Split('\n');
        _end = new System.Drawing.Point(lines[^1].Length, lines.Length - 1);

        _confirm = Foot(confirm, 1, OnAdd);
        var delete = deletable ? Foot("Delete", Pos.Right(_confirm) + 1, () => Deleted?.Invoke(this, EventArgs.Empty)) : null;
        var cancel = Foot("Cancel", Pos.Right(delete ?? _confirm) + 1, () => Cancelled?.Invoke(this, EventArgs.Empty));
        _buttons = delete is null ? [_confirm, cancel] : [_confirm, delete, cancel];

        // Below the buttons, not beside them: the dialog grows for an alert rather than the body shrinking.
        _alert = new AlertView(DialogWidth - 2) { X = 0, Y = Pos.AnchorEnd() };

        Add(_body, _alert);
        foreach (var button in _buttons) Add(button);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.CommentCancel, () => Cancelled?.Invoke(this, EventArgs.Empty));
        _scopeCommands.Register(CommandIds.CommentConfirm, OnAdd);
        _scopeKeybindings.Bind("Esc", CommandIds.CommentCancel);
        _scopeKeybindings.Bind("Ctrl+Enter", CommandIds.CommentConfirm);
    }

    private static Button Foot(string text, Pos x, Action accepted)
    {
        var button = new Button { X = x, Y = Pos.AnchorEnd(1), Text = text };
        button.Accepting += (_, e) => { e.Handled = true; accepted(); };
        return button;
    }

    public string Body => _body.Text ?? string.Empty;

    internal string Status => _alert.Message;

    /// <summary>Editing starts where the draft left off: the caret only stays put once the box has focus.</summary>
    public bool FocusBody()
    {
        var focused = _body.SetFocus();
        _body.InsertionPoint = _end;
        return focused;
    }

    /// <summary>Shows why GitHub refused a reply; the dialog stays open with whatever was typed (#189).</summary>
    public void ShowError(string message)
    {
        Alert(message, AlertSeverity.Error);
        _confirm.Enabled = true;
    }

    /// <summary>Shows that the reply is on its way, and refuses a second one while it is (#189).</summary>
    public void ShowBusy()
    {
        Alert("Replying…", AlertSeverity.Info);
        _confirm.Enabled = false;
    }

    private void Alert(string message, AlertSeverity severity)
    {
        _alert.Show(message, severity);
        Height = DialogHeight + _alert.Lines;
        _body.Height = Dim.Fill(_alert.Lines + 2);
        foreach (var button in _buttons) button.Y = Pos.AnchorEnd(_alert.Lines + 1);
        SetNeedsLayout();
    }

    private void OnAdd()
    {
        if (!_confirm.Enabled) return;

        var body = Body.Trim();
        if (body.Length == 0)
        {
            Alert(_missing, AlertSeverity.Error);
            _body.SetFocus();
            return;
        }
        Added?.Invoke(this, body);
    }
}
