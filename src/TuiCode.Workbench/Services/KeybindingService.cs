using TuiCode.Abstractions;

namespace TuiCode.Workbench.Services;

public sealed class KeybindingService : IKeybindingService
{
    private readonly ICommandService _commands;
    private readonly Dictionary<CommandScope, ChordNode> _roots = new();
    // Where the chord in flight has got to, in the scope it started in; null when idle.
    private ChordNode? _current;
    private readonly List<Key> _chordSoFar = new();

    public KeybindingService(ICommandService commands)
    {
        _commands = commands;
    }

    public string? CurrentChord { get; private set; }
    public event EventHandler<string?>? ChordChanged;

    public Func<CommandScope> FocusedScope { get; set; } = () => CommandScope.Global;

    public void Bind(string keySequence, string commandId) =>
        Bind(ParseSequence(keySequence), commandId);

    public void Bind(IReadOnlyList<Key> chord, string commandId)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (chord.Count == 0) throw new ArgumentException("A chord needs at least one key.", nameof(chord));
        ArgumentException.ThrowIfNullOrEmpty(commandId);

        var node = Root(_commands.ScopeOf(commandId));
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

    public bool Unbind(string keySequence, CommandScope scope = CommandScope.Global) =>
        Unbind(ParseSequence(keySequence), scope);

    public bool Unbind(IReadOnlyList<Key> keys, CommandScope scope = CommandScope.Global)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0) throw new ArgumentException("A chord needs at least one key.", nameof(keys));

        if (!_roots.TryGetValue(scope, out var node)) return false;
        var path = new List<(ChordNode parent, Key key, ChordNode child)>(keys.Count);
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
        _roots.Clear();
        ResetChord();
    }

    public KeybindingConflict? CheckConflict(string keySequence, CommandScope scope = CommandScope.Global) =>
        CheckConflict(ParseSequence(keySequence), scope);

    public KeybindingConflict? CheckConflict(IReadOnlyList<Key> keys, CommandScope scope = CommandScope.Global)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0) throw new ArgumentException("A chord needs at least one key.", nameof(keys));

        if (!_roots.TryGetValue(scope, out var node)) return null;
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
            foreach (var (scope, root) in _roots)
            foreach (var binding in Walk(scope, root, stack))
                yield return binding;
        }
    }

    private static IEnumerable<KeyBinding> Walk(CommandScope scope, ChordNode node, List<Key> stack)
    {
        if (node.CommandId is not null)
            yield return new KeyBinding(stack.ToArray(), node.CommandId, scope);

        foreach (var (key, child) in node.Children)
        {
            stack.Add(key);
            foreach (var b in Walk(scope, child, stack))
                yield return b;
            stack.RemoveAt(stack.Count - 1);
        }
    }

    public KeyHandlingResult Handle(Key key)
    {
        var normalized = Normalize(key);

        if (_current is not null)
            return Continue(normalized);

        var focused = FocusedScope();
        if (focused != CommandScope.Global && Start(focused, normalized) is { } result)
            return result;
        return Start(CommandScope.Global, normalized) ?? KeyHandlingResult.Pass;
    }

    // The first key of a chord, looked up in one scope. Null when that scope has nothing enabled for it.
    private KeyHandlingResult? Start(CommandScope scope, Key key)
    {
        if (!_roots.TryGetValue(scope, out var root) || !root.Children.TryGetValue(key, out var next))
            return null;

        if (next.CommandId is not null)
        {
            if (!_commands.IsEnabled(next.CommandId)) return null;
            _commands.TryExecute(next.CommandId);
            return KeyHandlingResult.Consumed;
        }

        return Descend(key, next);
    }

    // A later key of the chord in flight, which always finishes in the scope it started in.
    private KeyHandlingResult Continue(Key key)
    {
        // Esc cancels an in-flight chord, and a stray key abandons it; both are consumed silently, as in VS Code.
        if (IsEscape(key) || !_current!.Children.TryGetValue(key, out var next))
        {
            ResetChord();
            return KeyHandlingResult.Consumed;
        }

        if (next.CommandId is not null)
        {
            var commandId = next.CommandId;
            ResetChord();
            if (_commands.IsEnabled(commandId))
                _commands.TryExecute(commandId);
            return KeyHandlingResult.Consumed;
        }

        return Descend(key, next);
    }

    private KeyHandlingResult Descend(Key key, ChordNode next)
    {
        _chordSoFar.Add(key);
        _current = next;
        SetChordDisplay(KeyChord.Display(_chordSoFar));
        return KeyHandlingResult.ChordInProgress;
    }

    private ChordNode Root(CommandScope scope)
    {
        if (!_roots.TryGetValue(scope, out var root))
            _roots[scope] = root = new ChordNode();
        return root;
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

    private void ResetChord()
    {
        _current = null;
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
