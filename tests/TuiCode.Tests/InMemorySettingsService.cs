using TuiCode.Abstractions;

namespace TuiCode.Tests;

internal sealed class InMemorySettingsService : ISettingsService
{
    public string Theme { get; set; } = "Default";
    public IReadOnlyCollection<string> AvailableThemes { get; init; } =
        new[] { "Default", "Dark", "Light" };

    public int SaveCount { get; private set; }
    public void Save() => SaveCount++;
}
