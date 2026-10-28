using System.Net.Http;
using System.Text.Json;

namespace EDC.Companion;

public enum SurveyKind { Undiscovered, Partial, Complete }

/// <summary>
/// What EDSM knows of a system: nothing, some of its bodies, or all of them. <paramref name="Count"/> is
/// the number the discovery scanner gave; unknown when nobody sent it.
/// </summary>
public sealed record SystemSurvey(SurveyKind Kind, int Known, int? Count, IReadOnlyList<NotableBody> Planets)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<SystemSurvey> FetchAsync(long address, CancellationToken cancel = default) =>
        Parse(await Http.GetStringAsync($"https://www.edsm.net/api-system-v1/bodies?systemId64={address}", cancel));

    /// <summary>EDSM's api-system-v1/bodies: "{}" for a system it has never seen.</summary>
    public static SystemSurvey Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out _))
            return new SystemSurvey(SurveyKind.Undiscovered, 0, null, []);

        var known = root.TryGetProperty("bodies", out var bodies) && bodies.ValueKind == JsonValueKind.Array ? bodies.GetArrayLength() : 0;
        var count = GameState.Long(root, "bodyCount") is { } c ? (int)c : (int?)null;
        return new SystemSurvey(count is { } total && known >= total ? SurveyKind.Complete : SurveyKind.Partial, known, count, NotableBodies.Parse(json));
    }
}
