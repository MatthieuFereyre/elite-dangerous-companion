using EDC.Companion;
using Xunit;

namespace EDC.Tests;

/// <summary>
/// In the surface scanner's view, the panel tells the planet probed: what mapping it pays, where a ship
/// lands, its rarer materials, its signals with what their genera pay, and what is left to map here.
/// </summary>
public sealed class SurveyTests
{
    private const long Address = 77;

    private static string Line(string body) => $"{{ \"timestamp\":\"2026-10-30T19:00:00Z\", {body} }}";

    private static string Scan(string name, string planetClass, string extra = "") =>
        Line($"\"event\":\"Scan\", \"ScanType\":\"Detailed\", \"StarSystem\":\"Epsilon\", \"SystemAddress\":{Address}, \"BodyName\":\"{name}\", \"BodyID\":4, " +
             $"\"DistanceFromArrivalLS\":800, \"PlanetClass\":\"{planetClass}\", \"TerraformState\":\"\", \"MassEM\":1, \"WasDiscovered\":true, \"WasMapped\":true{extra}");

    // A landable rock at half a g, 239 K, without air; two rare raws among the common.
    private const string Rock = ", \"Landable\":true, \"SurfaceGravity\":4.903, \"SurfaceTemperature\":239.4, \"Atmosphere\":\"\", \"Materials\":[ " +
        "{ \"Name\":\"iron\", \"Percent\":20.6 }, { \"Name\":\"cadmium\", \"Percent\":1.5 }, { \"Name\":\"polonium\", \"Percent\":1.21 }, " +
        "{ \"Name\":\"tellurium\", \"Name_Localised\":\"Tellure\", \"Percent\":0.9 } ]";

    private static readonly DateTime Now = new(2026, 10, 30, 19, 30, 0, DateTimeKind.Utc);

    private static GameState Replay(params string[] lines)
    {
        var state = new GameState();
        state.Apply(Line($"\"event\":\"FSDJump\", \"StarSystem\":\"Epsilon\", \"SystemAddress\":{Address}, \"StarPos\":[0,0,0], \"FuelUsed\":1, \"FuelLevel\":16"));
        foreach (var line in lines)
            state.Apply(line);
        return state;
    }

    private static GameStatus Probing(string? body) => new(1L << 24 | 1L << 4, 0, 10, 16, 50_000_000, "Clean", Destination: body);

    private static List<string> Card(GameState state, string? body = "Epsilon 1") =>
        Briefing.Panel(state, Probing(body), Now).Select(l => l.Text).ToList();

    [Fact]
    public void The_panel_shows_in_the_surface_scanner_s_view() =>
        Assert.True(Briefing.PanelVisible(Probing("Epsilon 1")));

    [Fact]
    public void The_planet_probed_tells_its_worth_where_to_land_and_its_rare_materials()
    {
        var state = Replay(Scan("Epsilon 1", "Rocky body", Rock));

        var card = Card(state);

        Assert.Equal("1 · Rocheux", card[0]);
        Assert.EndsWith(" Cr", card[1]);
        Assert.Equal("0,5 g · 239 K · sans atmosphère", card[2]);
        // Grade 4 before grade 3, the common ones left out.
        Assert.Equal("Polonium 1,2 % · Tellure 0,9 % · Cadmium 1,5 %", card[3]);
    }

    [Fact]
    public void A_heavy_planet_is_told_in_red()
    {
        var state = Replay(Scan("Epsilon 1", "Rocky body", Rock.Replace("4.903", "24.5")));

        var landing = Briefing.Panel(state, Probing("Epsilon 1"), Now).Single(l => l.Text.Contains(" g · "));

        Assert.Equal(("2,5 g · 239 K · sans atmosphère", Tone.Alert), (landing.Text, landing.Tone));
    }

    [Fact]
    public void A_planet_no_ship_lands_on_says_nothing_of_landing_nor_materials()
    {
        var state = Replay(Scan("Epsilon 1", "High metal content body",
            ", \"Landable\":false, \"SurfaceGravity\":9.8, \"SurfaceTemperature\":400, \"Atmosphere\":\"thick nitrogen atmosphere\", \"Materials\":[ { \"Name\":\"polonium\", \"Percent\":1 } ]"));

        var card = Card(state);

        Assert.DoesNotContain(card, l => l.Contains(" g · ") || l.Contains("Polonium"));
    }

    [Fact]
    public void The_signals_are_counted_then_named_with_what_their_genera_pay_once_probed()
    {
        var state = Replay(Scan("Epsilon 1", "Rocky body", Rock),
            Line($"\"event\":\"FSSBodySignals\", \"BodyName\":\"Epsilon 1\", \"BodyID\":4, \"SystemAddress\":{Address}, \"Signals\":[ " +
                 "{ \"Type\":\"$SAA_SignalType_Biological;\", \"Count\":2 }, { \"Type\":\"$SAA_SignalType_Geological;\", \"Count\":3 } ]"));
        Assert.Contains("Biologique 2 · Géologique 3", Card(state));

        state.Apply(Line($"\"event\":\"SAASignalsFound\", \"BodyName\":\"Epsilon 1\", \"BodyID\":4, \"SystemAddress\":{Address}, \"Signals\":[ " +
                         "{ \"Type\":\"$SAA_SignalType_Biological;\", \"Count\":2 }, { \"Type\":\"$SAA_SignalType_Geological;\", \"Count\":3 } ], " +
                         "\"Genuses\":[ { \"Genus\":\"$Codex_Ent_Bacterial_Genus_Name;\", \"Genus_Localised\":\"Bacterium\" }, " +
                         "{ \"Genus\":\"$Codex_Ent_Vents_Name;\", \"Genus_Localised\":\"Amphora Plant\" } ]"));
        var card = Card(state);

        Assert.Contains("Biologique 2 · Géologique 3", card);
        // From the cheapest species of the genus to the dearest; a genus of one species, its price.
        Assert.Contains("Bacterium · 1,0 à 8,4 M Cr", card);
        Assert.Contains("Amphora Plant · 1,6 M Cr", card);
    }

    [Fact]
    public void What_is_left_to_map_here_and_the_next_planet_are_told()
    {
        var state = Replay(Scan("Epsilon 1", "Rocky body", Rock), Scan("Epsilon 2", "Water world"), Scan("Epsilon 3", "Water world"),
            Line($"\"event\":\"SAAScanComplete\", \"BodyName\":\"Epsilon 2\", \"SystemAddress\":{Address}, \"BodyID\":5, \"ProbesUsed\":5, \"EfficiencyTarget\":6"));
        state.MinNotableValue = 0;

        Assert.Equal("1 cartographiée · 2 à faire · suivante : 3 · 800 ls", Card(state)[^1]);

        state.Apply(Line($"\"event\":\"SAAScanComplete\", \"BodyName\":\"Epsilon 3\", \"SystemAddress\":{Address}, \"BodyID\":6, \"ProbesUsed\":5, \"EfficiencyTarget\":6"));
        Assert.Equal("2 cartographiées · 1 à faire", Card(state)[^1]);
    }

    [Fact]
    public void A_planet_mapped_before_stays_so_when_the_scan_after_the_commander_s_mapping_says_otherwise()
    {
        // As the game wrote it for Col 173 Sector MO-Q d5-48 B 2: mapped before, then « not mapped » once probed.
        var state = Replay(
            Scan("Epsilon 1", "High metal content body", "").Replace("\"Detailed\"", "\"AutoScan\""),
            Line($"\"event\":\"SAAScanComplete\", \"BodyName\":\"Epsilon 1\", \"SystemAddress\":{Address}, \"BodyID\":4, \"ProbesUsed\":4, \"EfficiencyTarget\":6"),
            Scan("Epsilon 1", "High metal content body", "").Replace("\"WasMapped\":true", "\"WasMapped\":false"));
        var before = Replay(Scan("Epsilon 1", "High metal content body", "")).PlanetHere("Epsilon 1");

        Assert.False(state.ScannedHere("Epsilon 1")?.FirstMapping);
        Assert.Equal((false, before?.Value), (state.PlanetHere("Epsilon 1")?.FirstMapping, state.PlanetHere("Epsilon 1")?.Value));
        Assert.DoesNotContain(Card(state), l => l.Contains("1re cartographie"));
    }

    [Fact]
    public void A_planet_not_known_shows_nothing()
    {
        var state = Replay(Scan("Epsilon 1", "Rocky body", Rock));

        Assert.Empty(Card(state, "Epsilon 9"));
        Assert.Empty(Card(state, null));
    }
}
