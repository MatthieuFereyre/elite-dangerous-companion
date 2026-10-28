using System.Text.Json;
using EDC.Companion;
using Xunit;

namespace EDC.Tests;

public sealed class StationFinderTests
{
    [Fact]
    public void The_query_asks_every_service_but_the_material_trader_and_no_carrier()
    {
        using var query = JsonDocument.Parse(StationFinder.Query("Col 173 Sector OO-Q d5-13"));
        var root = query.RootElement;

        var services = root.GetProperty("filters").GetProperty("services").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToList();
        Assert.Contains("Shipyard", services);
        Assert.Contains("Restock", services);
        Assert.Contains("Vista Genomics", services);
        Assert.DoesNotContain("Material Trader", services);
        var types = root.GetProperty("filters").GetProperty("type").GetProperty("value").EnumerateArray().Select(t => t.GetString()).ToList();
        Assert.DoesNotContain("Drake-Class Carrier", types);
        Assert.Contains("Orbis Starport", types);
        Assert.Equal("Col 173 Sector OO-Q d5-13", root.GetProperty("reference_system").GetString());
    }

    [Fact]
    public void The_answer_gives_the_nearest_station_with_its_way_there()
    {
        const string answer = """
            { "count": 2080, "results": [
              { "name": "Antonov Depot", "system_name": "Col 173 Sector OO-Q d5-13", "distance": 0.0, "distance_to_arrival": 1532.4,
                "type": "Orbis Starport", "has_large_pad": true },
              { "name": "The Colony", "system_name": "Vela Dark Region DQ-Y c29", "distance": 117.9, "distance_to_arrival": 1498.3,
                "type": "Dodec Starport", "has_large_pad": true } ] }
            """;

        var station = StationFinder.Parse(answer);

        Assert.Equal(new StationSpot("Antonov Depot", "Col 173 Sector OO-Q d5-13", 0, 1532.4, "Orbis Starport", true), station);
        Assert.Null(StationFinder.Parse("""{ "count": 0, "results": [] }"""));
    }
}
