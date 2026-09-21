using Terminal.Gui.Text;

namespace TuiCode.Workbench.Controls;

/// <summary>How an <see cref="AlertView"/> message reads, and so which theme scheme it draws in.</summary>
public enum AlertSeverity
{
    Info,
    Error,
}

/// <summary>
/// A message block for the foot of a dialog: full width, in the theme's own colours, wrapping onto as
/// many rows as the message needs and taking none at all while there is nothing to say. Dialogs sized
/// it themselves rather than letting a <c>Label</c> grow, since a Label that outgrows its one row draws
/// straight over whatever sits below it. <see cref="Lines"/> is how many rows to leave for it.
/// </summary>
public sealed class AlertView : View
{
    private const string Indent = " ";

    private readonly int _textWidth;

    /// <param name="width">Columns the block spans; the message wraps inside it, one column in from each side.</param>
    public AlertView(int width)
    {
        _textWidth = width - (Indent.Length * 2);
        Width = width;
        Height = 0;
        Visible = false;
        CanFocus = false;
        TextFormatter.WordWrap = true;
        TextFormatter.MultiLine = true;
    }

    /// <summary>The message as it was given, unwrapped.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>Rows the wrapped message occupies; zero when there is no message.</summary>
    public int Lines { get; private set; }

    public void Show(string message, AlertSeverity severity)
    {
        Message = message;
        SchemeName = severity == AlertSeverity.Error ? "Error" : "Accent";
        var wrapped = Wrap(message, _textWidth);
        Lines = wrapped.Count;
        Text = string.Join('\n', wrapped.Select(line => Indent + line));
        Height = Lines;
        Visible = Lines > 0;
    }

    public void Clear() => Show(string.Empty, AlertSeverity.Info);

    private static List<string> Wrap(string message, int width) =>
        message.Length == 0
            ? []
            : Terminal.Gui.Text.TextFormatter.Format(
                message, width, Alignment.Start, wordWrap: true, preserveTrailingSpaces: false, tabWidth: 4,
                TextDirection.LeftRight_TopBottom, multiLine: false, textFormatter: null, preserveTabs: false);
}
