using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EDC.Companion;

/// <summary>A system with a hazardous resource extraction site, how far it is, and on which planets' rings.</summary>
public sealed record HuntCandidate(string System, double Distance, string Where = "");

/// <summary>What Spansh says of a system: an anarchy has no bounty, and the power that holds it.</summary>
public sealed record HuntSystem(string? Government, string? Power);

/// <summary>A system to hunt in, to copy into the galaxy map.</summary>
public sealed record HuntSpot(string System, double Distance, string? Power, string Where = "")
{
    /// <summary>
    /// The distance and the jumps it takes at <paramref name="jumpRange"/>, the most the FSD does: a floor,
    /// the route between the stars adds some. Without a range, the distance alone.
    /// </summary>
    public string Trip(double jumpRange)
    {
        // edtools gives 0.00 for the system the search starts from.
        if (Distance <= 0)
            return Resources.Strings.StationHere;

        var distance = string.Format(Resources.Strings.LightYears, Distance.ToString("0.#", Core.Loc.Culture));
        if (jumpRange <= 0)
            return distance;

        var jumps = Math.Max(1, (int)Math.Ceiling(Distance / jumpRange));
        return string.Format(Resources.Strings.HuntTrip, distance, Core.Loc.Plural(jumps, Resources.Strings.JumpsOne, Resources.Strings.JumpsMany));
    }
}

public sealed record HuntResult(HuntSpot? Nearest, HuntSpot? OfPower);

/// <summary>
/// The nearest systems for bounty hunting: a hazardous RES, the most wanted ships, outside anarchies,
/// where nobody is wanted. edtools.cc knows the RES (from what players' tools send to EDDN, nearest
/// first); Spansh knows the government and the power of each system.
/// </summary>
public static partial class HuntFinder
{
    /// <summary>The powers of Powerplay, as Spansh and the journal name them.</summary>
    public static readonly IReadOnlyList<string> Powers =
    [
        "A. Lavigny-Duval", "Aisling Duval", "Archon Delaine", "Denton Patreus", "Edmund Mahon", "Felicia Winters",
        "Jerome Archer", "Li Yong-Rui", "Nakato Kaine", "Pranav Antal", "Yuri Grom", "Zemina Torval",
    ];

    /// <summary>The first systems of edtools' list sent to Spansh: one request, enough for a power's.</summary>
    private const int Asked = 100;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // edtools turns away a client that does not look like a browser.
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) EliteDangerousCompanion");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/json");
        return client;
    }

    public static async Task<HuntResult> FindAsync(string system, string? power, CancellationToken cancel = default)
    {
        var html = await Http.GetStringAsync($"https://edtools.cc/res.php?s={Uri.EscapeDataString(system)}&f_hz=on", cancel);
        var hazards = ParseHazards(html);
        if (hazards.Count == 0)
            return new HuntResult(null, null);

        var query = JsonSerializer.Serialize(new
        {
            filters = new { name = new { value = hazards.Take(Asked).Select(h => h.System).ToArray() } },
            size = Asked,
        });
        using var response = await Http.PostAsync("https://spansh.co.uk/api/systems/search",
            new StringContent(query, Encoding.UTF8, "application/json"), cancel);
        response.EnsureSuccessStatusCode();
        return Pick(hazards, ParseSystems(await response.Content.ReadAsStringAsync(cancel)), power);
    }

    /// <summary>The rows of edtools' table with a hazardous RES, in its order: nearest first.</summary>
    public static IReadOnlyList<HuntCandidate> ParseHazards(string html)
    {
        var candidates = new List<HuntCandidate>();
        foreach (Match row in RowPattern().Matches(html))
        {
            var cells = CellPattern().Matches(row.Groups[1].Value).Select(c => c.Groups[1].Value).ToList();
            // Distance, system, beacon, Haz, High, Reg, Low, notes.
            if (cells.Count < 4 || cells[3].Trim().Length == 0
                || !double.TryParse(cells[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var distance)
                || NamePattern().Match(cells[1]) is not { Success: true } name)
                continue;

            candidates.Add(new HuntCandidate(WebUtility.HtmlDecode(name.Groups[1].Value), distance, Where(cells[3])));
        }

        return candidates;
    }

    /// <summary>
    /// « Planète A 10 · 1 231 ls » from edtools' Haz cell: one site, or several in its tooltip. The RES of a
    /// quiet system only shows near its ring, so the planet is where to fly.
    /// </summary>
    private static string Where(string cell)
    {
        var tip = TipPattern().Match(cell);
        var sites = BreakPattern().Split(tip.Success ? tip.Groups[1].Value : cell)
            .Select(site => SitePattern().Match(WebUtility.HtmlDecode(site).Trim()))
            .Where(site => site.Success)
            .Select(site => (
                // edtools writes « A10 », the game « A 10 »; and « Asellus_3 » for a named planet.
                Body: BodyPattern().Replace(site.Groups[1].Value, "$1 $2").Replace('_', ' '),
                Ls: double.TryParse(site.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ls) ? ls : (double?)null))
            .OrderBy(site => site.Ls ?? double.MaxValue)
            .Select(site => site.Ls is { } ls ? $"{site.Body} · {ls.ToString("N0", Core.Loc.Culture)} ls" : site.Body)
            .ToList();

        return sites.Count switch
        {
            0 => "",
            1 => string.Format(Resources.Strings.HuntPlanet, sites[0]),
            _ => string.Format(Resources.Strings.HuntPlanets, string.Join(", ", sites)),
        };
    }

    public static IReadOnlyDictionary<string, HuntSystem> ParseSystems(string json)
    {
        var systems = new Dictionary<string, HuntSystem>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return systems;

        foreach (var system in results.EnumerateArray())
            if (GameState.Str(system, "name") is { } name)
                systems[name] = new HuntSystem(GameState.Str(system, "government"), GameState.Str(system, "controlling_power"));
        return systems;
    }

    /// <summary>The nearest outside anarchies, and the nearest held by <paramref name="power"/>.</summary>
    public static HuntResult Pick(IReadOnlyList<HuntCandidate> hazards, IReadOnlyDictionary<string, HuntSystem> systems, string? power)
    {
        var spots = hazards
            .Select(h => (Candidate: h, System: systems.GetValueOrDefault(h.System)))
            .Where(s => s.System?.Government != "Anarchy")
            .Select(s => new HuntSpot(s.Candidate.System, s.Candidate.Distance, s.System?.Power, s.Candidate.Where))
            .ToList();

        return new HuntResult(
            spots.FirstOrDefault(),
            power is null ? null : spots.FirstOrDefault(s => s.Power == power));
    }

    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline)]
    private static partial Regex RowPattern();

    [GeneratedRegex(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex CellPattern();

    [GeneratedRegex(@"data-clipboard-text=""([^""]*)""")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"<span class=""ttip"">(.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex TipPattern();

    [GeneratedRegex(@"<br\s*/?>")]
    private static partial Regex BreakPattern();

    // « A10:1231 ls », or the body alone when edtools has no distance.
    [GeneratedRegex(@"^([^:]+?)\s*(?::\s*([\d.]+)\s*ls)?$")]
    private static partial Regex SitePattern();

    [GeneratedRegex(@"^([A-Z]+)(\d)")]
    private static partial Regex BodyPattern();
}
