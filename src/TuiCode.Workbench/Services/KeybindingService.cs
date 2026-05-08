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

    public void Bind(string keySequence, string commandId)
    {
        ArgumentException.ThrowIfNullOrEmpty(keySequence);
        ArgumentException.ThrowIfNullOrEmpty(commandId);

        var keys = ParseSequence(keySequence);
        var node = _root;
        foreach (var key in keys)
        {
            if (!node.Children.TryGetValue(key, out var child))
            {
                child = new ChordNode();
                node.Children[key] = child;
            }
            node = child;
        }
        node.CommandId = commandId;
    }

    public KeyHandlingResult Handle(Key key)
    {
        // Esc cancels an in-flight chord and is consumed silently.
        if (_current != _root && IsEscape(key))
        {
            ResetChord();
            return KeyHandlingResult.Consumed;
        }

        if (!_current.Children.TryGetValue(key, out var next))
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

        _chordSoFar.Add(key);

        if (next.CommandId is not null)
        {
            var commandId = next.CommandId;
            ResetChord();
            _commands.TryExecute(commandId);
            return KeyHandlingResult.Consumed;
        }

        // Prefix match — descend and wait for the next key.
        _current = next;
        SetChordDisplay(string.Join(" ", _chordSoFar.Select(k => k.ToString())));
        return KeyHandlingResult.ChordInProgress;
    }

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
