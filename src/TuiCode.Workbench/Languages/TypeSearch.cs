namespace TuiCode.Workbench.Languages;

/// <summary>
/// Go to type's searches over every ready server. One runs at a time: a query typed while one runs waits, and only
/// the last of those runs next, so typing never piles requests up. A server that fails or takes longer than
/// <see cref="Timeout"/> counts as finding nothing.
/// </summary>
public sealed class TypeSearch
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly Func<IReadOnlyList<Func<string, Task<IReadOnlyList<TypeSymbol>>>>> _sources;
    private readonly Action<Action> _post;
    private string? _running;
    private string? _next;
    private bool _stopped;

    /// <param name="sources">The servers to ask, read afresh for every search.</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    public TypeSearch(Func<IReadOnlyList<Func<string, Task<IReadOnlyList<TypeSymbol>>>>> sources, Action<Action> post)
    {
        _sources = sources;
        _post = post;
    }

    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>What a query has found so far, raised on the UI thread as each server answers; <c>done</c> once they all have.</summary>
    public event Action<string, IReadOnlyList<TypeSymbol>, bool>? Found;

    public void Search(string query)
    {
        if (_stopped) return;
        if (_running is not null)
        {
            _next = query;
            return;
        }
        Start(query);
    }

    /// <summary>No more searches start, and the answers still to come are dropped.</summary>
    public void Stop() => _stopped = true;

    private void Start(string query)
    {
        _running = query;
        _next = null;
        var sources = _sources();
        var found = new List<TypeSymbol>();
        var pending = sources.Count;
        if (pending == 0)
        {
            Finish(query, found);
            return;
        }
        foreach (var source in sources)
            Ask(source, query).ContinueWith(answer => _post(() =>
            {
                found.AddRange(answer.Result);
                if (--pending == 0) Finish(query, found);
                else if (!_stopped && _next is null) Found?.Invoke(query, [.. found], false);
            }), TaskScheduler.Default);
    }

    private void Finish(string query, List<TypeSymbol> found)
    {
        _running = null;
        if (_stopped) return;
        if (_next is { } next && next != query)
        {
            Start(next);
            return;
        }
        _next = null;
        Found?.Invoke(query, [.. found], true);
    }

    private async Task<IReadOnlyList<TypeSymbol>> Ask(Func<string, Task<IReadOnlyList<TypeSymbol>>> source, string query)
    {
        try
        {
            return await source(query).WaitAsync(Timeout).ConfigureAwait(false);
        }
        catch (Exception e) when (e is TimeoutException or JsonRpcException or IOException or ObjectDisposedException)
        {
            return [];
        }
    }
}
