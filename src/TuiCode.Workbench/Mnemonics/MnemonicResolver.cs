namespace TuiCode.Workbench.Mnemonics;

/// <summary>A command paired with the mnemonic that triggers it and its display label.</summary>
public sealed record MnemonicEntry(string CommandId, string Mnemonic, string Label);

/// <summary>
/// Pure matching logic behind the mnemonic dialog — no Terminal.Gui dependency, so the
/// auto-execute rule can be unit-tested directly.
///
/// As the user types, the dialog feeds the accumulated prefix here. <see cref="Matching"/>
/// drives the filtered hint list; <see cref="ResolveExact"/> decides when to fire a command
/// without waiting for Enter: only when the prefix is itself a complete mnemonic and no other
/// mnemonic shares it as a prefix. Because the <see cref="Abstractions.CommandMnemonics"/>
/// table is prefix-free, "exactly one match and it equals the prefix" captures that precisely.
/// </summary>
public sealed class MnemonicResolver
{
    private readonly IReadOnlyList<MnemonicEntry> _entries;

    public MnemonicResolver(IEnumerable<MnemonicEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.OrderBy(e => e.Mnemonic, StringComparer.Ordinal).ToList();
    }

    /// <summary>Every entry, ordered by mnemonic — the dialog's initial (unfiltered) list.</summary>
    public IReadOnlyList<MnemonicEntry> All => _entries;

    /// <summary>Entries whose mnemonic starts with <paramref name="prefix"/> (empty prefix → all).</summary>
    public IReadOnlyList<MnemonicEntry> Matching(string prefix) =>
        string.IsNullOrEmpty(prefix)
            ? _entries
            : _entries.Where(e => e.Mnemonic.StartsWith(prefix, StringComparison.Ordinal)).ToList();

    /// <summary>
    /// The command to execute for this exact prefix, or null when the prefix is empty,
    /// matches nothing, or is still an incomplete/ambiguous lead-in (e.g. <c>c</c> while
    /// only <c>cf</c> exists, or <c>f</c> with fe/ft/f1… beneath it).
    /// </summary>
    public MnemonicEntry? ResolveExact(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return null;
        var matches = Matching(prefix);
        return matches.Count == 1 && matches[0].Mnemonic == prefix ? matches[0] : null;
    }
}
