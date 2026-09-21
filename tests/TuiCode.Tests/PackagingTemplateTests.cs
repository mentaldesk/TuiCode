using System.Text.Json;
using System.Text.RegularExpressions;

namespace TuiCode.Tests;

public class PackagingTemplateTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("tuicode.rb")]
    [InlineData("tuicode.json")]
    public void Every_packaging_placeholder_is_one_the_release_workflow_fills(string template) =>
        Assert.Empty(Placeholders(Template(template)).Except(Fillable()));

    [Fact]
    public void The_scoop_manifest_is_valid_json_once_rendered()
    {
        var rendered = Render(Template("tuicode.json"));

        Assert.Empty(Placeholders(rendered));
        using var manifest = JsonDocument.Parse(rendered);
        var architecture = manifest.RootElement.GetProperty("architecture");
        Assert.Equal("1.2.3", manifest.RootElement.GetProperty("version").GetString());
        Assert.Equal(Sha, architecture.GetProperty("64bit").GetProperty("hash").GetString());
        Assert.Equal(Sha, architecture.GetProperty("arm64").GetProperty("hash").GetString());
    }

    private static string Render(string template)
    {
        var rendered = template.Replace("{{version}}", "1.2.3");
        foreach (var placeholder in Fillable().Where(p => p != "{{version}}"))
            rendered = rendered.Replace(placeholder, Sha);
        return rendered;
    }

    private static IEnumerable<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{\{[^}]*\}\}").Select(m => m.Value).Distinct();

    /// <summary>The placeholders the publish workflow's render step substitutes, one sha per RID the build matrix produces.</summary>
    private static HashSet<string> Fillable()
    {
        var rids = Regex
            .Matches(File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "release.yml")), @"^\s*- rid: (\S+)\s*$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToArray();
        Assert.NotEmpty(rids);
        return [.. rids.Select(rid => $"{{{{sha_{rid.Replace('-', '_')}}}}}"), "{{version}}"];
    }

    private static string Template(string name) => File.ReadAllText(Path.Combine(RepoRoot(), "packaging", name));

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TuiCode.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }
}
