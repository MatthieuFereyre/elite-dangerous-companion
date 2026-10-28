using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using EDC.Resources;

namespace EDC.Companion;

/// <summary>The classes of planet, as the journal, EDSM and Spansh name them each in their way.</summary>
public enum PlanetClass { EarthLike, Water, Ammonia, WaterGiant, GasGiantLife, HighMetal, MetalRich, Rocky, RockyIce, Icy, GasGiant, HeliumGiant }

/// <summary>
/// A planet of the current system, or of the route, with what mapping it efficiently pays.
/// <paramref name="FirstDiscovery"/> and <paramref name="FirstMapping"/> when the journal's scan says nobody
/// had found, or mapped, it before; EDSM and Spansh only know bodies already found. <paramref name="Value"/>
/// is null when the mass is not known: the planet may pay above any threshold.
/// </summary>
public sealed record NotableBody(string Name, PlanetClass Class, bool Terraformable, double Distance, bool FirstDiscovery = false,
    long? Value = null, bool FirstMapping = false)
{
    public string Label
    {
        get
        {
            var name = Class switch
            {
                PlanetClass.EarthLike => Strings.PlanetEarthLike,
                PlanetClass.Water => Strings.PlanetWater,
                PlanetClass.Ammonia => Strings.PlanetAmmonia,
                PlanetClass.WaterGiant => Strings.PlanetWaterGiant,
                PlanetClass.GasGiantLife => Strings.PlanetGasGiantLife,
                PlanetClass.HighMetal => Strings.PlanetHighMetal,
                PlanetClass.MetalRich => Strings.PlanetMetalRich,
                PlanetClass.Rocky => Strings.PlanetRocky,
                PlanetClass.RockyIce => Strings.PlanetRockyIce,
                PlanetClass.Icy => Strings.PlanetIcy,
                PlanetClass.HeliumGiant => Strings.PlanetHeliumGiant,
                _ => Strings.PlanetGasGiant,
            };
            return Terraformable ? string.Format(Strings.PlanetTerraformableOf, name) : name;
        }
    }
}

/// <summary>A planet of the current system as the journal scanned it: whether mapping it is a first.</summary>
public sealed record ScannedBody(string Name, bool FirstMapping);

/// <summary>
/// The planets of a system with what mapping them pays: from the journal's scans as they come, from EDSM
/// for a system already known, and from Spansh for the systems of the route. Which are told is the
/// player's choice (<see cref="GameState.NotableBodies"/>).
/// </summary>
public static class NotableBodies
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>"Earthlike body", "Earth-like world", "Sudarsky class II gas giant", "Class II gas giant"…</summary>
    internal static PlanetClass? Classify(string? planetClass)
    {
        var c = planetClass?.ToLowerInvariant().Replace('-', ' ');
        return c switch
        {
            null => null,
            _ when c.Contains("earth") => PlanetClass.EarthLike,
            _ when c.Contains("water world") => PlanetClass.Water,
            _ when c.Contains("ammonia world") => PlanetClass.Ammonia,
            _ when c.Contains("water giant") => PlanetClass.WaterGiant,
            _ when c.Contains("based life") => PlanetClass.GasGiantLife,
            _ when c.Contains("helium") => PlanetClass.HeliumGiant,
            _ when c.Contains("gas giant") => PlanetClass.GasGiant,
            _ when c.Contains("high metal") => PlanetClass.HighMetal,
            _ when c.Contains("metal rich") => PlanetClass.MetalRich,
            _ when c.Contains("rocky ice") => PlanetClass.RockyIce,
            _ when c.Contains("rocky") => PlanetClass.Rocky,
            _ when c.Contains("icy") => PlanetClass.Icy,
            _ => null,
        };
    }

    /// <summary>A journal Scan event (discovery scanner, FSS, arrival nearby): the planet with its value, or null for a star or a belt.</summary>
    public static NotableBody? FromScan(JsonElement e)
    {
        var terraformable = GameState.Str(e, "TerraformState") == "Terraformable";
        if (GameState.Str(e, "BodyName") is not { } name || GameState.Str(e, "PlanetClass") is not { } planetClass || Classify(planetClass) is not { } kind)
            return null;

        var firstDiscovery = GameState.Bool(e, "WasDiscovered") == false;
        var firstMapping = GameState.Bool(e, "WasMapped") == false;
        return new NotableBody(name, kind, terraformable, GameState.Dbl(e, "DistanceFromArrivalLS") ?? 0, firstDiscovery,
            Value(planetClass, terraformable, GameState.Dbl(e, "MassEM"), firstDiscovery, firstMapping), firstMapping);
    }

    /// <summary>What mapping it efficiently pays; null without its mass.</summary>
    private static long? Value(string planetClass, bool terraformable, double? mass, bool firstDiscovery, bool firstMapping) =>
        mass is > 0 and var m ? ExplorationValues.Mapped(planetClass, terraformable, m, firstDiscovery, firstMapping) : null;

    /// <summary>The same event for a planet; null for a star or a belt cluster, which the surface scanner does not map.</summary>
    public static ScannedBody? Scanned(JsonElement e) =>
        GameState.Str(e, "BodyName") is { } name && GameState.Str(e, "PlanetClass") is not null
            ? new ScannedBody(name, GameState.Bool(e, "WasMapped") == false)
            : null;

    /// <summary>Spansh holds the bodies EDDN relayed: asked for a whole route at once, by chunks.</summary>
    private const int RouteChunk = 100;

    /// <summary>The planets of the route's systems Spansh knows, by system address, worth about <paramref name="minValue"/> or more.</summary>
    public static async Task<Dictionary<long, List<NotableBody>>> FetchRouteAsync(IReadOnlyList<long> addresses, long minValue, CancellationToken cancel = default)
    {
        var found = new Dictionary<long, List<NotableBody>>();
        foreach (var chunk in addresses.Distinct().Chunk(RouteChunk))
        {
            using var content = new StringContent(RouteQuery(chunk.Select(a => a.ToString(CultureInfo.InvariantCulture)).ToArray(), minValue),
                Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync("https://spansh.co.uk/api/bodies/search", content, cancel);
            response.EnsureSuccessStatusCode();
            foreach (var (address, notable) in ParseSpansh(await response.Content.ReadAsStringAsync(cancel)))
            {
                var list = found.TryGetValue(address, out var known) ? known : found[address] = [];
                if (!list.Any(b => b.Name == notable.Name))
                    list.Add(notable);
            }
        }

        return found;
    }

    /// <summary>
    /// The planets of these systems Spansh reckons worth half the threshold or more: its estimate differs
    /// from ours, which then sorts them. A floor of 100 k keeps a low threshold from asking for every rock.
    /// </summary>
    internal static string RouteQuery(string[] systems, long minValue) => JsonSerializer.Serialize(new
    {
        filters = new Dictionary<string, object>
        {
            ["system_id64"] = new { value = systems },
            ["type"] = new { value = new[] { "Planet" } },
            ["estimated_mapping_value"] = new
            {
                min = Math.Max(100_000, minValue / 2).ToString(CultureInfo.InvariantCulture),
                max = "999999999999",
            },
        },
        sort = Array.Empty<object>(),
        size = 500,
        page = 0,
    });

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
                && GameState.Str(b, "name") is { } name && GameState.Str(b, "subtype") is { } subtype && Classify(subtype) is { } kind)
                bodies.Add((system, new NotableBody(name, kind, terraformable, GameState.Dbl(b, "distance_to_arrival") ?? 0,
                    Value: Value(subtype, terraformable, GameState.Dbl(b, "earth_masses"), false, false))));
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
            .Select(b => b.Name is not null && GameState.Str(b.Body, "subType") is { } subType && Classify(subType) is { } kind
                ? new NotableBody(b.Name, kind, b.Terraformable, GameState.Dbl(b.Body, "distanceToArrival") ?? 0,
                    Value: Value(subType, b.Terraformable, GameState.Dbl(b.Body, "earthMasses"), false, false))
                : null)
            .OfType<NotableBody>()
            .ToList();
    }
}
