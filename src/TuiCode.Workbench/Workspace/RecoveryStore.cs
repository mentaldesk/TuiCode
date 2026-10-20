using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuiCode.Editor;

namespace TuiCode.Workbench.Workspace;

/// <summary>The process that kept a recovery copy, told apart from a later one given the same id.</summary>
public sealed record RecoveryOwner(int ProcessId, DateTime StartedUtc)
{
    public static RecoveryOwner Current
    {
        get
        {
            using var process = Process.GetCurrentProcess();
            return new(process.Id, process.StartTime.ToUniversalTime());
        }
    }

    public static bool IsRunning(RecoveryOwner owner)
    {
        try
        {
            using var process = Process.GetProcessById(owner.ProcessId);
            return !process.HasExited && (process.StartTime.ToUniversalTime() - owner.StartedUtc).Duration() < TimeSpan.FromSeconds(1);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false;
        }
        // Can't read its start time, so it may still be the owner: leave its copies alone.
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return true;
        }
    }
}

/// <summary>
/// Copies of dirty tabs (#579), one file per file and process, so a session that ends unexpectedly can be picked up
/// again. Each process writes and deletes only its own copies, and copies are offered only once their process has gone.
/// </summary>
public sealed class RecoveryStore
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly IFileSystem _fs;
    private readonly string _directory;
    private readonly RecoveryOwner _self;
    private readonly Func<RecoveryOwner, bool> _isRunning;
    private readonly ILogger _logger;
    private bool _writeFailed;

    public RecoveryStore(IFileSystem fs, string directory, RecoveryOwner self, Func<RecoveryOwner, bool> isRunning, ILogger? logger = null)
    {
        _fs = fs;
        _directory = directory;
        _self = self;
        _isRunning = isRunning;
        _logger = logger ?? NullLogger.Instance;
    }

    public static RecoveryStore ForUser(IFileSystem fs, ILogger? logger = null) =>
        new(fs, fs.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tui", "recovery"),
            RecoveryOwner.Current, RecoveryOwner.IsRunning, logger);

    /// <summary>The newest edits to <paramref name="file"/> kept by a process that has gone; null when there are none.</summary>
    public RecoveredEdits? Find(string file) =>
        LeftBehind(file).OrderByDescending(c => c.Written).Select(c => c.Edits).FirstOrDefault();

    /// <summary>Keeps <paramref name="edits"/> as this process's copy for <paramref name="file"/>, replacing any before.</summary>
    public void Write(string file, RecoveredEdits edits)
    {
        var position = edits.Position;
        var json = new JsonObject
        {
            ["File"] = file,
            ["Process"] = _self.ProcessId,
            ["Started"] = _self.StartedUtc.ToString("O"),
            ["Written"] = DateTime.UtcNow.ToString("O"),
            ["Line"] = position.Line,
            ["Column"] = position.Column,
            ["Top"] = position.TopLine,
            ["Left"] = position.LeftColumn,
            ["Text"] = edits.Text,
            ["OnDisk"] = new JsonObject
            {
                ["Modified"] = edits.OnDisk.LastWriteTimeUtc.ToString("O"),
                ["Length"] = edits.OnDisk.Length,
                ["Content"] = edits.OnDisk.Content,
            },
        }.ToJsonString();

        var path = PathFor(file, _self.ProcessId);
        var temp = path + ".tmp";
        try
        {
            _fs.Directory.CreateDirectory(_directory);
            if (OperatingSystem.IsWindows())
            {
                _fs.File.WriteAllText(temp, json);
                _fs.File.Move(temp, path, overwrite: true);
            }
            else
            {
                using (var stream = _fs.FileStream.New(temp, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = OwnerOnly }))
                using (var writer = new StreamWriter(stream))
                    writer.Write(json);
                _fs.File.Move(temp, path, overwrite: true);
                // The create mode doesn't apply to a temp file a crash left behind.
                _fs.File.SetUnixFileMode(path, OwnerOnly);
            }
            _writeFailed = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!_writeFailed) _logger.LogWarning(e, "Couldn't keep a copy of the unsaved edits to {File} in {Directory}", file, _directory);
            _writeFailed = true;
        }
    }

    /// <summary>Drops this process's copy for <paramref name="file"/>.</summary>
    public void Delete(string file) => TryDelete(PathFor(file, _self.ProcessId));

    /// <summary>Drops the copies for <paramref name="file"/> that processes which have gone left behind.</summary>
    public void Forget(string file)
    {
        foreach (var copy in LeftBehind(file))
            TryDelete(copy.Path);
    }

    private IEnumerable<Copy> LeftBehind(string file)
    {
        if (!_fs.Directory.Exists(_directory)) return [];
        string[] paths;
        try
        {
            paths = _fs.Directory.GetFiles(_directory, $"{KeyOf(file)}-*.json");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        return paths.Select(Read).OfType<Copy>()
            .Where(c => c.File == file && c.Owner != _self && !_isRunning(c.Owner))
            .ToList();
    }

    private Copy? Read(string path)
    {
        try
        {
            if (JsonNode.Parse(_fs.File.ReadAllText(path)) is not JsonObject obj) return null;
            var disk = obj["OnDisk"]!.AsObject();
            return new Copy(
                path,
                obj["File"]!.GetValue<string>(),
                new RecoveryOwner(obj["Process"]!.GetValue<int>(), Time(obj["Started"])),
                Time(obj["Written"]),
                new RecoveredEdits(
                    obj["Text"]!.GetValue<string>(),
                    new ViewPosition(Int(obj, "Line"), Int(obj, "Column"), Int(obj, "Top"), Int(obj, "Left")),
                    new FileSnapshot(Time(disk["Modified"]), disk["Length"]!.GetValue<long>(), disk["Content"]!.GetValue<string>())));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or NullReferenceException)
        {
            return null;
        }
    }

    private static DateTime Time(JsonNode? node) =>
        DateTime.Parse(node!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);

    private static int Int(JsonObject obj, string key) => Math.Max(obj[key]?.GetValue<int>() ?? 0, 0);

    private void TryDelete(string path)
    {
        try
        {
            _fs.File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string PathFor(string file, int processId) => _fs.Path.Combine(_directory, $"{KeyOf(file)}-{processId}.json");

    private static string KeyOf(string file) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(file)))[..16];

    private sealed record Copy(string Path, string File, RecoveryOwner Owner, DateTime Written, RecoveredEdits Edits);
}
