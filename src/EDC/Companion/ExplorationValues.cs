using System.Text.Json;

namespace EDC.Companion;

/// <summary>A star or planet scanned, with what its value depends on.</summary>
public sealed record ScanValue(string System, bool Star, double K, double Mass, bool FirstDiscovery, bool FirstMapping);

/// <summary>
/// What the data of a body sells for at Universal Cartographics, by the formula players worked out from
/// the game's prices (EDDiscovery, MattG). The game never says what the data held is worth.
/// </summary>
public static class ExplorationValues
{
    private const double Q = 0.56591828;

    /// <summary>
    /// A star or planet of a Scan event; a belt or a ring is worth nothing on its own, and Universal
    /// Cartographics does not buy what a navigation beacon gave.
    /// </summary>
    public static ScanValue? FromScan(JsonElement e)
    {
        if (GameState.Str(e, "StarSystem") is not { } system || GameState.Str(e, "ScanType") == "NavBeaconDetail")
            return null;
        var firstDiscovery = e.TryGetProperty("WasDiscovered", out var discovered) && discovered.ValueKind == JsonValueKind.False;
        var firstMapping = e.TryGetProperty("WasMapped", out var mapped) && mapped.ValueKind == JsonValueKind.False;

        if (GameState.Str(e, "StarType") is { } type)
        {
            var k = type switch
            {
                "N" or "H" => 22628,
                _ when type.StartsWith('D') => 14057,
                _ => 1200,
            };
            return new ScanValue(system, true, k, GameState.Dbl(e, "StellarMass") ?? 0, firstDiscovery, false);
        }

        if (GameState.Str(e, "PlanetClass") is { } planetClass)
            return new ScanValue(system, false, PlanetK(planetClass, GameState.Str(e, "TerraformState") is "Terraformable"),
                GameState.Dbl(e, "MassEM") ?? 0, firstDiscovery, firstMapping);

        return null;
    }

    /// <summary>The constant of a planet's value, by its class as the journal, EDSM or Spansh names it.</summary>
    public static double PlanetK(string planetClass, bool terraformable) => planetClass switch
    {
        "Metal rich body" or "Metal-rich body" => 21790,
        "Ammonia world" => 96932,
        "Sudarsky class I gas giant" or "Class I gas giant" => 1656,
        "High metal content body" or "High metal content world" or "Sudarsky class II gas giant" or "Class II gas giant" => 9654 + (terraformable ? 100677 : 0),
        "Earthlike body" or "Earth-like world" => 64831 + 116295,
        "Water world" => 64831 + (terraformable ? 116295 : 0),
        _ => 300 + (terraformable ? 93328 : 0),
    };

    /// <summary>What a planet pays mapped within the probes of its target, the way worth planning for.</summary>
    public static long Mapped(string planetClass, bool terraformable, double mass, bool firstDiscovery, bool firstMapping) =>
        Value(new ScanValue("", false, PlanetK(planetClass, terraformable), mass, firstDiscovery, firstMapping), mapped: true, efficient: true);

    /// <summary>The body's value, mapped with the surface scanner or not, within the probes of its target or not.</summary>
    public static long Value(ScanValue body, bool mapped, bool efficient)
    {
        if (body.Star)
            return (long)Math.Round((body.K + body.Mass * body.K / 66.25) * (body.FirstDiscovery ? 2.6 : 1));

        var mapping = !mapped ? 1
            : body.FirstDiscovery && body.FirstMapping ? 3.699622554
            : body.FirstMapping ? 8.0956
            : 3.3333333333;
        var value = (body.K + body.K * Q * Math.Pow(body.Mass, 0.2)) * mapping;
        if (mapped)
        {
            value += Math.Max(value * 0.3, 555);
            if (efficient)
                value *= 1.25;
        }

        return (long)Math.Round(Math.Max(500, value) * (body.FirstDiscovery ? 2.6 : 1));
    }
}
