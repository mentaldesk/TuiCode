using System.Text.Json.Nodes;
using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

public class LspLocationsTests
{
    private static readonly string A = Path.GetFullPath("/work/A.cs");
    private static readonly string B = Path.GetFullPath("/work/B.cs");

    [Fact]
    public void A_single_location_is_one_place()
    {
        var result = Parse("""{"uri":"URI_A","range":{"start":{"line":4,"character":7},"end":{"line":4,"character":9}}}""");

        Assert.Equal([new SourceLocation(A, 4, 7)], LspLocations.Parse(result));
    }

    [Fact]
    public void Several_locations_keep_their_order_and_lose_duplicates()
    {
        var result = Parse("""
            [
              {"uri":"URI_B","range":{"start":{"line":1,"character":2},"end":{"line":1,"character":3}}},
              {"uri":"URI_A","range":{"start":{"line":3,"character":0},"end":{"line":3,"character":1}}},
              {"uri":"URI_B","range":{"start":{"line":1,"character":2},"end":{"line":1,"character":3}}}
            ]
            """);

        Assert.Equal([new SourceLocation(B, 1, 2), new SourceLocation(A, 3, 0)], LspLocations.Parse(result));
    }

    [Fact]
    public void A_location_link_lands_on_its_selection_range()
    {
        var result = Parse("""
            [{"targetUri":"URI_A",
              "targetRange":{"start":{"line":10,"character":0},"end":{"line":20,"character":1}},
              "targetSelectionRange":{"start":{"line":12,"character":16},"end":{"line":12,"character":21}}}]
            """);

        Assert.Equal([new SourceLocation(A, 12, 16)], LspLocations.Parse(result));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""[{"uri":"csharp:/metadata/System.Console.cs","range":{"start":{"line":0,"character":0},"end":{"line":0,"character":0}}}]""")]
    public void Nothing_on_disk_is_no_places(string json) =>
        Assert.Empty(LspLocations.Parse(JsonNode.Parse(json)));

    [Fact]
    public void A_path_survives_the_round_trip_through_a_uri()
    {
        var path = Path.GetFullPath("/work/with space/C#.cs");

        Assert.Equal(path, LspLocations.ToPath(LspLocations.ToUri(path)));
    }

    private static JsonNode? Parse(string json) =>
        JsonNode.Parse(json.Replace("URI_A", LspLocations.ToUri(A)).Replace("URI_B", LspLocations.ToUri(B)));
}
