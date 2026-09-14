using TuiCode.Abstractions;

namespace TuiCode.Workbench.Services;

public sealed class KeybindingService : IKeybindingService
{
    private readonly ICommandService _commands;
    private readonly ChordNode _root = new();
    private ChordNode _current;
    private readonly List<Key> _chordSoFar = new();

    public KeybindingService(ICommandService commands)
    {
        _commands = commands;
        _current = _root;
    }

    public string? CurrentChord { get; private set; }
    public event EventHandler<string?>? ChordChanged;

    public void Bind(string keySequence, string commandId) =>
        Bind(ParseSequence(keySequence), commandId);

    public void Bind(IReadOnlyList<Key> chord, string commandId)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (chord.Count == 0) throw new ArgumentException("A chord needs at least one key.", nameof(chord));
        ArgumentException.ThrowIfNullOrEmpty(commandId);

        var node = _root;
        foreach (var key in chord)
        {
            var normalized = Normalize(key);
            if (!node.Children.TryGetValue(normalized, out var child))
            {
                child = new ChordNode();
                node.Children[normalized] = child;
            }
            node = child;
        }
        node.CommandId = commandId;
    }

    public bool Unbind(string keySequence) => Unbind(ParseSequence(keySequence));

    public bool Unbind(IReadOnlyList<Key> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0) throw new ArgumentException("A chord needs at least one key.", nameof(keys));

        var path = new List<(ChordNode parent, Key key, ChordNode child)>(keys.Count);
        var node = _root;
        foreach (var key in keys)
        {
            var normalized = Normalize(key);
            if (!node.Children.TryGetValue(normalized, out var child))
                return false;
            path.Add((node, normalized, child));
            node = child;
        }

        if (node.CommandId is null) return false;
        node.CommandId = null;

        // Prune empty subtrees from the leaf upwards.
        for (var i = path.Count - 1; i >= 0; i--)
        {
            var (parent, key, child) = path[i];
            if (child.CommandId is not null || child.Children.Count > 0) break;
            parent.Children.Remove(key);
        }
        return true;
    }

    public void Reset()
    {
        _root.Children.Clear();
        _root.CommandId = null;
        ResetChord();
    }

    public KeybindingConflict? CheckConflict(string keySequence) =>
        CheckConflict(ParseSequence(keySequence));

    public KeybindingConflict? CheckConflict(IReadOnlyList<Key> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0) throw new ArgumentException("A chord needs at least one key.", nameof(keys));

        var node = _root;
        for (var i = 0; i < keys.Count; i++)
        {
            var normalized = Normalize(keys[i]);
            if (!node.Children.TryGetValue(normalized, out var child))
                return null; // path doesn't exist; no conflict

            // A shorter binding fires before this chord could complete.
            if (child.CommandId is not null && i < keys.Count - 1)
                return KeybindingConflict.ExtensionOfExisting;

            node = child;
        }

        // We reached the proposed terminal node.
        if (node.Children.Count > 0)
            return KeybindingConflict.PrefixOfExisting; // would shadow existing chords beneath it
        if (node.CommandId is not null)
            return KeybindingConflict.ExactMatch;
        return null;
    }

    public IEnumerable<KeyBinding> Bindings
    {
        get
        {
            var stack = new List<Key>();
            foreach (var binding in Walk(_root, stack))
                yield return binding;
        }
    }

    private static IEnumerable<KeyBinding> Walk(ChordNode node, List<Key> stack)
    {
        if (node.CommandId is not null)
            yield return new KeyBinding(stack.ToArray(), node.CommandId);

        foreach (var (key, child) in node.Children)
        {
            stack.Add(key);
            foreach (var b in Walk(child, stack))
                yield return b;
            stack.RemoveAt(stack.Count - 1);
        }
    }

    public KeyHandlingResult Handle(Key key)
    {
        var normalized = Normalize(key);

        // Esc cancels an in-flight chord and is consumed silently.
        if (_current != _root && IsEscape(normalized))
        {
            ResetChord();
            return KeyHandlingResult.Consumed;
        }

        if (!_current.Children.TryGetValue(normalized, out var next))
        {
            // Unknown key. If we were in a chord, abandon it (and consume — VS Code does
            // the same: a stray key during a chord doesn't reach the focused view).
            if (_current != _root)
            {
                ResetChord();
                return KeyHandlingResult.Consumed;
            }
            return KeyHandlingResult.Pass;
        }

        _chordSoFar.Add(normalized);

        if (next.CommandId is not null)
        {
            var commandId = next.CommandId;
            ResetChord();
            _commands.TryExecute(commandId);
            return KeyHandlingResult.Consumed;
        }

        // Prefix match — descend and wait for the next key.
        _current = next;
        SetChordDisplay(string.Join(" ", _chordSoFar.Select(FormatKey)));
        return KeyHandlingResult.ChordInProgress;
    }

    /// <summary>
    /// Collapse case for bare letters: typing "x" and "X" should both match a
    /// binding written as "X". TG distinguishes them (Shift bit + KeyCode), so
    /// without this any letter chord step would only fire for the exact case
    /// the user wrote in the binding string. Modifier-stacked letters (e.g.
    /// Ctrl+S) are already case-collapsed by TG itself; only Shift+letter with
    /// no other modifier needs to be normalized here.
    /// </summary>
    private static Key Normalize(Key key)
    {
        if (key.IsShift && !key.IsCtrl && !key.IsAlt)
        {
            var ch = (char)key.AsRune.Value;
            if (char.IsLetter(ch))
                return new Key(char.ToLowerInvariant(ch));
        }
        return key;
    }

    private static string FormatKey(Key key) => key.ToString();

    private void ResetChord()
    {
        _current = _root;
        _chordSoFar.Clear();
        SetChordDisplay(null);
    }

    private void SetChordDisplay(string? value)
    {
        if (CurrentChord == value) return;
        CurrentChord = value;
        ChordChanged?.Invoke(this, value);
    }

    private static bool IsEscape(Key key) =>
        Key.TryParse("Esc", out var esc) && key == esc;

    private static IReadOnlyList<Key> ParseSequence(string sequence)
    {
        var parts = sequence.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var keys = new Key[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!Key.TryParse(parts[i], out var k))
                throw new ArgumentException($"Invalid key '{parts[i]}' in sequence '{sequence}'", nameof(sequence));
            keys[i] = k;
        }
        return keys;
    }

    private sealed class ChordNode
    {
        public string? CommandId;
        public Dictionary<Key, ChordNode> Children { get; } = new();
    }
}
