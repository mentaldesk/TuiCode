using System.Text.Json.Nodes;

namespace TuiCode.Workbench.Languages;

/// <summary>csharp-ls's answer to <c>csharp/metadata</c>: a library type's decompiled source and the assembly it's from.</summary>
public sealed record DecompiledSource(string AssemblyName, string SymbolName, string Source)
{
    public static DecompiledSource? Parse(JsonNode? result) =>
        result is JsonObject answer
        && answer["source"]?.GetValue<string>() is { } source
        && answer["symbolName"]?.GetValue<string>() is { } symbol
            ? new DecompiledSource(answer["assemblyName"]?.GetValue<string>() ?? symbol, symbol, source)
            : null;
}
