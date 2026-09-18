using TuiCode.Abstractions;
using TuiCode.Workbench.Themes;

namespace TuiCode.Tests;

internal sealed class InMemorySettingsService : ISettingsService
{
    public string Theme
    {
        get;
        set
        {
            field = value;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    } = BundledThemes.Default;

    public event EventHandler? ThemeChanged;
    public IReadOnlyCollection<string> AvailableThemes { get; init; } =
        BundledThemes.Names;

    private List<KeybindingOverride> _overrides = new();
    public IReadOnlyList<KeybindingOverride> KeybindingOverrides => _overrides;
    public void SetKeybindingOverrides(IEnumerable<KeybindingOverride> overrides) =>
        _overrides = overrides.ToList();

    private Dictionary<string, string> _grammarAssociations = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> GrammarAssociations => _grammarAssociations;
    public void SetGrammarAssociations(IReadOnlyDictionary<string, string> associations) =>
        _grammarAssociations = new(associations, StringComparer.OrdinalIgnoreCase);

    public FileIconStyle FileIcons { get; set; }

    public int SaveCount { get; private set; }
    public void Save() => SaveCount++;

    public int LoadCount { get; private set; }
    public void Load() => LoadCount++;
}
