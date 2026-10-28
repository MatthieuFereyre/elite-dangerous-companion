using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace EDC.Companion;

/// <summary>A station with all the services, to copy into the galaxy map.</summary>
public sealed record StationSpot(string Station, string System, double Distance, double? ArrivalLs, string Type, bool LargePad);

/// <summary>
/// The nearest station offering everything a commander comes back for, from Spansh. The material
/// trader is left out, as asked; so are the black market and Pioneer Supplies, which push the answer
/// hundreds of light years away (724 ly from The Colony with Pioneer Supplies). Carriers move, and
/// settlements have little: neither counts.
/// </summary>
public static class StationFinder
{
    public static readonly IReadOnlyList<string> Services =
    [
        "Refuel", "Repair", "Restock", "Market", "Outfitting", "Shipyard", "Universal Cartographics",
        "Vista Genomics", "Interstellar Factors Contact", "Search and Rescue",
    ];

    private static readonly string[] Types =
    [
        "Asteroid base", "Coriolis Starport", "Dockable Planet Station", "Dodec Starport", "Ocellus Starport",
        "Orbis Starport", "Outpost", "Planetary Outpost", "Planetary Port",
    ];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<StationSpot?> FindAsync(string system, CancellationToken cancel = default)
    {
        using var response = await Http.PostAsync("https://spansh.co.uk/api/stations/search",
            new StringContent(Query(system), Encoding.UTF8, "application/json"), cancel);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancel));
    }

    /// <summary>Each service as an object of its own: Spansh then wants them all, where a list of values takes any.</summary>
    internal static string Query(string system) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["filters"] = new Dictionary<string, object>
        {
            ["services"] = Services.Select(s => new { name = s }).ToArray(),
            ["type"] = new { value = Types },
        },
        ["sort"] = new[] { new { distance = new { direction = "asc" } } },
        ["size"] = 1,
        ["page"] = 0,
        ["reference_system"] = system,
    });

    internal static StationSpot? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return null;

        var s = results[0];
        return new StationSpot(
            GameState.Str(s, "name") ?? "",
            GameState.Str(s, "system_name") ?? "",
            GameState.Dbl(s, "distance") ?? 0,
            GameState.Dbl(s, "distance_to_arrival"),
            GameState.Str(s, "type") ?? "",
            s.TryGetProperty("has_large_pad", out var large) && large.ValueKind == JsonValueKind.True);
    }
}
