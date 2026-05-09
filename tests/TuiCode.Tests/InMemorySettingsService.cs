using TuiCode.Abstractions;

namespace TuiCode.Tests;

internal sealed class InMemorySettingsService : ISettingsService
{
    public string Theme { get; set; } = "Default";
    public IReadOnlyCollection<string> AvailableThemes { get; init; } =
        new[] { "Default", "Dark", "Light" };

    private List<KeybindingOverride> _overrides = new();
    public IReadOnlyList<KeybindingOverride> KeybindingOverrides => _overrides;
    public void SetKeybindingOverrides(IEnumerable<KeybindingOverride> overrides) =>
        _overrides = overrides.ToList();

    public int SaveCount { get; private set; }
    public void Save() => SaveCount++;
}
