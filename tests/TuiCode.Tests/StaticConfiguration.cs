using Terminal.Gui.Configuration;

namespace TuiCode.Tests;

// xUnit serialises all classes sharing a collection. This definition is the one
// home for the rule; DisableParallelization keeps the collection from running
// against itself too. See issue #77.
[CollectionDefinition("StaticConfiguration", DisableParallelization = true)]
public sealed class StaticConfigurationCollection { }

/// <summary>
/// Base for any test that boots a Terminal.Gui <c>Application</c> (e.g. news up a
/// <see cref="TuiCode.Workbench.WorkbenchHost"/>) or mutates <c>ThemeManager</c> /
/// <c>ConfigurationManager</c>. Those are process-global TG statics and TG's render
/// path throws <c>KeyNotFoundException</c> if the theme dictionary is mutated by a
/// parallel test (issue #77).
///
/// Deriving does two things: (a) joins the serialised "StaticConfiguration"
/// collection so these tests never overlap, and (b) snapshot/restores
/// <c>ThemeManager.Theme</c> so a test that changes the theme can't leak into the next.
///
/// Rule of thumb: if your test news up a <c>WorkbenchHost</c>, renders a View, or
/// touches <c>ThemeManager</c>/<c>ConfigurationManager</c>, derive from this.
/// </summary>
[Collection("StaticConfiguration")]
public abstract class StaticConfigurationTest : IDisposable
{
    // Belt-and-suspenders for the collection wiring: if two serialised tests ever
    // run concurrently (e.g. a renamed collection no longer matches), trip loudly
    // and name the fix instead of failing intermittently somewhere in TG's renderer.
    private static int _live;

    private readonly string _theme;

    protected StaticConfigurationTest()
    {
        if (Interlocked.Increment(ref _live) > 1)
            throw new InvalidOperationException(
                "Two StaticConfiguration tests ran concurrently — the serialised collection " +
                "is not actually serialising. Check that every Application/ThemeManager test " +
                "derives from StaticConfigurationTest and that the [CollectionDefinition] name " +
                "still matches. See issue #77.");

        _theme = ThemeManager.Theme;
    }

    public virtual void Dispose()
    {
        ThemeManager.Theme = _theme;
        Interlocked.Decrement(ref _live);
        GC.SuppressFinalize(this);
    }
}
