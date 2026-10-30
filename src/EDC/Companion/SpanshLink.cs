using System.Text.Json;

namespace EDC.Companion;

/// <summary>
/// The link of a route's results on spansh.co.uk, « /riches/results/&lt;job&gt;?from=…&amp;range=… »:
/// the job to read, and the parameters to plot it again with once Spansh has let it expire, as its own
/// page does. A list in the link, the tourist planner's destinations, is given one value at a time.
/// </summary>
public sealed record SpanshLink(string Kind, string? Job, IReadOnlyList<(string Name, string Value)> Parameters)
{
    /// <summary>Where to plot it again: the neutron plotter's page is « plotter », its address « route ».</summary>
    public string Endpoint => Kind == "plotter" ? "route" : $"{Kind}/route";

    public static SpanshLink? Parse(string? text)
    {
        if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
            || !(uri.Host == "spansh.co.uk" || uri.Host.EndsWith(".spansh.co.uk", StringComparison.Ordinal)))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;

        var job = segments.Length >= 3 && segments[1] == "results" ? segments[2] : null;
        var parameters = new List<(string, string)>();
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equal = pair.IndexOf('=');
            var name = Uri.UnescapeDataString((equal < 0 ? pair : pair[..equal]).Replace('+', ' '));
            var value = equal < 0 ? "" : Uri.UnescapeDataString(pair[(equal + 1)..].Replace('+', ' '));
            parameters.AddRange(List(value) is { } values ? values.Select(v => (name, v)) : [(name, value)]);
        }

        // Without a job nor anything to plot with, a page of the site, not a route.
        return job is null && parameters.Count == 0 ? null : new SpanshLink(segments[0], job, parameters);
    }

    private static List<string>? List(string value)
    {
        if (!value.StartsWith('['))
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
