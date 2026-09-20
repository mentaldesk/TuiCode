using System.Globalization;

namespace TuiCode.Abstractions;

/// <summary>
/// Helpers for the two faces of a key chord (issue #89):
/// <list type="bullet">
/// <item><see cref="Canonical"/> — the raw <see cref="KeyCode"/> of each step, joined. This is the
/// chord's <em>identity</em>: what we bind, match, diff, and persist on. Keycodes are integers,
/// so the join never collides with a chord step's own characters.</item>
/// <item><see cref="Display(IReadOnlyList{Key})"/> — <c>Key.ToString()</c> of each step, with the cursor keys
/// as arrows. This is for the UI only and is <em>never parsed back</em>. It can be lossy (e.g. a
/// three-modifier <c>Ctrl+Alt+Shift++</c> stringifies to something <c>Key.TryParse</c> can't read), which
/// is exactly why it can't be identity.</item>
/// </list>
/// </summary>
public static class KeyChord
{
    private static readonly (string Name, string Symbol)[] Arrows =
    [
        ("CursorUp", "↑"),
        ("CursorDown", "↓"),
        ("CursorLeft", "←"),
        ("CursorRight", "→"),
    ];

    /// <summary>Canonical, parse-stable identity: each step's raw keycode, space-joined.</summary>
    public static string Canonical(IReadOnlyList<Key> chord) =>
        string.Join(' ', chord.Select(k => ((uint)k.KeyCode).ToString(CultureInfo.InvariantCulture)));

    /// <summary>Human-readable label for the UI. Display-only — do not parse it back into keys.</summary>
    public static string Display(IReadOnlyList<Key> chord) =>
        string.Join(' ', chord.Select(Display));

    /// <summary>Human-readable label for one key, e.g. <c>Alt+↓</c> (#194). Display-only.</summary>
    public static string Display(Key key)
    {
        var text = key.ToString();
        foreach (var (name, symbol) in Arrows)
        {
            if (string.Equals(text, name, StringComparison.Ordinal)) return symbol;
            if (text.EndsWith($"+{name}", StringComparison.Ordinal))
                return string.Concat(text.AsSpan(0, text.Length - name.Length), symbol);
        }
        return text;
    }
}
