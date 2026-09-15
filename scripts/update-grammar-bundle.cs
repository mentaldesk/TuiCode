#:package TextMateSharp.Grammars@2.0.4

// Regenerates src/TuiCode.Syntax/Grammars.zip; see "Syntax highlighting" in AGENTS.md.

using System.IO.Compression;
using System.Text.Json.Nodes;

const string grammarPrefix = "TextMateSharp.Grammars.Resources.Grammars.";
const string themePrefix = "TextMateSharp.Grammars.Resources.Themes.";
const string manifestSuffix = ".package.json";

var assembly = typeof(TextMateSharp.Grammars.RegistryOptions).Assembly;
var resources = assembly.GetManifestResourceNames().ToHashSet(StringComparer.Ordinal);
var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);

foreach (var resource in resources.Order(StringComparer.Ordinal))
{
    if (resource.StartsWith(themePrefix, StringComparison.Ordinal))
    {
        entries["themes/" + resource[themePrefix.Length..]] = resource;
        continue;
    }
    if (!resource.StartsWith(grammarPrefix, StringComparison.Ordinal) || !resource.EndsWith(manifestSuffix, StringComparison.Ordinal))
        continue;

    var package = resource[grammarPrefix.Length..^manifestSuffix.Length];
    entries[$"grammars/{package}/package.json"] = resource;

    var manifest = JsonNode.Parse(ReadResource(resource))!;
    foreach (var grammar in manifest["contributes"]?["grammars"]?.AsArray() ?? [])
    {
        var path = grammar!["path"]!.GetValue<string>().TrimStart('.', '/');
        var grammarResource = grammarPrefix + package + "." + path.Replace('/', '.');
        // The package only embeds JSON grammars; a few manifests also name plist ones TextMateSharp can't read.
        if (resources.Contains(grammarResource))
            entries[$"grammars/{package}/{path}"] = grammarResource;
        else
            Console.WriteLine($"Skipping {package}/{path}: not embedded in the package");
    }
}

var output = args.Length > 0
    ? args[0]
    : Path.Combine((string)AppContext.GetData("EntryPointFileDirectoryPath")!, "..", "src", "TuiCode.Syntax", "Grammars.zip");
File.Delete(output);

// Fixed timestamps keep regeneration byte-for-byte reproducible.
var timestamp = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
using (var zip = ZipFile.Open(output, ZipArchiveMode.Create))
{
    foreach (var (path, resource) in entries)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.SmallestSize);
        entry.LastWriteTime = timestamp;
        using var source = OpenResource(resource);
        using var target = entry.Open();
        source.CopyTo(target);
    }
}

Console.WriteLine($"Wrote {entries.Count} files to {Path.GetFullPath(output)} ({new FileInfo(output).Length / 1024} KB)");

Stream OpenResource(string name) =>
    assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing resource {name}");

string ReadResource(string name)
{
    using var reader = new StreamReader(OpenResource(name));
    return reader.ReadToEnd();
}
