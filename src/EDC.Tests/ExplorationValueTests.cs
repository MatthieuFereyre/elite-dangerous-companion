using EDC.Companion;
using EDC.Core;
using Xunit;

namespace EDC.Tests;

public class ExplorationValueTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 20, 0, 0, DateTimeKind.Utc);

    private static string Line(string time, string body) => $"{{ \"timestamp\":\"2026-10-09T{time}Z\", {body} }}";

    private static string Planet(string time, string system, string body, string planetClass, double mass, bool discovered = true, bool mapped = false, string terraform = "") =>
        Line(time, $"\"event\":\"Scan\", \"ScanType\":\"Detailed\", \"StarSystem\":\"{system}\", \"BodyName\":\"{body}\", \"SystemAddress\":1, " +
                   $"\"PlanetClass\":\"{planetClass}\", \"TerraformState\":\"{terraform}\", \"MassEM\":{mass.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
                   $"\"WasDiscovered\":{(discovered ? "true" : "false")}, \"WasMapped\":{(mapped ? "true" : "false")}");

    private static string Star(string time, string system, string type, double mass, bool discovered = true) =>
        Line(time, $"\"event\":\"Scan\", \"ScanType\":\"AutoScan\", \"StarSystem\":\"{system}\", \"BodyName\":\"{system}\", \"SystemAddress\":1, " +
                   $"\"StarType\":\"{type}\", \"StellarMass\":{mass.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"WasDiscovered\":{(discovered ? "true" : "false")}, \"WasMapped\":false");

    private static string Map(string time, string body, int probes, int target) =>
        Line(time, $"\"event\":\"SAAScanComplete\", \"BodyName\":\"{body}\", \"SystemAddress\":1, \"ProbesUsed\":{probes}, \"EfficiencyTarget\":{target}");

    private static string Sell(string time, params string[] systems) =>
        Line(time, $"\"event\":\"MultiSellExplorationData\", \"Discovered\":[ {string.Join(", ", systems.Select(s => $"{{ \"SystemName\":\"{s}\", \"NumBodies\":1 }}"))} ], \"BaseValue\":1, \"Bonus\":0, \"TotalEarnings\":1");

    private static GameState Replay(params string[] lines)
    {
        var state = new GameState();
        foreach (var line in lines)
            state.Apply(line);
        return state;
    }

    private static GameStatus Flying() => new(1L << 24, 0, 0, 16, 50_000_000, "Clean");

    [Fact]
    public void A_body_is_worth_what_the_formula_of_the_players_says_scanned_then_mapped()
    {
        Assert.Equal(1_208, Replay(Star("19:00:00", "Alpha", "M", 0.421875)).ExplorationUnsold);
        Assert.Equal(3_140, Replay(Star("19:00:00", "Alpha", "M", 0.421875, discovered: false)).ExplorationUnsold);
        Assert.Equal(1_300, Replay(Planet("19:00:00", "Alpha", "Alpha 1", "Rocky body", 0.05, discovered: false)).ExplorationUnsold);
        Assert.Equal(101_341, Replay(Planet("19:00:00", "Alpha", "Alpha 2", "Water world", 0.975783)).ExplorationUnsold);
        // Mapped first, within the probes of the target: the efficiency bonus.
        Assert.Equal(1_333_171, Replay(Planet("19:00:00", "Alpha", "Alpha 2", "Water world", 0.975783), Map("19:01:00", "Alpha 2", 7, 7)).ExplorationUnsold);
        // An Earth-like world someone mapped before.
        Assert.Equal(1_536_321, Replay(Planet("19:00:00", "Alpha", "Alpha 3", "Earthlike body", 1, mapped: true), Map("19:01:00", "Alpha 3", 5, 6)).ExplorationUnsold);
    }

    [Fact]
    public void The_data_is_counted_until_sold_and_lost_with_the_ship()
    {
        var state = Replay(
            Star("19:00:00", "Alpha", "M", 0.421875),
            Planet("19:00:10", "Alpha", "Alpha 2", "Water world", 0.975783),
            Planet("19:00:10", "Alpha", "Alpha 2", "Water world", 0.975783),
            Star("19:10:00", "Beta", "M", 0.421875, discovered: false));
        Assert.Equal(1_208 + 101_341 + 3_140, state.ExplorationUnsold);

        state.Apply(Sell("19:20:00", "Alpha"));
        Assert.Equal(3_140, state.ExplorationUnsold);

        state.Apply(Line("19:30:00", "\"event\":\"Died\""));
        Assert.Equal(0, state.ExplorationUnsold);
    }

    [Fact]
    public void A_navigation_beacon_scan_is_worth_nothing_Universal_Cartographics_does_not_buy_it()
    {
        var beacon = Planet("19:00:00", "Alpha", "Alpha 2", "Water world", 0.975783).Replace("\"Detailed\"", "\"NavBeaconDetail\"");
        Assert.Equal(0, Replay(beacon).ExplorationUnsold);
    }

    [Fact]
    public void The_banner_tells_the_data_to_sell_beside_the_bounties()
    {
        var state = Replay(
            Line("19:00:00", "\"event\":\"Bounty\", \"Faction\":\"Feds\", \"TotalReward\":4000000"),
            Planet("19:00:10", "Alpha", "Alpha 2", "Water world", 0.975783), Map("19:01:00", "Alpha 2", 7, 7));

        var chips = Briefing.Banner(state, Flying(), PadBattery.None, Now);
        Assert.Contains(chips, c => c.Text == "Primes 4,0 M Cr" && c.Tone == Tone.Good);
        Assert.Contains(chips, c => c.Text == "Exploration 1,3 M Cr" && c.Tone == Tone.Good);
        Assert.DoesNotContain(Briefing.Banner(new GameState(), Flying(), PadBattery.None, Now), c => c.Text.StartsWith("Exploration"));
    }
}
