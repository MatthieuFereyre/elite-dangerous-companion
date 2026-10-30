using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EDC.Companion;

/// <summary>A step of a neutron route: the system to put in the galaxy map, and the jumps the game's route takes to it.</summary>
public sealed record NeutronStep(string System, int Jumps, bool Neutron, double DistanceLeft);

/// <summary>
/// A route as Spansh plots it, through neutron stars or given by a link, and the last of its steps
/// reached: the first is the start, so reached from the outset, unless the route was given away from it (-1).
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
        return await QueueAsync("route", form, cancel);
    }

    /// <summary>
    /// A route plotted on Spansh, from the link of its results: read while Spansh keeps it, plotted again
    /// from the link's parameters once it has let it expire.
    /// </summary>
    public static async Task<NeutronRoute> ImportAsync(string text, CancellationToken cancel = default)
    {
        var link = SpanshLink.Parse(text) ?? throw new NeutronException(Resources.Strings.RouteLinkUnknown);
        if (link.Job is { } job)
        {
            using var answer = await Http.GetAsync(Results(job), cancel);
            if (answer.StatusCode != System.Net.HttpStatusCode.NotFound)
                return ParseResult(await answer.Content.ReadAsStringAsync(cancel)) ?? await PollAsync(job, cancel);
        }

        if (link.Parameters.Count == 0)
            throw new NeutronException(Resources.Strings.RouteExpired);
        using var form = new FormUrlEncodedContent(link.Parameters.Select(p => KeyValuePair.Create(p.Name, p.Value)));
        return await QueueAsync(link.Endpoint, form, cancel);
    }

    private static async Task<NeutronRoute> QueueAsync(string endpoint, HttpContent form, CancellationToken cancel)
    {
        using var queued = await Http.PostAsync($"https://spansh.co.uk/api/{endpoint}", form, cancel);
        return await PollAsync(ParseJob(await queued.Content.ReadAsStringAsync(cancel)), cancel);
    }

    private static string Results(string job) => $"https://spansh.co.uk/api/results/{Uri.EscapeDataString(job)}";

    private static async Task<NeutronRoute> PollAsync(string job, CancellationToken cancel)
    {
        // A long route takes a while: a minute at most.
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancel);
            if (ParseResult(await Http.GetStringAsync(Results(job), cancel)) is { } route)
                return route;
        }

        throw new NeutronException(Resources.Strings.NeutronTimeout);
    }

    public static string ParseJob(string json)
    {
        using var document = Parse(json);
        return GameState.Str(document.RootElement, "job") ?? throw new NeutronException(Resources.Strings.NeutronUnreadable);
    }

    /// <summary>
    /// The route, or null while the job is still queued. The neutron and tourist plotters give their
    /// jumps, the road to riches and its kin a list of systems; only the neutron plotter says what is
    /// left, worked out for the others from where the systems lie.
    /// </summary>
    public static NeutronRoute? ParseResult(string json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        // Queued or started, the answer has no result yet.
        if (!root.TryGetProperty("result", out var result) || result.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            return null;

        var rows = result.ValueKind == JsonValueKind.Array ? result
            : result.TryGetProperty("system_jumps", out var jumps) && jumps.ValueKind == JsonValueKind.Array ? jumps
            : default;
        var list = rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray().ToList() : [];
        var left = DistancesLeft(list);
        // The first is where the route starts: no jump to it, whatever the list says.
        var steps = list.Select((j, i) => new NeutronStep(GameState.Str(j, "system") ?? GameState.Str(j, "name") ?? "?",
                i == 0 ? 0 : (int)(GameState.Long(j, "jumps") ?? 0), GameState.Bool(j, "neutron_star") == true, left[i]))
            .ToList();

        var range = (result.ValueKind == JsonValueKind.Object ? Number(result, "range") : null)
                    ?? (root.TryGetProperty("parameters", out var parameters) ? Number(parameters, "range") : null) ?? 0;
        var from = result.ValueKind == JsonValueKind.Object ? GameState.Str(result, "source_system") : null;
        var to = result.ValueKind == JsonValueKind.Object ? GameState.Str(result, "destination_system") ?? GameState.Str(result, "final_destination_system") : null;
        return new NeutronRoute(from ?? steps.FirstOrDefault()?.System ?? "", to ?? steps.LastOrDefault()?.System ?? "", range, steps);
    }

    // Spansh writes some numbers as text.
    private static double? Number(JsonElement e, string name) =>
        GameState.Dbl(e, name) ?? (double.TryParse(GameState.Str(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null);

    // Spansh's own when it gives it; otherwise the way still to go, hop by hop, from the coordinates.
    private static double[] DistancesLeft(List<JsonElement> rows)
    {
        var left = new double[rows.Count];
        for (var i = rows.Count - 2; i >= 0; i--)
            left[i] = left[i + 1] + Hop(rows[i], rows[i + 1]);
        for (var i = 0; i < rows.Count; i++)
            if (GameState.Dbl(rows[i], "distance_left") is { } given)
                left[i] = given;
        return left;
    }

    private static double Hop(JsonElement from, JsonElement to)
    {
        double Axis(JsonElement row, string name) => GameState.Dbl(row, name) ?? 0;
        var (dx, dy, dz) = (Axis(to, "x") - Axis(from, "x"), Axis(to, "y") - Axis(from, "y"), Axis(to, "z") - Axis(from, "z"));
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
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
