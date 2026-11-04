using System.Collections.Concurrent;
using TuiCode.Syntax;
using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

public class TypeSearchTests
{
    private readonly Lock _gate = new();
    private readonly List<(string Query, string[] Names, bool Done)> _found = [];

    [Fact]
    public async Task Every_servers_types_are_merged()
    {
        var search = Search(Answers("Widget"), Answers("Gadget"));

        search.Search("g");

        var (query, names, _) = await Settled();
        Assert.Equal("g", query);
        Assert.Equal(["Gadget", "Widget"], names.Order());
    }

    [Fact]
    public async Task A_server_that_fails_or_times_out_leaves_the_others_types_showing()
    {
        var never = new TaskCompletionSource<IReadOnlyList<TypeSymbol>>();
        var search = Search((_, _) => throw new JsonRpcException(-32603, "boom"), (_, _) => never.Task, Answers("Widget"));

        search.Search("w");

        Assert.Equal(["Widget"], (await Settled()).Names);
    }

    [Fact]
    public async Task A_server_that_times_out_is_asked_to_cancel_the_request()
    {
        var token = CancellationToken.None;
        var search = Search((_, cancel) =>
        {
            token = cancel;
            return new TaskCompletionSource<IReadOnlyList<TypeSymbol>>().Task;
        });

        search.Search("w");
        await Settled();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task Typing_while_a_search_runs_asks_only_the_last_query_next_and_drops_the_older_answer()
    {
        var asked = new ConcurrentQueue<string>();
        var held = new ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<TypeSymbol>>>();
        var search = Search((query, _) =>
        {
            asked.Enqueue(query);
            return held.GetOrAdd(query, _ => new()).Task;
        });

        search.Search("W");
        search.Search("Wi");
        search.Search("Wid");
        held["W"].SetResult([Type("Window")]);
        await Until(() => held.ContainsKey("Wid"));
        held["Wid"].SetResult([Type("Widget")]);

        var settled = await Settled();
        Assert.Equal(["W", "Wid"], asked);
        Assert.Equal("Wid", settled.Query);
        Assert.Equal(["Widget"], settled.Names);
        lock (_gate) Assert.DoesNotContain(_found, f => f.Query == "W");
    }

    [Fact]
    public async Task Once_stopped_an_answer_still_to_come_is_dropped()
    {
        var held = new TaskCompletionSource<IReadOnlyList<TypeSymbol>>();
        var search = Search((_, _) => held.Task);

        search.Search("w");
        search.Stop();
        held.SetResult([Type("Widget")]);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        lock (_gate) Assert.Empty(_found);
    }

    private TypeSearch Search(params Func<string, CancellationToken, Task<IReadOnlyList<TypeSymbol>>>[] sources)
    {
        var search = new TypeSearch(() => sources, action =>
        {
            lock (_gate) action();
        }) { Timeout = TimeSpan.FromMilliseconds(200) };
        search.Found += (query, types, done) => _found.Add((query, [.. types.Select(t => t.Name)], done));
        return search;
    }

    private static Func<string, CancellationToken, Task<IReadOnlyList<TypeSymbol>>> Answers(params string[] names) =>
        (_, _) => Task.FromResult<IReadOnlyList<TypeSymbol>>([.. names.Select(Type)]);

    private static TypeSymbol Type(string name) => new(name, SymbolKind.Class, null, new SourceLocation("/work/A.cs", 0, 0));

    private async Task<(string Query, string[] Names, bool Done)> Settled()
    {
        await Until(() =>
        {
            lock (_gate) return _found.Any(f => f.Done);
        });
        lock (_gate) return _found.Single(f => f.Done);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }
}
