using System.Text.Json;
using EDC.Companion;
using Xunit;

namespace EDC.Tests;

/// <summary>Any planet is worth telling when mapping it pays above the threshold, or when it is a first the player asked for.</summary>
public sealed class NotableClassTests
{
    private const long Address = 77;

    private static string Line(string body) => $"{{ \"timestamp\":\"2026-10-09T19:00:00Z\", {body} }}";

    private static string Planet(string name, string planetClass, double mass, bool discovered = true, bool mapped = true, string terraform = "", bool landable = false) =>
        Line($"\"event\":\"Scan\", \"ScanType\":\"Detailed\", \"StarSystem\":\"Epsilon\", \"SystemAddress\":{Address}, \"BodyName\":\"{name}\", \"BodyID\":4, \"Landable\":{(landable ? "true" : "false")}, " +
             $"\"DistanceFromArrivalLS\":800, \"PlanetClass\":\"{planetClass}\", \"TerraformState\":\"{terraform}\", \"MassEM\":{mass.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
             $"\"WasDiscovered\":{(discovered ? "true" : "false")}, \"WasMapped\":{(mapped ? "true" : "false")}");

    private static GameState Replay(params string[] lines)
    {
        var state = new GameState();
        state.Apply(Line($"\"event\":\"FSDJump\", \"StarSystem\":\"Epsilon\", \"SystemAddress\":{Address}, \"StarPos\":[0,0,0], \"FuelUsed\":1, \"FuelLevel\":16"));
        foreach (var line in lines)
            state.Apply(line);
        return state;
    }

    private static NotableBody? FromScan(string line)
    {
        using var document = JsonDocument.Parse(line);
        return NotableBodies.FromScan(document.RootElement);
    }

    // Short, for a line of the panel to hold on one line over the game.
    [Theory]
    [InlineData("High metal content body", "", "Métallique")]
    [InlineData("High metal content body", "Terraformable", "Métallique terraformable")]
    [InlineData("Metal rich body", "", "Riche en métaux")]
    [InlineData("Rocky body", "Terraformable", "Rocheux terraformable")]
    [InlineData("Rocky ice body", "", "Roche et glace")]
    [InlineData("Water world", "Terraformable", "Aquatique terraformable")]
    [InlineData("Earthlike body", "", "Terre")]
    [InlineData("Ammonia world", "", "Ammoniac")]
    [InlineData("Sudarsky class II gas giant", "", "Géante gazeuse")]
    [InlineData("Icy body", "", "Glacé")]
    public void Every_planet_class_is_known_and_named(string planetClass, string terraform, string label)
    {
        Assert.Equal(label, FromScan(Planet("Epsilon 1", planetClass, 1, terraform: terraform))?.Label);
    }

    [Fact]
    public void A_planet_says_whether_a_ship_can_land_there_from_the_journal_edsm_and_spansh()
    {
        Assert.True(FromScan(Planet("Epsilon 1", "Rocky body", 1, landable: true))?.Landable);
        Assert.False(FromScan(Planet("Epsilon 1", "Rocky body", 1))?.Landable);

        var edsm = NotableBodies.Parse("""{"bodies":[{"name":"Epsilon 2","subType":"Rocky body","isLandable":true},{"name":"Epsilon 3","subType":"Icy body","isLandable":false}]}""");
        Assert.Equal([true, false], edsm.Select(b => b.Landable));

        var spansh = NotableBodies.ParseSpansh("""{"results":[{"system_id64":5,"name":"Zeta 1","subtype":"Rocky body","is_landable":true}]}""");
        Assert.True(spansh.Single().Body.Landable);
    }

    [Fact]
    public void The_panel_marks_a_planet_a_ship_can_land_on()
    {
        var state = Replay(Planet("Epsilon 1", "Rocky body", 1, landable: true), Planet("Epsilon 2", "Water world", 1));
        state.MinNotableValue = 0;

        var lines = Briefing.Panel(state, new GameStatus(1L << 24, 0, 0, 16, 50_000_000, "Clean"), DateTime.UtcNow).Where(l => !l.Header).ToList();

        Assert.True(lines.Single(l => l.Text.StartsWith("Rocheux")).Landable);
        Assert.False(lines.Single(l => l.Text.StartsWith("Aquatique")).Landable);
    }

    [Fact]
    public void The_threshold_alone_decides_whatever_the_class()
    {
        var state = Replay(Planet("Epsilon 1", "High metal content body", 2), Planet("Epsilon 2", "Icy body", 0.01));
        state.MinNotableValue = 0;
        var worth = state.NotableBodies.Single(b => b.Name == "Epsilon 1").Value!.Value;

        state.MinNotableValue = worth;
        Assert.Equal(["Epsilon 1"], state.NotableBodies.Select(b => b.Name));
        state.MinNotableValue = worth + 1;
        Assert.Empty(state.NotableBodies);
    }

    [Fact]
    public void First_discoveries_and_first_mappings_are_told_below_the_threshold_when_asked()
    {
        var state = Replay(
            Planet("Epsilon 1", "Icy body", 0.01, discovered: false, mapped: false),
            Planet("Epsilon 2", "Icy body", 0.01, discovered: true, mapped: false),
            Planet("Epsilon 3", "Icy body", 0.01));
        state.MinNotableValue = 10_000_000;
        Assert.Empty(state.NotableBodies);

        state.FirstDiscoveries = true;
        Assert.Equal(["Epsilon 1"], state.NotableBodies.Select(b => b.Name));

        state.FirstMappings = true;
        Assert.Equal(["Epsilon 1", "Epsilon 2"], state.NotableBodies.Select(b => b.Name).Order());
    }

    [Fact]
    public void A_planet_whose_mass_is_not_known_is_told_whatever_the_threshold()
    {
        var bodies = NotableBodies.Parse("""{"bodies":[{"name":"Epsilon 9","subType":"Icy body","distanceToArrival":40}]}""");
        Assert.Null(bodies.Single().Value);

        var state = Replay();
        state.MinNotableValue = 10_000_000;
        state.SetNotableBodies(Address, bodies);
        Assert.Equal(["Epsilon 9"], state.NotableBodies.Select(b => b.Name));
    }

    [Fact]
    public void Spansh_is_asked_for_the_bodies_of_the_route_worth_half_the_threshold_and_more()
    {
        using var query = JsonDocument.Parse(NotableBodies.RouteQuery(["1", "2"], 700_000));
        var filters = query.RootElement.GetProperty("filters");

        Assert.Equal("350000", filters.GetProperty("estimated_mapping_value").GetProperty("min").GetString());
        Assert.Equal(["1", "2"], filters.GetProperty("system_id64").GetProperty("value").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("Planet", filters.GetProperty("type").GetProperty("value")[0].GetString());
    }
}
