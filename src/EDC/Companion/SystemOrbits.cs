using System.Text.Json;

namespace EDC.Companion;

/// <summary>A point in a system, in light-seconds.</summary>
public readonly record struct Point3(double X, double Y, double Z)
{
    public static Point3 operator +(Point3 a, Point3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public double DistanceTo(Point3 other)
    {
        double dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

/// <summary>
/// A Keplerian orbit as the journal's scans give it: the semi-major axis (here in light-seconds),
/// the angles in degrees, the period in seconds, and the mean anomaly at the moment of the scan.
/// </summary>
public sealed record Orbit(double SemiMajorAxisLs, double Eccentricity, double Inclination, double Periapsis,
    double AscendingNode, double PeriodSeconds, double MeanAnomaly, DateTime EpochUtc)
{
    private const double Light = 299_792_458;

    /// <summary>Where the body is on this orbit at <paramref name="utc"/>, from what it orbits.</summary>
    public Point3 Relative(DateTime utc)
    {
        var e = Eccentricity;
        var m = Radians(MeanAnomaly) + 2 * Math.PI * (utc - EpochUtc).TotalSeconds / PeriodSeconds;
        var anomaly = m;
        for (var n = 0; n < 30; n++)
            anomaly -= (anomaly - e * Math.Sin(anomaly) - m) / (1 - e * Math.Cos(anomaly));

        var x = SemiMajorAxisLs * (Math.Cos(anomaly) - e);
        var y = SemiMajorAxisLs * Math.Sqrt(Math.Max(0, 1 - e * e)) * Math.Sin(anomaly);
        double cw = Math.Cos(Radians(Periapsis)), sw = Math.Sin(Radians(Periapsis));
        double ci = Math.Cos(Radians(Inclination)), si = Math.Sin(Radians(Inclination));
        double cn = Math.Cos(Radians(AscendingNode)), sn = Math.Sin(Radians(AscendingNode));
        return new Point3(
            (cn * cw - sn * sw * ci) * x + (-cn * sw - sn * cw * ci) * y,
            (sn * cw + cn * sw * ci) * x + (-sn * sw + cn * cw * ci) * y,
            sw * si * x + cw * si * y);
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    /// <summary>The orbit of a Scan or ScanBaryCentre event; null for the root of a system, which has none.</summary>
    public static Orbit? From(JsonElement e)
    {
        if (GameState.Dbl(e, "SemiMajorAxis") is not { } axis || GameState.Dbl(e, "OrbitalPeriod") is not { } period || period <= 0
            || GameState.Str(e, "timestamp") is not { } stamp
            || !DateTime.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var epoch))
            return null;

        return new Orbit(axis / Light, GameState.Dbl(e, "Eccentricity") ?? 0, GameState.Dbl(e, "OrbitalInclination") ?? 0,
            GameState.Dbl(e, "Periapsis") ?? 0, GameState.Dbl(e, "AscendingNode") ?? 0, period, GameState.Dbl(e, "MeanAnomaly") ?? 0, epoch);
    }
}

/// <summary>
/// The bodies of the current system as the journal scanned them, placed where they are now: each one on
/// its orbit, plus the orbits of what it goes around, up to the root of the system. A barycentre does
/// not say what it orbits; a body that goes around it does, in its list of parents.
/// </summary>
public sealed class SystemOrbits
{
    private sealed record Entry(int Id, string? Name, Orbit? Orbit, int[] Ancestors, bool IsArrivalStar);

    private readonly Dictionary<int, Entry> _bodies = [];
    private readonly Dictionary<string, int> _ids = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The star one arrives at; null until it is scanned.</summary>
    public string? ArrivalStar { get; private set; }

    public void Add(JsonElement e)
    {
        if (GameState.Long(e, "BodyID") is not { } id)
            return;

        var ancestors = e.TryGetProperty("Parents", out var parents) && parents.ValueKind == JsonValueKind.Array
            ? parents.EnumerateArray().SelectMany(p => p.EnumerateObject().Select(o => o.Value.GetInt32())).ToArray()
            : [];
        var name = GameState.Str(e, "BodyName");
        var arrival = GameState.Str(e, "StarType") is not null && GameState.Dbl(e, "DistanceFromArrivalLS") is 0;
        _bodies[(int)id] = new Entry((int)id, name, Orbit.From(e), ancestors, arrival);
        if (name is not null)
            _ids[name] = (int)id;
        if (arrival)
            ArrivalStar = name;
    }

    public Point3? Position(string body, DateTime utc) => _ids.TryGetValue(body, out var id) ? Position(id, utc) : null;

    public Point3? Position(int id, DateTime utc)
    {
        if (Chain(id) is not { } chain)
            return null;

        var at = new Point3(0, 0, 0);
        for (var n = 0; n < chain.Count; n++)
        {
            var root = n == chain.Count - 1;
            if (!_bodies.TryGetValue(chain[n], out var entry))
            {
                // Only the root of the system may be unknown: it sits at the origin.
                if (root)
                    break;
                return null;
            }

            if (entry.Orbit is null)
            {
                if (root)
                    break;
                return null;   // a belt cluster, or a body that cannot be placed
            }

            at += entry.Orbit.Relative(utc);
        }

        return at;
    }

    /// <summary>The body then its ancestors up to the root.</summary>
    private List<int>? Chain(int id)
    {
        if (_bodies.TryGetValue(id, out var entry) && (entry.Ancestors.Length > 0 || entry.Name is not null))
            return [id, .. entry.Ancestors];

        // A barycentre, or a body not scanned: what orbits it knows its ancestors.
        foreach (var other in _bodies.Values)
        {
            var at = Array.IndexOf(other.Ancestors, id);
            if (at >= 0)
                return [.. other.Ancestors[at..]];
        }

        return entry is null ? null : [id];
    }
}

/// <summary>
/// The order in which to visit bodies so that supercruise takes the least time. Supercruise time grows
/// far slower than the distance: a hop between two neighbours costs much more per light-second than a
/// long crossing, so neighbours are best taken together.
/// </summary>
public static class VisitPlanner
{
    /// <summary>Exact below this many stops; beyond, nearest neighbour then 2-opt.</summary>
    private const int Exact = 10;

    /// <summary>
    /// An estimate, not the game's rule: about 20 s for 10 ls, 2 min 20 for 1,000 ls, 3 min 30 for
    /// 10,000 ls, then the 2,001 c of top speed for the very long crossings.
    /// </summary>
    public static double SupercruiseSeconds(double lightSeconds) => 30 * Math.Log(1 + lightSeconds / 10) + lightSeconds / 2001;

    /// <summary>The stops in the order to visit them from <paramref name="start"/>, each with the time to reach it from the one before.</summary>
    public static IReadOnlyList<(int Index, double Seconds)> Order(Point3 start, IReadOnlyList<Point3> stops)
    {
        var n = stops.Count;
        if (n == 0)
            return [];

        double Cost(int from, int to) => SupercruiseSeconds((from < 0 ? start : stops[from]).DistanceTo(stops[to]));
        var order = n <= Exact ? HeldKarp(n, Cost) : TwoOpt(NearestNeighbour(n, Cost), Cost);

        var legs = new List<(int, double)>(n);
        var previous = -1;
        foreach (var stop in order)
        {
            legs.Add((stop, Cost(previous, stop)));
            previous = stop;
        }

        return legs;
    }

    // Open path from the start through every stop, by subsets.
    private static List<int> HeldKarp(int n, Func<int, int, double> cost)
    {
        var full = 1 << n;
        var best = new double[full, n];
        var from = new int[full, n];
        for (var mask = 0; mask < full; mask++)
            for (var last = 0; last < n; last++)
                best[mask, last] = double.PositiveInfinity;
        for (var stop = 0; stop < n; stop++)
            best[1 << stop, stop] = cost(-1, stop);

        for (var mask = 1; mask < full; mask++)
            for (var last = 0; last < n; last++)
            {
                if ((mask & (1 << last)) == 0 || double.IsPositiveInfinity(best[mask, last]))
                    continue;
                for (var next = 0; next < n; next++)
                {
                    if ((mask & (1 << next)) != 0)
                        continue;
                    var into = mask | (1 << next);
                    var total = best[mask, last] + cost(last, next);
                    if (total < best[into, next])
                    {
                        best[into, next] = total;
                        from[into, next] = last;
                    }
                }
            }

        var end = Enumerable.Range(0, n).MinBy(last => best[full - 1, last]);
        var path = new List<int>(n);
        for (int mask = full - 1, at = end; ; )
        {
            path.Add(at);
            var before = mask == 1 << at ? -1 : from[mask, at];
            mask &= ~(1 << at);
            if (before < 0)
                break;
            at = before;
        }

        path.Reverse();
        return path;
    }

    private static List<int> NearestNeighbour(int n, Func<int, int, double> cost)
    {
        var left = Enumerable.Range(0, n).ToHashSet();
        var path = new List<int>(n);
        var at = -1;
        while (left.Count > 0)
        {
            var next = left.MinBy(stop => cost(at, stop));
            path.Add(next);
            left.Remove(next);
            at = next;
        }

        return path;
    }

    private static List<int> TwoOpt(List<int> path, Func<int, int, double> cost)
    {
        double Length(List<int> p) => p.Select((stop, i) => cost(i == 0 ? -1 : p[i - 1], stop)).Sum();

        var improved = true;
        while (improved)
        {
            improved = false;
            for (var i = 0; i < path.Count - 1; i++)
                for (var j = i + 1; j < path.Count; j++)
                {
                    var candidate = new List<int>(path);
                    candidate.Reverse(i, j - i + 1);
                    if (Length(candidate) < Length(path) - 1e-9)
                    {
                        path = candidate;
                        improved = true;
                    }
                }
        }

        return path;
    }
}
