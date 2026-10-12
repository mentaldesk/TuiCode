using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuiCode.Abstractions;
using TuiCode.Editor;

namespace TuiCode.Workbench.Workspace;

/// <summary>
/// Where the user was in each file they've had open (#502), keyed by full path across every folder, as a JSON array
/// ordered most recently used first and capped at <see cref="MaxFiles"/>. Files inside <c>.git</c> are never kept.
/// </summary>
public sealed class FilePositionStore
{
    public const int MaxFiles = 300;

    private readonly IFileSystem _fs;
    private readonly string _path;
    private readonly ILogger _logger;
    private bool _readFailed;
    private bool _writeFailed;

    public FilePositionStore(IFileSystem fs, string path, ILogger? logger = null)
    {
        _fs = fs;
        _path = path;
        _logger = logger ?? NullLogger.Instance;
    }

    public static FilePositionStore ForUser(IFileSystem fs, ILogger? logger = null) =>
        new(fs, fs.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tui", "TuiCode.positions.json"), logger);

    public ViewPosition? Load(string file)
    {
        if (FilePaths.IsInGitDir(file)) return null;
        foreach (var obj in ReadEntries())
        {
            if (FileOf(obj) != file) continue;
            try
            {
                return new ViewPosition(Int(obj, "Line"), Int(obj, "Column"), Int(obj, "Top"), Int(obj, "Left"));
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException)
            {
                return null;
            }
        }
        return null;
    }

    private static int Int(JsonObject obj, string key) => Math.Max(obj[key]?.GetValue<int>() ?? 0, 0);

    /// <summary>Records each file's position, the first as the most recently used.</summary>
    public void Save(IEnumerable<(string File, ViewPosition Position)> positions)
    {
        var saving = positions.Where(p => !FilePaths.IsInGitDir(p.File)).DistinctBy(p => p.File, StringComparer.Ordinal).ToList();
        if (saving.Count == 0) return;

        var root = new JsonArray();
        foreach (var (file, position) in saving.Take(MaxFiles))
            root.Add((JsonNode)new JsonObject
            {
                ["File"] = file,
                ["Line"] = position.Line,
                ["Column"] = position.Column,
                ["Top"] = position.TopLine,
                ["Left"] = position.LeftColumn,
            });
        var saved = saving.Select(p => p.File).ToHashSet(StringComparer.Ordinal);
        foreach (var obj in ReadEntries().Where(o => FileOf(o) is { } f && !saved.Contains(f)).Take(MaxFiles - root.Count))
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
            if (!_writeFailed) _logger.LogWarning(e, "Couldn't save where you were in each file to {File}", _path);
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
            if (!_readFailed) _logger.LogWarning(e, "Couldn't read where you were in each file from {File}", _path);
            _readFailed = true;
            return [];
        }
    }

    private static string? FileOf(JsonObject obj) =>
        obj["File"] is JsonValue v && v.TryGetValue<string>(out var file) ? file : null;
}
