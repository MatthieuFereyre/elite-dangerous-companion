using System.Net.Http;
using System.Text.Json;

namespace EDC.Companion;

/// <summary>A body worth mapping with the surface scanner: an Earth-like, water or ammonia world, a terraformable.</summary>
public sealed record ValuableBody(string Name, double Distance, long Value);

/// <summary>What EDSM reckons a system pays, scanned and mapped, from the bodies players sent it.</summary>
public sealed record SystemValue(long Scan, long Mapped, IReadOnlyList<ValuableBody> Bodies)
{
    /// <summary>A system EDSM does not know, or knows without its bodies: perhaps never discovered.</summary>
    public static readonly SystemValue Unknown = new(0, 0, []);

    public bool Known => Scan > 0;
}

/// <summary>EDSM's estimated value of a system, asked on arriving there.</summary>
public static class SystemValues
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<SystemValue> FetchAsync(long address, CancellationToken cancel = default) =>
        Parse(await Http.GetStringAsync($"https://www.edsm.net/api-system-v1/estimated-value?systemId64={address}", cancel));

    /// <summary>The value, and the bodies worth mapping nearest the arrival star first.</summary>
    public static SystemValue Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || GameState.Long(root, "estimatedValue") is not { } scan)
            return SystemValue.Unknown;

        var bodies = root.TryGetProperty("valuableBodies", out var valuable) && valuable.ValueKind == JsonValueKind.Array
            ? valuable.EnumerateArray()
                .Where(b => GameState.Str(b, "bodyName") is not null)
                .Select(b => new ValuableBody(GameState.Str(b, "bodyName")!, GameState.Dbl(b, "distance") ?? 0, GameState.Long(b, "valueMax") ?? 0))
                .OrderBy(b => b.Distance)
                .ToList()
            : [];
        return new SystemValue(scan,GameState.Long(root, "estimatedValueMapped") ?? 0, bodies);
    }
}
