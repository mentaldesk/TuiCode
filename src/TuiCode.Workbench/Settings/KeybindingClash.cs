using TuiCode.Abstractions;

namespace TuiCode.Workbench.Settings;

/// <summary>
/// An existing binding a new one would clash with (#142). In the same scope it has to be replaced or removed first; a
/// scoped binding against a Global one only warns, since the focused scope wins and Global is the fallback.
/// </summary>
internal sealed record KeybindingClash(KeybindingConflict Kind, KeyBinding Existing, KeyBinding Candidate)
{
    public bool IsWarning => Existing.Scope != Candidate.Scope;

    /// <summary>Same-scope clashes first, then a scope against Global. Bindings in two different non-Global scopes never clash.</summary>
    public static KeybindingClash? Find(IEnumerable<KeyBinding> current, KeyBinding candidate)
    {
        var bindings = current.ToArray();
        return First(bindings.Where(b => b.Scope == candidate.Scope), candidate)
            ?? First(bindings.Where(b => b.Scope != candidate.Scope
                && (b.Scope == CommandScope.Global || candidate.Scope == CommandScope.Global)), candidate);
    }

    private static KeybindingClash? First(IEnumerable<KeyBinding> bindings, KeyBinding candidate)
    {
        var chord = Keycodes(candidate.Chord);
        foreach (var b in bindings)
        {
            var existing = Keycodes(b.Chord);
            if (existing.SequenceEqual(chord))
                return new(KeybindingConflict.ExactMatch, b, candidate);
            if (existing.Length > chord.Length && existing.Take(chord.Length).SequenceEqual(chord))
                return new(KeybindingConflict.PrefixOfExisting, b, candidate);
            if (existing.Length < chord.Length && chord.Take(existing.Length).SequenceEqual(existing))
                return new(KeybindingConflict.ExtensionOfExisting, b, candidate);
        }
        return null;
    }

    private static uint[] Keycodes(IReadOnlyList<Key> chord) => chord.Select(k => (uint)k.KeyCode).ToArray();

    public string Message(Func<string, string> labelOf)
    {
        var existing = $"\"{labelOf(Existing.CommandId)}\"";
        if (IsWarning)
        {
            return Candidate.Scope == CommandScope.Global
                ? $"{Existing.Display} is {existing}\nin {Existing.Scope}, which wins while {FocusName(Existing.Scope)}\nhas focus. Bind it globally anyway?"
                : $"{Existing.Display} is {existing}\nglobally. The new binding wins while\n{FocusName(Candidate.Scope)} has focus. Bind it anyway?";
        }
        return Kind == KeybindingConflict.ExactMatch
            ? $"{Candidate.Display} is already {existing}\n{Where(Existing.Scope)}. Replace it?"
            : $"{Candidate.Display} clashes with {Existing.Display}\n({existing}) {Where(Existing.Scope)}.\nRemove that binding first.";
    }

    private static string Where(CommandScope scope) => scope == CommandScope.Global ? "globally" : $"in {scope}";

    private static string FocusName(CommandScope scope) => scope switch
    {
        CommandScope.Editor => "the editor",
        CommandScope.Explorer => "the explorer",
        CommandScope.Find => "the Find sidebar",
        CommandScope.Diff => "a diff tab",
        _ => "anything",
    };
}
