using System.Globalization;
using EDC.Companion;
using Xunit;

namespace EDC.Tests;

/// <summary>The panel lists the planets worth mapping in the order that takes the least supercruise.</summary>
public sealed class VisitPlanTests
{
    private const long Address = 99;
    private const double C = 299_792_458;
    private static readonly DateTime Now = new(2026, 10, 9, 19, 30, 0, DateTimeKind.Utc);

    private static string Line(string body) => $"{{ \"timestamp\":\"2026-10-09T19:00:00Z\", {body} }}";

    private static string Star() => Line($"\"event\":\"Scan\", \"ScanType\":\"AutoScan\", \"StarSystem\":\"Delta\", \"SystemAddress\":{Address}, \"BodyName\":\"Delta A\", \"BodyID\":0, \"DistanceFromArrivalLS\":0, \"StarType\":\"K\", \"StellarMass\":0.8");

    // A terraformable water world on a circular orbit, at <paramref name="degrees"/> along it at the scan.
    private static string World(int id, string name, double ls, double degrees) =>
        Line($"\"event\":\"Scan\", \"ScanType\":\"Detailed\", \"StarSystem\":\"Delta\", \"SystemAddress\":{Address}, \"BodyName\":\"{name}\", \"BodyID\":{id}, " +
             $"\"Parents\":[ {{\"Star\":0}} ], \"DistanceFromArrivalLS\":{ls.ToString(CultureInfo.InvariantCulture)}, \"PlanetClass\":\"Water world\", \"TerraformState\":\"Terraformable\", " +
             $"\"MassEM\":0.5, \"WasDiscovered\":true, \"WasMapped\":false, \"SemiMajorAxis\":{(ls * C).ToString(CultureInfo.InvariantCulture)}, \"Eccentricity\":0, " +
             $"\"OrbitalInclination\":0, \"Periapsis\":0, \"OrbitalPeriod\":1e12, \"AscendingNode\":0, \"MeanAnomaly\":{degrees.ToString(CultureInfo.InvariantCulture)}");

    private static GameState Replay(params string[] lines)
    {
        var state = new GameState();
        state.Apply(Line($"\"event\":\"FSDJump\", \"StarSystem\":\"Delta\", \"SystemAddress\":{Address}, \"StarPos\":[0,0,0], \"FuelUsed\":1, \"FuelLevel\":16"));
        foreach (var line in lines)
            state.Apply(line);
        return state;
    }

    private static GameStatus Flying() => new(1L << 24, 0, 0, 16, 50_000_000, "Clean");

    [Fact]
    public void The_planets_come_in_the_order_of_the_shortest_trip_not_of_their_names()
    {
        // B 1 and A 2 lie side by side at 1,000 ls; A 1, at 1,000 ls too, is on the other side of the star.
        var state = Replay(Star(),
            World(1, "Delta A 1", 1000, 180),
            World(2, "Delta A 2", 1000, 2),
            World(3, "Delta B 1", 1000, 0));

        var lines = Briefing.Panel(state, Flying(), Now).Select(l => l.Text).ToList();

        var here = lines.FindIndex(l => l.StartsWith("Ici"));
        // No supercruise time: the FSD's engineering, or an overcharged one, changes it beyond any estimate.
        Assert.Equal("Ici (3)", lines[here]);
        Assert.DoesNotContain(lines, l => l.Contains(" min") || l.Contains(" s ·") || l.EndsWith(" s"));
        Assert.Contains("B 1", lines[here + 1] + lines[here + 2]);
        Assert.Contains("A 2", lines[here + 1] + lines[here + 2]);
        Assert.Contains("A 1", lines[here + 3]);
    }

    [Fact]
    public void From_a_planet_already_reached_the_next_ones_are_counted_from_there()
    {
        var state = Replay(Star(),
            World(1, "Delta A 1", 1000, 180),
            World(2, "Delta A 2", 1000, 2),
            World(3, "Delta B 1", 1000, 0),
            Line($"\"event\":\"SupercruiseExit\", \"StarSystem\":\"Delta\", \"SystemAddress\":{Address}, \"Body\":\"Delta A 1\", \"BodyID\":1, \"BodyType\":\"Planet\""));

        var plan = state.VisitPlan(Now);

        Assert.Equal("Delta A 1", plan[0].Body.Name);
        Assert.Equal(0, plan[0].Seconds!.Value, 6);
    }

    [Fact]
    public void A_planet_known_only_from_edsm_comes_last_by_its_distance()
    {
        var state = Replay(Star(), World(2, "Delta A 2", 1000, 2));
        state.SetNotableBodies(Address, [new NotableBody("Delta C 1", PlanetClass.EarthLike, false, 50, Value: 3_000_000)]);

        var plan = state.VisitPlan(Now);

        Assert.Equal(["Delta A 2", "Delta C 1"], plan.Select(p => p.Body.Name));
        Assert.Null(plan[1].Seconds);
    }
}
