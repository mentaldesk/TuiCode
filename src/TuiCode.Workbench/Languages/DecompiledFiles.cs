using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Languages;

/// <summary>A library type's decompiled source on disk, shown read-only (#610). <see cref="Uri"/> is csharp-ls's name for it.</summary>
public sealed record DecompiledFile(string Path, string Name, string Assembly, string? Uri = null)
{
    /// <summary><c>Console.cs [decompiled]</c>: <see cref="Name"/> without its namespace or generic arity.</summary>
    public string Title
    {
        get
        {
            var name = Name[(Name.LastIndexOfAny(['.', '+']) + 1)..];
            return $"{(name.IndexOf('`') is var tick and > 0 ? name[..tick] : name)}.cs{DecompiledFiles.Suffix}";
        }
    }

    /// <summary><c>System.Console [decompiled]</c>, as the Definitions picker lists it.</summary>
    public string Place => $"{Name}{DecompiledFiles.Suffix}";

    public string Origin => $"Decompiled from {Assembly}, read-only";
}

/// <summary>Decompiled sources: csharp-ls's, which this writes to the temp folder, and the Roslyn server's in <c>MetadataAsSource</c> there.</summary>
public sealed partial class DecompiledFiles(IFileSystem fileSystem)
{
    public const string Suffix = " [decompiled]";
    public const string Refusal = "Decompiled source is read-only";

    private readonly Dictionary<string, DecompiledFile> _byPath = new(StringComparer.Ordinal);

    private string Temp => fileSystem.Path.GetTempPath();

    /// <summary>Writes csharp-ls's <paramref name="source"/> for <paramref name="uri"/> to a file of its own, kept per project.</summary>
    public DecompiledFile Write(string uri, DecompiledSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var project = uri[..uri.IndexOf("/decompiled/", StringComparison.Ordinal)];
        var folder = fileSystem.Path.Combine(Temp, "TuiCode", "decompiled", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(project)))[..8]);
        var path = fileSystem.Path.Combine(folder, uri[(uri.LastIndexOf('/') + 1)..]);
        fileSystem.Directory.CreateDirectory(folder);
        if (!fileSystem.File.Exists(path) || fileSystem.File.ReadAllText(path) != source.Source)
            fileSystem.File.WriteAllText(path, source.Source);
        var file = new DecompiledFile(path, source.SymbolName, source.AssemblyName, uri);
        _byPath[path] = file;
        return file;
    }

    /// <summary>The decompiled file at <paramref name="path"/>, or null for any other file.</summary>
    public DecompiledFile? At(string path)
    {
        if (_byPath.TryGetValue(path, out var file)) return file;
        if (!FilePaths.IsSameOrUnder(path, fileSystem.Path.Combine(Temp, "MetadataAsSource")) || !fileSystem.File.Exists(path)) return null;
        var header = fileSystem.File.ReadLines(path).FirstOrDefault() ?? "";
        var assembly = RoslynHeader().Match(header) is { Success: true } match ? match.Groups[1].Value : "a library";
        return new DecompiledFile(path, fileSystem.Path.GetFileNameWithoutExtension(path), assembly);
    }

    /// <summary><c>WriteLine(string? value)</c> from hover's <c>void Console.WriteLine(string? value)</c>, to tell overloads apart; null without one.</summary>
    public static string? Called(IReadOnlyList<HoverPart> hover) =>
        hover.FirstOrDefault(p => p.IsCode) is { } code && CalledSignature().Match(code.Text.Split('\n')[0]) is { Success: true } match
            ? match.Groups[1].Value
            : null;

    [GeneratedRegex(@"\b(\w+\(.*\))\s*$")]
    private static partial Regex CalledSignature();

    [GeneratedRegex(@"^\uFEFF?#region Assembly ([^,\s]+)")]
    private static partial Regex RoslynHeader();
}
