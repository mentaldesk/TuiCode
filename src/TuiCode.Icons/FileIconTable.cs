using System.Globalization;

namespace TuiCode.Icons;

internal static class FileIconTable
{
    private static readonly (Dictionary<string, FileIcon> Names, Dictionary<string, FileIcon> Extensions) Tables = Load();

    public static int Count => Tables.Names.Count + Tables.Extensions.Count;

    // Like nvim-web-devicons: the exact name, then each dotted suffix, longest first (spec.ts before ts).
    public static FileIcon? Lookup(string name)
    {
        if (Tables.Names.TryGetValue(name, out var icon))
            return icon;

        var extensions = Tables.Extensions.GetAlternateLookup<ReadOnlySpan<char>>();
        for (var dot = name.IndexOf('.'); dot >= 0; dot = name.IndexOf('.', dot + 1))
        {
            if (extensions.TryGetValue(name.AsSpan(dot + 1), out icon))
                return icon;
        }
        return null;
    }

    private static (Dictionary<string, FileIcon>, Dictionary<string, FileIcon>) Load()
    {
        var names = new Dictionary<string, FileIcon>(StringComparer.OrdinalIgnoreCase);
        var extensions = new Dictionary<string, FileIcon>(StringComparer.OrdinalIgnoreCase);

        using var stream = typeof(FileIconTable).Assembly.GetManifestResourceStream("file-icons.tsv")
            ?? throw new InvalidOperationException("The embedded file icons are missing.");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var fields = line.Split('\t');
            var icon = new FileIcon(
                char.ConvertFromUtf32(int.Parse(fields[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
                int.Parse(fields[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(fields[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            (fields[0] == "name" ? names : extensions)[fields[1]] = icon;
        }
        return (names, extensions);
    }
}
