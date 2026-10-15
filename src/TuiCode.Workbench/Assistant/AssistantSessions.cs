using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TuiCode.Workbench.Assistant;

/// <summary>
/// The last conversation in each folder (#566), as the agent's session id, in <c>~/.tui/TuiCode.assistant.json</c>:
/// a JSON array, most recently used folder first, capped at <see cref="MaxFolders"/>. The agent keeps the history itself.
/// </summary>
public sealed class AssistantSessions(IFileSystem fs, string path, ILogger? logger = null)
{
    public const int MaxFolders = 30;

    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private bool _failed;

    public static AssistantSessions ForUser(IFileSystem fs, ILogger? logger = null) =>
        new(fs, fs.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tui", "TuiCode.assistant.json"), logger);

    /// <summary>The session last used in <paramref name="folder"/> with <paramref name="agent"/>, or null.</summary>
    public string? Load(string folder, string agent) =>
        Read().FirstOrDefault(e => Text(e, "Folder") == folder && Text(e, "Agent") == agent) is { } entry ? Text(entry, "Session") : null;

    /// <summary>Remembers <paramref name="session"/> as <paramref name="folder"/>'s last; null forgets it.</summary>
    public void Save(string folder, string agent, string? session)
    {
        var root = new JsonArray();
        if (session is not null)
            root.Add((JsonNode)new JsonObject { ["Folder"] = folder, ["Agent"] = agent, ["Session"] = session });
        foreach (var entry in Read().Where(e => Text(e, "Folder") != folder).Take(MaxFolders - root.Count))
            root.Add((JsonNode)entry.DeepClone());
        try
        {
            var dir = fs.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) fs.Directory.CreateDirectory(dir);
            fs.File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Warn(e, "save");
        }
    }

    private List<JsonObject> Read()
    {
        try
        {
            if (!fs.File.Exists(path)) return [];
            return JsonNode.Parse(fs.File.ReadAllText(path)) is JsonArray entries ? [.. entries.OfType<JsonObject>()] : [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Warn(e, "read");
            return [];
        }
    }

    private void Warn(Exception e, string verb)
    {
        if (_failed) return;
        _failed = true;
        _logger.LogWarning(e, "Couldn't {Verb} the last AI assistance conversations in {File}", verb, path);
    }

    private static string? Text(JsonObject entry, string key) =>
        entry[key] is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;
}
