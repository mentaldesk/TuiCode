using System.Text.RegularExpressions;

namespace TuiCode.Tests;

public class PackagingTemplateTests
{
    [Fact]
    public void Every_formula_placeholder_is_one_the_release_workflow_fills()
    {
        var root = RepoRoot();

        var rids = Regex
            .Matches(File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml")), @"^\s*- rid: (\S+)$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToArray();
        Assert.NotEmpty(rids);

        var fillable = rids
            .Select(rid => $"{{{{sha_{rid.Replace('-', '_')}}}}}")
            .Append("{{version}}")
            .ToHashSet();

        var placeholders = Regex
            .Matches(File.ReadAllText(Path.Combine(root, "packaging", "tuicode.rb")), @"\{\{[^}]*\}\}")
            .Select(m => m.Value)
            .Distinct();

        Assert.Empty(placeholders.Except(fillable));
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TuiCode.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }
}
