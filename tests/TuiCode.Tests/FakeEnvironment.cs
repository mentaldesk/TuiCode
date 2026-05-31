using TuiCode.Abstractions;

namespace TuiCode.Tests;

internal sealed class FakeEnvironment : IEnvironment
{
    private readonly Dictionary<string, string?> _vars = new(StringComparer.Ordinal);
    private readonly Dictionary<Environment.SpecialFolder, string> _folders = new();

    public FakeEnvironment Set(string name, string? value)
    {
        _vars[name] = value;
        return this;
    }

    public FakeEnvironment SetFolder(Environment.SpecialFolder folder, string path)
    {
        _folders[folder] = path;
        return this;
    }

    public string? GetEnvironmentVariable(string name) =>
        _vars.TryGetValue(name, out var v) ? v : null;

    public string GetFolderPath(Environment.SpecialFolder folder) =>
        _folders.TryGetValue(folder, out var p) ? p : string.Empty;
}
