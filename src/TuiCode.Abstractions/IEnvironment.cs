namespace TuiCode.Abstractions;

/// <summary>
/// Wraps process environment + well-known folder lookups so terminal-integration code can be
/// unit-tested without poking at the real shell. Implementations delegate to
/// <see cref="System.Environment"/>; tests substitute a fake.
/// </summary>
public interface IEnvironment
{
    string? GetEnvironmentVariable(string name);

    string GetFolderPath(Environment.SpecialFolder folder);
}
