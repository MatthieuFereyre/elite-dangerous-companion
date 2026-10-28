using System.Text.Json;
using EDC.Companion;
using Xunit;

namespace EDC.Tests;

public sealed class OrbitTests
{
    private const double C = 299_792_458;
    private static readonly DateTime Epoch = new(2026, 10, 3, 19, 25, 35, DateTimeKind.Utc);

    private static Orbit Circular(double ls, double periodSeconds, double meanAnomaly = 0) =>
        new(ls, 0, 0, 0, 0, periodSeconds, meanAnomaly, Epoch);

    [Fact]
    public void A_body_moves_along_its_orbit_from_where_the_scan_saw_it()
    {
        var orbit = Circular(100, 1000);

        Assert.Equal(100, orbit.Relative(Epoch).X, 6);
        var half = orbit.Relative(Epoch.AddSeconds(500));
        Assert.Equal((-100, 0), (Math.Round(half.X, 6), Math.Round(half.Y, 6)));
    }

    [Fact]
    public void An_eccentric_orbit_comes_nearest_at_its_periapsis()
    {
        var orbit = new Orbit(100, 0.5, 30, 40, 50, 1000, 0, Epoch);
        var p = orbit.Relative(Epoch);

        Assert.Equal(50, Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z), 6);
    }

    private static string Scan(int id, string name, double smaMetres, string parents, double meanAnomaly = 0, double period = 1e9, double arrival = 1000) =>
        $$"""{ "timestamp":"2026-10-03T19:25:35Z", "event":"Scan", "BodyName":"{{name}}", "BodyID":{{id}}, "Parents":[ {{parents}} ], "DistanceFromArrivalLS":{{arrival}}, "PlanetClass":"Icy body", "SemiMajorAxis":{{smaMetres}}, "Eccentricity":0, "OrbitalInclination":0, "Periapsis":0, "OrbitalPeriod":{{period}}, "AscendingNode":0, "MeanAnomaly":{{meanAnomaly}} }""";

    private static SystemOrbits Orbits(params string[] scans)
    {
        var orbits = new SystemOrbits();
        foreach (var scan in scans)
        {
            using var document = JsonDocument.Parse(scan);
            orbits.Add(document.RootElement);
        }

        return orbits;
    }

    [Fact]
    public void A_moon_is_where_its_planet_is_plus_its_own_orbit()
    {
        var orbits = Orbits(
            """{ "timestamp":"2026-10-03T19:25:35Z", "event":"Scan", "BodyName":"Alpha A", "BodyID":0, "DistanceFromArrivalLS":0, "StarType":"K" }""",
            Scan(1, "Alpha A 1", 1000 * C, """{ "Star":0 }"""),
            Scan(2, "Alpha A 1 a", 2 * C, """{ "Planet":1 }, { "Star":0 }"""));

        var moon = orbits.Position("Alpha A 1 a", Epoch)!.Value;

        Assert.Equal(1002, moon.X, 6);
        Assert.Equal(0, orbits.Position("Alpha A", Epoch)!.Value.X, 6);
    }

    [Fact]
    public void A_barycentre_is_placed_through_the_parents_of_what_orbits_it()
    {
        var orbits = Orbits(
            """{ "timestamp":"2026-10-03T19:25:35Z", "event":"ScanBaryCentre", "BodyID":5, "SemiMajorAxis":299792458000, "Eccentricity":0, "OrbitalInclination":0, "Periapsis":0, "OrbitalPeriod":1000000000, "AscendingNode":0, "MeanAnomaly":0 }""",
            Scan(6, "Beta 3 a", 10 * C, """{ "Null":5 }, { "Star":0 }"""),
            Scan(7, "Beta 3 b", 10 * C, """{ "Null":5 }, { "Star":0 }""", meanAnomaly: 180));

        Assert.Equal(1010, orbits.Position("Beta 3 a", Epoch)!.Value.X, 6);
        Assert.Equal(990, orbits.Position("Beta 3 b", Epoch)!.Value.X, 6);
    }

    [Fact]
    public void A_body_of_unknown_ancestry_has_no_place()
    {
        var orbits = Orbits(Scan(2, "Gamma 1 a", 2 * C, """{ "Planet":1 }, { "Star":0 }"""));

        Assert.Null(orbits.Position("Gamma 1 a", Epoch));
    }

    /// <summary>From the journal: the game said 588.877 ls, the orbit says the same.</summary>
    [Fact]
    public void The_orbit_of_a_real_scan_gives_its_distance_from_the_arrival_star()
    {
        var orbits = Orbits(
            """{ "timestamp":"2026-10-03T19:20:00Z", "event":"Scan", "BodyName":"Core Sys Sector YO-R a4-0", "BodyID":0, "DistanceFromArrivalLS":0, "StarType":"M" }""",
            """{ "timestamp":"2026-10-03T19:25:35Z", "event":"Scan", "BodyName":"Core Sys Sector YO-R a4-0 2", "BodyID":15, "Parents":[ {"Star":0} ], "DistanceFromArrivalLS":588.877179, "SemiMajorAxis":176534795761.1084, "Eccentricity":6e-05, "OrbitalInclination":0.013006, "Periapsis":88.644995, "OrbitalPeriod":457593792.676926, "AscendingNode":54.722536, "MeanAnomaly":125.663788 }""");

        var p = orbits.Position("Core Sys Sector YO-R a4-0 2", Epoch)!.Value;

        Assert.Equal(588.877, Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z), 1);
        Assert.Equal("Core Sys Sector YO-R a4-0", orbits.ArrivalStar);
    }

    [Fact]
    public void Supercruise_time_grows_much_slower_than_distance()
    {
        Assert.InRange(VisitPlanner.SupercruiseSeconds(10), 15, 30);
        Assert.InRange(VisitPlanner.SupercruiseSeconds(1000), 120, 160);
        Assert.InRange(VisitPlanner.SupercruiseSeconds(10_000), 190, 240);
    }

    [Fact]
    public void The_visit_takes_the_near_bodies_together_before_the_far_one()
    {
        Point3[] stops = [new(1000, 0, 0), new(10, 0, 0), new(12, 3, 0)];

        var order = VisitPlanner.Order(new Point3(0, 0, 0), stops);

        Assert.Equal([1, 2, 0], order.Select(o => o.Index));
        Assert.Equal(VisitPlanner.SupercruiseSeconds(10), order[0].Seconds, 6);
    }

    [Fact]
    public void Many_bodies_still_get_an_order_through_each_of_them()
    {
        var stops = Enumerable.Range(0, 20).Select(i => new Point3(i * 100 % 700, i * 37 % 300, 0)).ToArray();

        var order = VisitPlanner.Order(new Point3(0, 0, 0), stops);

        Assert.Equal(Enumerable.Range(0, 20), order.Select(o => o.Index).Order());
    }
}
