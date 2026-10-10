using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TuiCode.Workbench.Workspace;

/// <summary>The files open in a workspace folder, in tab order, and which one is active (#13).</summary>
public sealed record WorkspaceState(IReadOnlyList<string> Files, string? ActiveFile);

/// <summary>
/// Persists <see cref="WorkspaceState"/> per folder to a JSON array ordered most recently used first,
/// keeping only the last <see cref="MaxFolders"/> folders so the file doesn't grow without bound.
/// </summary>
public sealed class WorkspaceStateStore
{
    public const int MaxFolders = 30;

    private readonly IFileSystem _fs;
    private readonly string _path;
    private readonly ILogger _logger;
    // Read and saved on every tab switch, so a failure that repeats is logged once until it stops.
    private bool _readFailed;
    private bool _writeFailed;

    public WorkspaceStateStore(IFileSystem fs, string path, ILogger? logger = null)
    {
        _fs = fs;
        _path = path;
        _logger = logger ?? NullLogger.Instance;
    }

    public static WorkspaceStateStore ForUser(IFileSystem fs, ILogger? logger = null) =>
        new(fs, fs.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tui", "TuiCode.workspaces.json"), logger);

    public WorkspaceState? Load(string folder)
    {
        foreach (var obj in ReadEntries())
        {
            if (FolderOf(obj) != folder) continue;
            try
            {
                var files = (obj["Files"] as JsonArray ?? [])
                    .Select(n => n?.GetValue<string>())
                    .OfType<string>()
                    .ToList();
                return new WorkspaceState(files, obj["Active"]?.GetValue<string>());
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>Every folder with saved state, most recently used first.</summary>
    public IReadOnlyList<string> Folders() => [.. ReadEntries().Select(FolderOf).OfType<string>()];

    public void Save(string folder, WorkspaceState state)
    {
        var files = new JsonArray();
        foreach (var file in state.Files) files.Add((JsonNode)JsonValue.Create(file));
        var entry = new JsonObject { ["Folder"] = folder, ["Files"] = files, ["Active"] = state.ActiveFile };

        var root = new JsonArray { (JsonNode)entry };
        foreach (var obj in ReadEntries().Where(o => FolderOf(o) is { } f && f != folder).Take(MaxFolders - 1))
            root.Add((JsonNode)obj.DeepClone());

        try
        {
            var dir = _fs.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) _fs.Directory.CreateDirectory(dir);
            _fs.File.WriteAllText(_path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            _writeFailed = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!_writeFailed) _logger.LogWarning(e, "Couldn't save the open files to {File}", _path);
            _writeFailed = true;
        }
    }

    private List<JsonObject> ReadEntries()
    {
        try
        {
            if (!_fs.File.Exists(_path)) return [];
            var entries = JsonNode.Parse(_fs.File.ReadAllText(_path)) is JsonArray arr ? arr.OfType<JsonObject>().ToList() : [];
            _readFailed = false;
            return entries;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            if (!_readFailed) _logger.LogWarning(e, "Couldn't read the open files from {File}", _path);
            _readFailed = true;
            return [];
        }
    }

    private static string? FolderOf(JsonObject obj) =>
        obj["Folder"] is JsonValue v && v.TryGetValue<string>(out var folder) ? folder : null;
}
