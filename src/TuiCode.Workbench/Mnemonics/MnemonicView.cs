using System.Text;
using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Mnemonics;

/// <summary>
/// Modal mnemonic launcher (issue #50). Opened by the leader key (Ctrl+Space by default).
/// Lists every command that has a mnemonic; as the user types, the list filters to mnemonics
/// with the typed prefix and the command fires the instant the prefix is unambiguous — no Enter
/// needed (see <see cref="MnemonicResolver.ResolveExact"/>). Esc cancels; Backspace edits.
///
/// Unlike <see cref="Actions.ActionView"/> this captures raw keystrokes via a
/// <see cref="KeyCaptureScope"/> rather than a focused TextField. That keeps execution on the
/// app's key-dispatch path (the same place every other modal opens from) instead of inside a
/// TextChanged callback, so closing/disposing the view mid-keystroke is safe.
/// </summary>
public sealed class MnemonicView : Window
{
    private readonly MnemonicResolver _resolver;
    private readonly Action<string> _execute;
    private readonly KeyCaptureScope _scope;
    private readonly Label _header;
    private readonly ListView _list;

    private readonly StringBuilder _input = new();
    private List<MnemonicEntry> _visible;

    /// <summary>The input scope <see cref="WorkbenchHost"/> pushes on open and pops on close.</summary>
    public IKeybindingService Scope => _scope;

    /// <summary>Fired after the view wants to be removed (Esc, or after a command was dispatched).</summary>
    public event EventHandler? Closed;

    public MnemonicView(IEnumerable<MnemonicEntry> entries, Action<string> execute)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(execute);

        _resolver = new MnemonicResolver(entries);
        _execute = execute;
        _scope = new KeyCaptureScope(OnKey);

        Title = "Mnemonics";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 60;
        Height = 22;
        CanFocus = true;

        _header = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = 1,
        };

        _list = new ListView
        {
            X = 1,
            Y = Pos.Bottom(_header) + 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1),
            CanFocus = false,
        };

        Add(_header, _list);

        _visible = _resolver.All.ToList();
        RebuildVisible();
    }

    private void OnKey(Key key)
    {
        if (key == Key.Esc)
        {
            Closed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (key == Key.Backspace)
        {
            if (_input.Length > 0)
            {
                _input.Length--;
                RebuildVisible();
            }
            return;
        }

        // Enter commits the lone remaining match. Useful for a reserved family with a single
        // member today (e.g. "c" → "cf"): you needn't type the second key once it's unambiguous.
        if (key == Key.Enter)
        {
            if (_visible.Count == 1)
                Execute(_visible[0]);
            return;
        }

        if (!key.TryGetPrintableRune(out var rune))
            return;

        var candidate = _input.ToString() + char.ToLowerInvariant((char)rune.Value);

        // Reject a keystroke that no mnemonic could complete — keeps the entered prefix valid.
        if (_resolver.Matching(candidate).Count == 0)
            return;

        _input.Clear();
        _input.Append(candidate);

        if (_resolver.ResolveExact(candidate) is { } exact)
        {
            Execute(exact);
            return;
        }

        RebuildVisible();
    }

    private void Execute(MnemonicEntry entry)
    {
        // Close before dispatching so the command runs against a workbench without this modal,
        // mirroring ActionView. The Closed handler pops our scope and removes the view.
        Closed?.Invoke(this, EventArgs.Empty);
        _execute(entry.CommandId);
    }

    private void RebuildVisible()
    {
        var prefix = _input.ToString();
        _visible = _resolver.Matching(prefix).ToList();

        _header.Text = prefix.Length == 0
            ? "Type a mnemonic   (Esc to cancel)"
            : $"Mnemonic: {prefix}";

        var lines = _visible.Select(FormatRow).ToList();
        _list.Source = new ListWrapper<string>(new(lines));
        _list.SelectedItem = _visible.Count > 0 ? 0 : null;
    }

    private static string FormatRow(MnemonicEntry e) =>
        $"{e.Mnemonic.PadRight(6)}{e.Label}";
}
