namespace TuiCode.Workbench.Usages;

public enum UsageLookupState { Looking, Stopped, Failed, Found }

/// <summary>One look for usages: of the symbol <c>gu</c> was on, or of the definition a usage row is in.</summary>
public sealed class UsageLookup
{
    private readonly CancellationTokenSource _cts = new();

    internal UsageLookup(string name, UsageLineNode? from, int generation)
    {
        Name = name;
        From = from;
        Generation = generation;
    }

    public string Name { get; }

    /// <summary>The row being followed, or null for <c>gu</c>'s own list.</summary>
    public UsageLineNode? From { get; }

    public UsageLookupState State { get; internal set; }

    public CancellationToken Token => _cts.Token;

    internal int Generation { get; }

    internal void Stop()
    {
        State = UsageLookupState.Stopped;
        _cts.Cancel();
    }
}
