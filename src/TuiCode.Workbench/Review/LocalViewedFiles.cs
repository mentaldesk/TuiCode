using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Review;

/// <summary>
/// The files marked viewed on a branch with no PR (#400), kept under <c>~/.tui/viewed</c> per checkout and branch.
/// Each mark holds a hash of the file as it was viewed, so a file that has changed since reads as
/// <see cref="GitHubViewedState.Dismissed"/>, as GitHub does after a push.
/// </summary>
public sealed class LocalViewedFiles(IFileSystem fs, string folder)
{
    public static LocalViewedFiles ForUser(IFileSystem fs) =>
        new(fs, fs.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tui", "viewed"));

    /// <summary>The state of each of the review's files that has a mark; none when the marks can't be read.</summary>
    public IReadOnlyDictionary<string, GitHubViewedState> Read(BranchReview review)
    {
        var marks = Marks(review);
        var states = new Dictionary<string, GitHubViewedState>(StringComparer.Ordinal);
        foreach (var change in review.Changes)
        {
            if (marks.TryGetValue(change.Path, out var hash))
                states[change.Path] = hash == Hash(review.RepoRoot, change.Path) ? GitHubViewedState.Viewed : GitHubViewedState.Dismissed;
        }
        return states;
    }

    public void Set(BranchReview review, IEnumerable<string> paths, bool viewed)
    {
        var marks = Marks(review);
        var changed = false;
        foreach (var path in paths)
        {
            if (viewed) marks[path] = Hash(review.RepoRoot, path);
            changed |= viewed || marks.Remove(path);
        }
        if (changed) Write(review, marks);
    }

    private Dictionary<string, string> Marks(BranchReview review)
    {
        var marks = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var path = Path(review);
            if (!fs.File.Exists(path) || JsonNode.Parse(fs.File.ReadAllText(path)) is not JsonObject entries) return marks;
            foreach (var (file, hash) in entries)
            {
                if (hash is JsonValue value && value.TryGetValue<string>(out var text)) marks[file] = text;
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            marks.Clear();
        }
        return marks;
    }

    private void Write(BranchReview review, Dictionary<string, string> marks)
    {
        var path = Path(review);
        try
        {
            if (marks.Count == 0)
            {
                if (fs.File.Exists(path)) fs.File.Delete(path);
                return;
            }
            var entries = new JsonObject();
            foreach (var (file, hash) in marks) entries[file] = hash;
            fs.Directory.CreateDirectory(folder);
            fs.File.WriteAllText(path, entries.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A hash of the file's content, or empty when it's gone.</summary>
    private string Hash(string repoRoot, string path)
    {
        try
        {
            var full = fs.Path.Combine(repoRoot, path);
            return fs.File.Exists(full) ? Convert.ToHexStringLower(SHA256.HashData(fs.File.ReadAllBytes(full))) : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>The checkout's folder name, so the file is legible, and a hash of its path and branch, so neither shares one.</summary>
    private string Path(BranchReview review)
    {
        var name = fs.Path.GetFileName(review.RepoRoot.TrimEnd('/', '\\'));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{review.RepoRoot}\n{review.Branch}")))[..8];
        return fs.Path.Combine(folder, $"{string.Concat(name.Split(fs.Path.GetInvalidFileNameChars()))}-{hash}.json");
    }
}
