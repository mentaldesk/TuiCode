using System.Globalization;
using System.Text;
using Terminal.Gui.Text;
using TuiCode.Abstractions;
using TuiCode.Syntax;
using TuiCode.Workbench.Controls;

namespace TuiCode.Workbench.Assistant;

/// <summary>The panel's AI assistance tab (#566): which agent and folder, the chat, and a box for the next question.</summary>
public sealed class AssistantView : View
{
    public const string TabTitle = "AI assistance";
    public const string FootText = "Esc stop · Ctrl+L clear";
    private const int InputRows = 2;

    private readonly VerbatimLabel _header;
    private readonly VerbatimLabel _context;
    private readonly InputView _input;
    private readonly VerbatimLabel _foot;
    private string _headerText = "";
    private string _contextText = "";

    public AssistantView(Conversation conversation, SyntaxHighlighter? syntax = null)
    {
        Conversation = conversation;
        CanFocus = true;
        _header = new VerbatimLabel { X = 0, Y = 0, Width = Dim.Fill(FootText.Length + 1), Height = 1, Text = "" };
        _foot = new VerbatimLabel { X = Pos.AnchorEnd(FootText.Length), Y = 0, Width = FootText.Length, Height = 1, Text = FootText };
        Chat = new ChatView(conversation, syntax) { X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Fill(InputRows + InputView.Frame + 1) };
        _context = new VerbatimLabel { X = 0, Y = Pos.AnchorEnd(InputRows + InputView.Frame + 1), Width = Dim.Fill(), Height = 1, Text = "" };
        _input = new InputView { X = 0, Y = Pos.AnchorEnd(InputRows + InputView.Frame), Width = Dim.Fill(), Height = InputRows + InputView.Frame };
        // GetAttributeForRole isn't virtual, so the hints are styled through its event; Handled makes the result stick.
        _context.GettingAttributeForRole += Faint;
        _foot.GettingAttributeForRole += Faint;
        Add(_header, Chat, _context, _input, _foot);
        ViewportChanged += (_, _) => ShowLines();
    }

    public Conversation Conversation { get; }

    public ChatView Chat { get; }

    public string HeaderText => _headerText;

    /// <summary>What the next question will be about, shown above the box.</summary>
    public string ContextText => _contextText;

    public string Question
    {
        get => _input.Text.TrimEnd('\n', '\r');
        set => _input.Text = value;
    }

    public bool InputHasFocus => _input.HasFocus;

    public bool FocusInput() => _input.SetFocus();

    public void ShowHeader(string text)
    {
        _headerText = text;
        ShowLines();
    }

    public void ShowContext(string? context)
    {
        _contextText = context is null ? "" : $"About {context}";
        ShowLines();
    }

    private void ShowLines()
    {
        _header.Text = Fit(_headerText, Viewport.Width - FootText.Length - 1);
        _context.Text = Fit(_contextText, Viewport.Width);
    }

    private static string Fit(string text, int columns)
    {
        if (columns <= 0 || text.GetColumns() <= columns) return text;
        var kept = new StringBuilder();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext() && kept.ToString().GetColumns() + enumerator.GetTextElement().GetColumns() <= columns - 1)
            kept.Append(enumerator.GetTextElement());
        return kept.Append('…').ToString();
    }

    public void Refresh() => Chat.Refresh();

    private void Faint(object? sender, VisualRoleEventArgs e)
    {
        var attribute = e.Result ?? GetAttributeForRole(e.Role);
        e.Result = attribute with { Style = attribute.Style | TextStyle.Faint };
        e.Handled = true;
    }
}
