using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace EDC.Companion;

/// <summary>The planets an explorer comes for, the most precious first.</summary>
public enum PlanetKind { EarthLike, Ammonia, Water, Terraformable }

/// <summary>
/// An Earth-like, ammonia or water world or a terraformable planet of the current system. <paramref name="FirstDiscovery"/>
/// when the journal's scan says nobody had found it before. <paramref name="Value"/>, what mapping it well pays.
/// </summary>
public sealed record NotableBody(string Name, PlanetKind Kind, bool Terraformable, double Distance, bool FirstDiscovery = false, long Value = 0)
{
    public string Label => Kind switch
    {
        PlanetKind.EarthLike => Resources.Strings.PlanetEarthLike,
        PlanetKind.Ammonia => Resources.Strings.PlanetAmmonia,
        PlanetKind.Water => Terraformable ? Resources.Strings.PlanetWaterTerraformable : Resources.Strings.PlanetWater,
        _ => Resources.Strings.PlanetTerraformable,
    };
}

/// <summary>A planet of the current system as the journal scanned it: whether mapping it is a first.</summary>
public sealed record ScannedBody(string Name, bool FirstMapping);

/// <summary>
/// Those planets, from the journal's scans as they come, from EDSM for a system already known, and from
/// Spansh for the systems of the route.
/// </summary>
public static class NotableBodies
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static PlanetKind? Kind(string? planetClass, bool terraformable) => planetClass switch
    {
        "Earthlike body" or "Earth-like world" => PlanetKind.EarthLike,
        "Ammonia world" => PlanetKind.Ammonia,
        "Water world" => PlanetKind.Water,
        _ when terraformable => PlanetKind.Terraformable,
        // Not the gas giants: too long to map for what they pay.
        _ => null,
    };

    /// <summary>A journal Scan event (discovery scanner, FSS, arrival nearby): a body worth noting, or null.</summary>
    public static NotableBody? FromScan(JsonElement e)
    {
        var terraformable = GameState.Str(e, "TerraformState") == "Terraformable";
        if (GameState.Str(e, "BodyName") is not { } name || GameState.Str(e, "PlanetClass") is not { } planetClass || Kind(planetClass, terraformable) is not { } kind)
            return null;

        var firstDiscovery = GameState.Bool(e, "WasDiscovered") == false;
        return new NotableBody(name, kind, terraformable, GameState.Dbl(e, "DistanceFromArrivalLS") ?? 0, firstDiscovery,
            ExplorationValues.Mapped(planetClass, terraformable, GameState.Dbl(e, "MassEM") ?? 0, firstDiscovery, GameState.Bool(e, "WasMapped") == false));
    }

    /// <summary>The same event for a planet; null for a star or a belt cluster, which the surface scanner does not map.</summary>
    public static ScannedBody? Scanned(JsonElement e) =>
        GameState.Str(e, "BodyName") is { } name && GameState.Str(e, "PlanetClass") is not null
            ? new ScannedBody(name, GameState.Bool(e, "WasMapped") == false)
            : null;

    /// <summary>Spansh holds the bodies EDDN relayed: asked for a whole route at once, by chunks.</summary>
    private const int RouteChunk = 100;

    /// <summary>
    /// The notable planets of the route's systems Spansh knows, by system address: one request for the
    /// classes, one for the terraformables, as its filters only combine with « and ».
    /// </summary>
    public static async Task<Dictionary<long, List<NotableBody>>> FetchRouteAsync(IReadOnlyList<long> addresses, CancellationToken cancel = default)
    {
        var found = new Dictionary<long, List<NotableBody>>();
        foreach (var chunk in addresses.Distinct().Chunk(RouteChunk))
        {
            var ids = chunk.Select(a => a.ToString(CultureInfo.InvariantCulture)).ToArray();
            foreach (var filter in new object[]
                     {
                         new { subtype = new { value = new[] { "Earth-like world", "Ammonia world", "Water world" } } },
                         new { terraforming_state = new { value = new[] { "Terraformable" } } },
                     })
            {
                var filters = JsonSerializer.SerializeToNode(filter)!.AsObject();
                filters["system_id64"] = JsonSerializer.SerializeToNode(new { value = ids });
                var body = JsonSerializer.Serialize(new { filters, sort = Array.Empty<object>(), size = 500, page = 0 });
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync("https://spansh.co.uk/api/bodies/search", content, cancel);
                response.EnsureSuccessStatusCode();
                foreach (var (address, notable) in ParseSpansh(await response.Content.ReadAsStringAsync(cancel)))
                {
                    var list = found.TryGetValue(address, out var known) ? known : found[address] = [];
                    if (!list.Any(b => b.Name == notable.Name))
                        list.Add(notable);
                }
            }
        }

        return found;
    }

    /// <summary>Spansh's api/bodies/search: each body with its system's address.</summary>
    public static IReadOnlyList<(long System, NotableBody Body)> ParseSpansh(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return [];

        var bodies = new List<(long, NotableBody)>();
        foreach (var b in results.EnumerateArray())
        {
            // An id64 may pass 2^53: read as a whole number, not through a double.
            var terraformable = GameState.Str(b, "terraforming_state") == "Terraformable";
            if (b.TryGetProperty("system_id64", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var system)
                && GameState.Str(b, "name") is { } name && GameState.Str(b, "subtype") is { } subtype && Kind(subtype, terraformable) is { } kind)
                bodies.Add((system, new NotableBody(name, kind, terraformable, GameState.Dbl(b, "distance_to_arrival") ?? 0,
                    Value: ExplorationValues.Mapped(subtype, terraformable, GameState.Dbl(b, "earth_masses") ?? 0, false, false))));
        }

        return bodies;
    }

    public static async Task<IReadOnlyList<NotableBody>> FetchAsync(long address, CancellationToken cancel = default) =>
        Parse(await Http.GetStringAsync($"https://www.edsm.net/api-system-v1/bodies?systemId64={address}", cancel));

    /// <summary>EDSM's api-system-v1/bodies: the bodies players sent it, with their type.</summary>
    public static IReadOnlyList<NotableBody> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("bodies", out var bodies) || bodies.ValueKind != JsonValueKind.Array)
            return [];

        return bodies.EnumerateArray()
            .Select(b => (Name: GameState.Str(b, "name"), Terraformable: GameState.Str(b, "terraformingState") == "Candidate for terraforming", Body: b))
            .Select(b => b.Name is not null && GameState.Str(b.Body, "subType") is { } subType && Kind(subType, b.Terraformable) is { } kind
                ? new NotableBody(b.Name, kind, b.Terraformable, GameState.Dbl(b.Body, "distanceToArrival") ?? 0,
                    Value: ExplorationValues.Mapped(subType, b.Terraformable, GameState.Dbl(b.Body, "earthMasses") ?? 0, false, false))
                : null)
            .OfType<NotableBody>()
            .ToList();
    }
}
