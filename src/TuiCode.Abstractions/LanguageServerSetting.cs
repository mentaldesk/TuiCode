namespace TuiCode.Abstractions;

/// <summary>The language server chosen for a language (#456). An empty <see cref="Command"/> runs none.</summary>
public sealed record LanguageServerSetting(string Command, IReadOnlyList<string> Arguments)
{
    public static LanguageServerSetting None { get; } = new("", []);

    public bool IsNone => Command.Length == 0;

    public bool Equals(LanguageServerSetting? other) =>
        other is not null && Command == other.Command && Arguments.SequenceEqual(other.Arguments);

    public override int GetHashCode() => HashCode.Combine(Command, Arguments.Count);
}
