using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EDC.Companion;

/// <summary>A step of a neutron route: the system to put in the galaxy map, and the jumps the game's route takes to it.</summary>
public sealed record NeutronStep(string System, int Jumps, bool Neutron, double DistanceLeft);

/// <summary>
/// A neutron route as Spansh plots it, and the last of its steps reached: the first is the start, so
/// reached from the outset.
/// </summary>
public sealed record NeutronRoute(string From, string To, double Range, IReadOnlyList<NeutronStep> Steps, int Reached = 0)
{
    /// <summary>Spansh's own total_jumps counts the steps: the jumps are those of each step, added up.</summary>
    [JsonIgnore]
    public int TotalJumps => Steps.Sum(s => s.Jumps);

    [JsonIgnore]
    public double Distance => Steps.Count > 0 ? Steps[0].DistanceLeft : 0;

    /// <summary>The step to copy into the galaxy map; null once arrived.</summary>
    [JsonIgnore]
    public NeutronStep? Next => Reached + 1 < Steps.Count ? Steps[Reached + 1] : null;

    /// <summary>
    /// Forward to <paramref name="system"/> when it is a step ahead. Between two steps the game's route
    /// goes through plain stars: those change nothing, and neither does a step left behind.
    /// </summary>
    public NeutronRoute Advance(string? system)
    {
        for (var i = Reached + 1; i < Steps.Count; i++)
            if (string.Equals(Steps[i].System, system, StringComparison.OrdinalIgnoreCase))
                return this with { Reached = i };
        return this;
    }
}

/// <summary>Spansh's error, in French when it is a known one.</summary>
public sealed class NeutronException(string message) : Exception(message);

/// <summary>
/// The neutron plotter of spansh.co.uk: a job is queued with the start, the end and the range, then its
/// result asked for until it is ready, which takes a few seconds.
/// </summary>
public static class NeutronPlotter
{
    /// <summary>Spansh's default: how far a step may stray from the straight line, for more neutron stars.</summary>
    private const int Efficiency = 60;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) EliteDangerousCompanion");
        return client;
    }

    public static async Task<NeutronRoute> PlotAsync(string from, string to, double range, CancellationToken cancel = default)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["efficiency"] = Efficiency.ToString(CultureInfo.InvariantCulture),
            ["range"] = range.ToString(CultureInfo.InvariantCulture),
            ["from"] = from,
            ["to"] = to,
        });
        using var queued = await Http.PostAsync("https://spansh.co.uk/api/route", form, cancel);
        var job = ParseJob(await queued.Content.ReadAsStringAsync(cancel));

        // A long route takes a while: a minute at most.
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancel);
            if (ParseResult(await Http.GetStringAsync($"https://spansh.co.uk/api/results/{Uri.EscapeDataString(job)}", cancel)) is { } route)
                return route;
        }

        throw new NeutronException(Resources.Strings.NeutronTimeout);
    }

    public static string ParseJob(string json)
    {
        using var document = Parse(json);
        return GameState.Str(document.RootElement, "job") ?? throw new NeutronException(Resources.Strings.NeutronUnreadable);
    }

    /// <summary>The route, or null while the job is still queued.</summary>
    public static NeutronRoute? ParseResult(string json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        // Queued or started, the answer has no result yet.
        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            return null;

        var steps = result.TryGetProperty("system_jumps", out var jumps) && jumps.ValueKind == JsonValueKind.Array
            ? jumps.EnumerateArray()
                .Select(j => new NeutronStep(GameState.Str(j, "system") ?? "?", (int)(GameState.Long(j, "jumps") ?? 0),
                    GameState.Bool(j, "neutron_star") == true, GameState.Dbl(j, "distance_left") ?? 0))
                .ToList()
            : [];
        var range = double.TryParse(GameState.Str(result, "range"), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0;
        return new NeutronRoute(GameState.Str(result, "source_system") ?? "", GameState.Str(result, "destination_system") ?? "", range, steps);
    }

    /// <summary>The document, unless Spansh answered with an error.</summary>
    private static JsonDocument Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new NeutronException(Resources.Strings.NeutronUnreadable);
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object && GameState.Str(document.RootElement, "error") is { } error)
        {
            document.Dispose();
            throw new NeutronException(error switch
            {
                "Could not find finishing system" => Resources.Strings.NeutronNoEnd,
                "Could not find starting system" => Resources.Strings.NeutronNoStart,
                _ => error,
            });
        }

        return document;
    }
}
