using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Terminal.Gui.Drivers;
using Point = System.Drawing.Point;

namespace TuiCode.Editor;

/// <summary>
/// Shows the focused editor's secondary carets as real terminal cursors, in terminals that support kitty's
/// multiple cursors protocol (https://sw.kovidgoyal.net/kitty/multiple-cursors-protocol/). Elsewhere the editor paints them itself.
/// </summary>
public sealed partial class TerminalCursors : IDisposable
{
    private const string ClearAll = "\e[>0;4 q";

    private static readonly ConditionalWeakTable<IApplication, TerminalCursors> ByApp = new();

    private readonly IApplication _app;
    private readonly Action<string> _write;
    private string _shown = "";

    public TerminalCursors(IApplication app) : this(app, sequence => app.Driver?.WriteRaw(sequence))
    {
    }

    internal TerminalCursors(IApplication app, Action<string> write)
    {
        _app = app;
        _write = write;
        ByApp.AddOrUpdate(app, this);
        app.LayoutAndDrawComplete += OnLayoutAndDrawComplete;
    }

    public bool IsSupported { get; private set; }

    internal static bool IsSupportedBy(IApplication? app) =>
        app is not null && ByApp.TryGetValue(app, out var cursors) && cursors.IsSupported;

    /// <summary>Asks the terminal whether it supports the protocol. Terminals that don't answer are dropped after a second.</summary>
    public void Detect() =>
        _app.Driver?.QueueAnsiRequest(new AnsiEscapeSequenceRequest
        {
            Request = "\e[> q",
            Terminator = "q",
            ResponseReceived = response => Supported(IsFollowMainCursorShapeSupported(response)),
        });

    internal void Supported(bool supported)
    {
        IsSupported = supported;
        _app.Navigation?.GetFocused()?.SetNeedsDraw();
    }

    // The reply lists the supported shapes and operations; 29 draws extra cursors in the main cursor's shape.
    internal static bool IsFollowMainCursorShapeSupported(string? response) =>
        response is not null && Reply().Match(response) is { Success: true } match
                             && match.Groups[1].Value.Split(';').Contains("29");

    [GeneratedRegex(@"\[>([\d;]*) q$")]
    private static partial Regex Reply();

    private void OnLayoutAndDrawComplete(object? sender, EventArgs e) => Update();

    internal void Update()
    {
        var positions = IsSupported && _app.Navigation?.GetFocused() is EditorTextView view
            ? view.SecondaryCaretsOnScreen().ToArray()
            : [];
        var shown = positions.Length == 0
            ? ""
            : $"\e[>29;2:{string.Join(':', positions.Select(p => $"{p.Y + 1}:{p.X + 1}"))} q";
        if (shown == _shown) return;
        _write(ClearAll + shown);
        _shown = shown;
    }

    public void Dispose()
    {
        _app.LayoutAndDrawComplete -= OnLayoutAndDrawComplete;
        ByApp.Remove(_app);
    }
}
