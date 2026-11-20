namespace TuiCode.Workbench.Mnemonics;

/// <summary>A command paired with the mnemonic that triggers it and its display label.</summary>
public sealed record MnemonicEntry(string CommandId, string Mnemonic, string Label);

/// <summary>
/// Pure matching logic behind the mnemonic dialog — no Terminal.Gui dependency, so the
/// auto-execute rule can be unit-tested directly.
///
/// As the user types, the dialog feeds the accumulated prefix here. <see cref="Matching"/>
/// drives the filtered hint list; <see cref="ResolveExact"/> decides when to fire a command
/// without waiting for Enter: only when the prefix is itself a complete mnemonic and no longer
/// mnemonic starts with it. One that does (<c>q</c> under <c>qi</c>) runs on Enter (<see cref="Complete"/>).
/// </summary>
public sealed class MnemonicResolver
{
    private readonly IReadOnlyList<MnemonicEntry> _entries;
    private readonly IReadOnlyList<string> _every;

    /// <param name="every">Every mnemonic, in scope or not, so hiding a longer one never makes a shorter one fire early.</param>
    public MnemonicResolver(IEnumerable<MnemonicEntry> entries, IEnumerable<string>? every = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.OrderBy(e => e.Mnemonic, StringComparer.Ordinal).ToList();
        _every = [.. (every ?? []).Concat(_entries.Select(e => e.Mnemonic))];
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
    public MnemonicEntry? ResolveExact(string prefix) =>
        Complete(prefix) is { } entry && !_every.Any(m => m.Length > prefix.Length && m.StartsWith(prefix, StringComparison.Ordinal))
            ? entry
            : null;

    /// <summary>The entry whose mnemonic is exactly <paramref name="prefix"/>, whether or not a longer one starts with it.</summary>
    public MnemonicEntry? Complete(string prefix) =>
        string.IsNullOrEmpty(prefix) ? null : _entries.FirstOrDefault(e => e.Mnemonic == prefix);
}
