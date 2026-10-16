using System.Text.Json.Nodes;

namespace TuiCode.Workbench.Assistant;

public enum ChatRole { User, Agent }

/// <summary>What a <c>session/update</c> said, as far as the chat shows it.</summary>
public abstract record SessionUpdate
{
    public static SessionUpdate? Parse(JsonNode? update)
    {
        var kind = StringOf(update?["sessionUpdate"]);
        var content = update?["content"];
        return kind switch
        {
            "agent_message_chunk" => Message(ChatRole.Agent, content),
            "user_message_chunk" => Message(ChatRole.User, content),
            "tool_call" or "tool_call_update" when StringOf(update!["toolCallId"]) is { } id =>
                new ToolCallUpdate(id, StringOf(update["title"]), StringOf(update["status"])),
            _ => null,
        };
    }

    private static SessionUpdate? Message(ChatRole role, JsonNode? content) => StringOf(content?["type"]) switch
    {
        "text" when StringOf(content!["text"]) is { } text => new MessageChunk(role, text),
        "resource" when StringOf(content!["resource"]?["uri"]) is { } uri => new ContextChunk(role, uri),
        "resource_link" when StringOf(content!["uri"]) is { } uri => new ContextChunk(role, uri),
        _ => null,
    };

    internal static string? StringOf(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

public sealed record MessageChunk(ChatRole Role, string Text) : SessionUpdate;

/// <summary>A file attached to a message, by URI.</summary>
public sealed record ContextChunk(ChatRole Role, string Uri) : SessionUpdate;

public sealed record ToolCallUpdate(string Id, string? Title, string? Status) : SessionUpdate;
