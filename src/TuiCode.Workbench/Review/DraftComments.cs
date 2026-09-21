using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Review;

/// <summary>
/// The line comments drafted on one PR (#188). They're kept under <c>~/.tui/reviews</c> rather than on
/// GitHub, so quitting doesn't lose them, and are posted with the review by <c>sr</c>.
/// </summary>
public sealed class DraftComments(IFileSystem fs, string folder)
{
    private readonly List<DraftComment> _drafts = [];
    private string? _repoRoot;
    private int _number;

    public static DraftComments ForUser(IFileSystem fs) =>
        new(fs, fs.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tui", "reviews"));

    public IReadOnlyList<DraftComment> All => _drafts;

    public int Count => _drafts.Count;

    /// <summary>Reads back the drafts on a PR, unless they're the ones already held.</summary>
    public void Open(string repoRoot, int number)
    {
        if (_repoRoot == repoRoot && _number == number) return;
        _repoRoot = repoRoot;
        _number = number;
        _drafts.Clear();
        _drafts.AddRange(Read());
    }

    public IReadOnlyList<DraftComment> On(string path) =>
        [.. _drafts.Where(d => string.Equals(d.Path, path, StringComparison.Ordinal))];

    public void Add(string path, int line, string body)
    {
        _drafts.Add(new DraftComment(path, line, body));
        Write();
    }

    public void Replace(DraftComment draft, string body)
    {
        var at = _drafts.IndexOf(draft);
        if (at < 0) return;
        _drafts[at] = draft with { Body = body };
        Write();
    }

    public void Remove(DraftComment draft)
    {
        if (_drafts.Remove(draft)) Write();
    }

    public void Clear()
    {
        if (_drafts.Count == 0) return;
        _drafts.Clear();
        Write();
    }

    /// <summary>What the Review tab says at its foot; empty while nothing is drafted.</summary>
    public string Line => _drafts.Count switch
    {
        0 => string.Empty,
        1 => "Draft review: 1 comment",
        var count => $"Draft review: {count} comments",
    };

    /// <summary>What the <c>sr</c> dialog says the review carries; empty while nothing is drafted.</summary>
    public static string Posting(int count) => count switch
    {
        0 => string.Empty,
        1 => "1 draft comment will be posted with it.",
        _ => $"{count} draft comments will be posted with it.",
    };

    private List<DraftComment> Read()
    {
        try
        {
            if (Path() is not { } path || !fs.File.Exists(path)) return [];
            if (JsonNode.Parse(fs.File.ReadAllText(path)) is not JsonArray array) return [];
            return [.. array.OfType<JsonObject>().Select(Draft).OfType<DraftComment>()];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static DraftComment? Draft(JsonObject entry)
    {
        try
        {
            return entry["Path"]?.GetValue<string>() is { } path && entry["Line"]?.GetValue<int>() is { } line
                ? new DraftComment(path, line, entry["Body"]?.GetValue<string>() ?? string.Empty)
                : null;
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private void Write()
    {
        if (Path() is not { } path) return;
        try
        {
            if (_drafts.Count == 0)
            {
                if (fs.File.Exists(path)) fs.File.Delete(path);
                return;
            }
            var array = new JsonArray();
            foreach (var draft in _drafts)
                array.Add((JsonNode)new JsonObject { ["Path"] = draft.Path, ["Line"] = draft.Line, ["Body"] = draft.Body });
            fs.Directory.CreateDirectory(folder);
            fs.File.WriteAllText(path, array.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Losing a draft is better than crashing on every keystroke in the comment dialog.
        }
    }

    private string? Path() => _repoRoot is { } repoRoot ? fs.Path.Combine(folder, $"{Key(repoRoot)}-{_number}.json") : null;

    /// <summary>The checkout's folder name, so the file is legible, and a hash of its path, so two checkouts don't share one.</summary>
    private string Key(string repoRoot)
    {
        var name = fs.Path.GetFileName(repoRoot.TrimEnd('/', '\\'));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(repoRoot)))[..8];
        return $"{string.Concat(name.Split(fs.Path.GetInvalidFileNameChars()))}-{hash}";
    }
}
