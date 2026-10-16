using System.Globalization;
using System.Text;
using TuiCode.Editor;

namespace TuiCode.Workbench.Assistant;

public enum ChatLineKind { Blank, Heading, Text, Tool, Note, Code }

/// <summary>One screen row of the chat: who said it (null for a note), and for a code row its fenced block, the line in it and the UTF-16 offset the row starts at.</summary>
public sealed record ChatLine(ChatLineKind Kind, string Text, int Block = -1, int Line = -1, int Offset = 0, ChatRole? Role = null);

/// <summary>A fenced code block in an answer: its language as the fence names it, and its lines.</summary>
public sealed record CodeBlock(string Language, IReadOnlyList<string> Lines);

/// <summary>The chat in the AI assistance tab (#566): questions, streamed answers with a line per tool call, and notes. TG-free.</summary>
public sealed class Conversation
{
    private const int TabWidth = 4;

    /// <summary>Columns left blank left of the user's turns and right of the agent's.</summary>
    public const int Gutter = 4;

    private readonly List<Entry> _entries = [];

    /// <summary>Changes on every edit, so a view knows to lay out again.</summary>
    public int Version { get; private set; }

    public bool IsEmpty => _entries.Count == 0;

    /// <summary>The agent's name, which heads each answer.</summary>
    public string AgentName { get; set; } = "Agent";

    /// <summary>Folder the context labels of reloaded questions are relative to.</summary>
    public string? Root { get; set; }

    public void Clear()
    {
        _entries.Clear();
        Version++;
    }

    public void AddQuestion(string question, string? context)
    {
        _entries.Add(new Turn(ChatRole.User) { Context = context });
        Append(ChatRole.User, question);
    }

    public void AddNote(string note)
    {
        _entries.Add(new Note(note));
        Version++;
    }

    /// <summary>Starts a fresh answer, so a reply after a note doesn't run on from the last one.</summary>
    public void EndTurn() => _entries.Add(new Note(null));

    public void Apply(SessionUpdate update)
    {
        switch (update)
        {
            case MessageChunk chunk:
                Append(chunk.Role, chunk.Text);
                break;
            case ContextChunk { Role: ChatRole.User } context:
                TurnFor(ChatRole.User).Context ??= AskContext.LabelOf(context.Uri, Root);
                Version++;
                break;
            case ToolCallUpdate call:
                var turn = TurnFor(ChatRole.Agent);
                if (turn.Parts.OfType<ToolPart>().FirstOrDefault(p => p.Id == call.Id) is { } known)
                {
                    known.Title = call.Title ?? known.Title;
                    known.Status = call.Status ?? known.Status;
                }
                else turn.Parts.Add(new ToolPart(call.Id) { Title = call.Title ?? "Tool", Status = call.Status });
                Version++;
                break;
        }
    }

    private void Append(ChatRole role, string text)
    {
        var turn = TurnFor(role);
        if (turn.Parts.LastOrDefault() is not TextPart part) turn.Parts.Add(part = new TextPart());
        part.Text.Append(text);
        Version++;
    }

    private Turn TurnFor(ChatRole role)
    {
        if (_entries.LastOrDefault() is Turn turn && turn.Role == role) return turn;
        var started = new Turn(role);
        _entries.Add(started);
        return started;
    }

    /// <summary>The chat laid out at <paramref name="width"/> columns, and the code blocks its code rows refer to.</summary>
    public (IReadOnlyList<ChatLine> Lines, IReadOnlyList<CodeBlock> Blocks) Render(int width)
    {
        var lines = new List<ChatLine>();
        var blocks = new List<CodeBlock>();
        var turnWidth = Math.Max(width - Gutter, 1);
        foreach (var entry in _entries)
        {
            switch (entry)
            {
                case Note { Text: { } text }:
                    Gap(lines);
                    Wrap(lines, ChatLineKind.Note, text, width);
                    break;
                case Turn { Role: ChatRole.User } turn:
                    Gap(lines);
                    Wrap(lines, ChatLineKind.Heading, turn.Context is null ? "You" : $"You · {turn.Context}", turnWidth, ChatRole.User);
                    foreach (var part in turn.Parts.OfType<TextPart>())
                        foreach (var line in Split(part.Text.ToString().Trim('\n'))) Wrap(lines, ChatLineKind.Text, line, turnWidth, ChatRole.User);
                    break;
                case Turn turn:
                    Gap(lines);
                    Wrap(lines, ChatLineKind.Heading, AgentName, turnWidth, ChatRole.Agent);
                    foreach (var part in turn.Parts)
                    {
                        if (part is ToolPart tool) Wrap(lines, ChatLineKind.Tool, $"▸ {tool.Title}{(tool.Status == "failed" ? " (failed)" : "")}", turnWidth, ChatRole.Agent);
                        else if (part is TextPart text) Markdown(lines, blocks, text.Text.ToString(), turnWidth);
                    }
                    break;
            }
        }
        return (lines, blocks);
    }

    private static void Gap(List<ChatLine> lines)
    {
        if (lines.Count > 0) lines.Add(new ChatLine(ChatLineKind.Blank, ""));
    }

    private static string[] Split(string text) => text.Split('\n');

    // Text between ``` fences becomes code rows of its own block; everything else wraps as prose.
    private static void Markdown(List<ChatLine> lines, List<CodeBlock> blocks, string text, int width)
    {
        List<string>? code = null;
        var block = -1;
        foreach (var raw in Split(text.TrimStart('\n')))
        {
            var line = raw.TrimEnd('\r');
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (code is null)
                {
                    code = [];
                    block = blocks.Count;
                    blocks.Add(new CodeBlock(line.TrimStart().TrimStart('`').Trim(), code));
                }
                else code = null;
                continue;
            }
            if (code is null)
            {
                Wrap(lines, ChatLineKind.Text, line, width, ChatRole.Agent);
                continue;
            }
            var expanded = line.Replace("\t", new string(' ', TabWidth));
            code.Add(expanded);
            WrapCode(lines, block, code.Count - 1, expanded, width);
        }
    }

    private static void Wrap(List<ChatLine> lines, ChatLineKind kind, string text, int width, ChatRole? role = null)
    {
        var graphemes = Graphemes(text);
        var starts = WrapLayout.RowStarts(graphemes, width, TabWidth);
        for (var row = 0; row < starts.Length; row++)
        {
            var end = row + 1 < starts.Length ? starts[row + 1] : graphemes.Count;
            lines.Add(new ChatLine(kind, string.Concat(graphemes.Skip(starts[row]).Take(end - starts[row])).TrimEnd(), Role: role));
        }
    }

    private static void WrapCode(List<ChatLine> lines, int block, int line, string text, int width)
    {
        var graphemes = Graphemes(text);
        var starts = WrapLayout.RowStarts(graphemes, width, TabWidth);
        var offset = 0;
        for (var row = 0; row < starts.Length; row++)
        {
            var end = row + 1 < starts.Length ? starts[row + 1] : graphemes.Count;
            var piece = string.Concat(graphemes.Skip(starts[row]).Take(end - starts[row]));
            lines.Add(new ChatLine(ChatLineKind.Code, piece, block, line, offset, ChatRole.Agent));
            offset += piece.Length;
        }
    }

    private static List<string> Graphemes(string text)
    {
        var result = new List<string>(text.Length);
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) result.Add(enumerator.GetTextElement());
        return result;
    }

    private abstract class Entry;

    private sealed class Note(string? text) : Entry
    {
        public string? Text { get; } = text;
    }

    private sealed class Turn(ChatRole role) : Entry
    {
        public ChatRole Role { get; } = role;
        public string? Context { get; set; }
        public List<object> Parts { get; } = [];
    }

    private sealed class TextPart
    {
        public StringBuilder Text { get; } = new();
    }

    private sealed class ToolPart(string id)
    {
        public string Id { get; } = id;
        public string Title { get; set; } = "";
        public string? Status { get; set; }
    }
}
