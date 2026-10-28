using System.Globalization;
using EDC.Companion;
using EDC.Core;
using Xunit;

namespace EDC.Tests;

public sealed class ExplorationTests
{
    private static readonly DateTime Arrival = new(2026, 10, 9, 19, 0, 0, DateTimeKind.Utc);

    private static string Line(string time, string body) => $"{{ \"timestamp\":\"2026-10-09T{time}Z\", {body} }}";

    private static string Ls(int ls) => ls.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"));

    private static string Jump(string time, string system, long address) =>
        Line(time, $"\"event\":\"FSDJump\", \"StarSystem\":\"{system}\", \"SystemAddress\":{address}, \"FuelUsed\":2, \"FuelLevel\":16");

    private static string Honk(string time, string system, long address) =>
        Line(time, $"\"event\":\"FSSDiscoveryScan\", \"Progress\":0.18, \"BodyCount\":9, \"NonBodyCount\":15, \"SystemName\":\"{system}\", \"SystemAddress\":{address}");

    private static string Sell(string time, params string[] systems) =>
        Line(time, $"\"event\":\"MultiSellExplorationData\", \"Discovered\":[ {string.Join(", ", systems.Select(s => $"{{ \"SystemName\":\"{s}\", \"NumBodies\":3 }}"))} ], \"BaseValue\":28773, \"Bonus\":0, \"TotalEarnings\":28773");

    private static string Map(string time, string body, long address) =>
        Line(time, $"\"event\":\"SAAScanComplete\", \"BodyName\":\"{body}\", \"SystemAddress\":{address}, \"BodyID\":12, \"ProbesUsed\":5, \"EfficiencyTarget\":7");

    // The app's default threshold.
    private static GameState Replay(params string[] lines)
    {
        var state = new GameState { MinNotableValue = 700_000 };
        foreach (var line in lines)
            state.Apply(line);
        return state;
    }

    private static GameStatus Flying() => new(1L << 24, 0, 0, 16, 50_000_000, "Clean");
    private static GameStatus Docked() => new((1L << 24) | 1, 0, 0, 16, 50_000_000, "Clean");

    private static Warning? Warning(GameState state, int seconds, GameStatus? status = null) =>
        Briefing.Warning(state, status ?? Flying(), Arrival.AddSeconds(seconds));

    [Fact]
    public void A_system_not_yet_honked_calls_for_the_discovery_scanner_until_it_is()
    {
        var state = Replay(Jump("19:00:00", "Itela", 5336236820851));

        Assert.Equal(new Warning("Coup de klaxon", "Système pas encore scanné", Big: true, Tone.Info), Warning(state, 5));
        Assert.Equal(new Warning("Coup de klaxon", "Système pas encore scanné", Big: false, Tone.Info), Warning(state, 30));
        Assert.Null(Warning(state, 30, Docked()));

        state.Apply(Honk("19:00:40", "Itela", 5336236820851));
        Assert.Null(Warning(state, 45));
    }

    [Fact]
    public void The_honk_reminder_can_be_turned_off_without_the_other_warnings()
    {
        var state = Replay(Jump("19:00:00", "Itela", 5336236820851));

        Assert.Null(Briefing.Warning(state, Flying(), Arrival.AddSeconds(5), honk: false));
        Assert.True(new EDC.Core.PlayerSettings().HonkReminder);
    }

    [Fact]
    public void A_system_honked_before_needs_no_reminder_sold_or_not()
    {
        var state = Replay(
            Jump("18:00:00", "Itela", 1), Honk("18:00:10", "Itela", 1),
            Jump("18:10:00", "Porrima", 2), Honk("18:10:10", "Porrima", 2), Sell("18:30:00", "Porrima"),
            Jump("19:00:00", "Itela", 1));
        Assert.Null(Warning(state, 5));

        state.Apply(Jump("19:00:00", "Porrima", 2));
        Assert.Null(Warning(state, 5));
    }

    [Fact]
    public void Dying_loses_what_was_not_sold_and_its_systems_are_to_honk_again()
    {
        var state = Replay(
            Jump("18:00:00", "Itela", 1), Honk("18:00:10", "Itela", 1),
            Jump("18:10:00", "Porrima", 2), Honk("18:10:10", "Porrima", 2), Sell("18:30:00", "Porrima"),
            Line("18:40:00", "\"event\":\"Died\""),
            Jump("19:00:00", "Itela", 1));
        Assert.Equal("Coup de klaxon", Warning(state, 5)?.Title);

        state.Apply(Jump("19:00:00", "Porrima", 2));
        Assert.Null(Warning(state, 5));
    }

    [Fact]
    public void The_reminder_stays_while_the_FSD_charges_and_leaves_with_the_jump()
    {
        var state = Replay(Jump("19:00:00", "Itela", 1));
        Assert.NotNull(Warning(state, 30, new GameStatus((1L << 24) | (1L << 17), 0, 0, 16, 50_000_000, "Clean")));
        Assert.Null(Warning(state, 30, new GameStatus((1L << 24) | (1L << 30), 0, 0, 16, 50_000_000, "Clean")));
    }

    [Fact]
    public void The_shields_and_the_tank_come_before_the_honk()
    {
        var state = Replay(Jump("19:00:00", "Itela", 1), Line("19:00:01", "\"event\":\"ShieldState\", \"ShieldsUp\":false"));
        Assert.Equal("Boucliers tombés", Warning(state, 5)?.Title);
        Assert.Equal("Boucliers tombés", Warning(state, 60)?.Title);
    }

    [Fact]
    public void Each_new_system_asks_for_its_planets()
    {
        var state = Replay(Jump("19:00:00", "LHS 3447", 5306465653474));
        Assert.True(state.SystemBodiesWanted);
        state.SystemBodiesWanted = false;

        state.Apply(Line("19:01:00", "\"event\":\"SupercruiseExit\", \"StarSystem\":\"LHS 3447\", \"SystemAddress\":5306465653474"));
        Assert.False(state.SystemBodiesWanted);

        state.Apply(Jump("19:05:00", "Itela", 5336236820851));
        Assert.True(state.SystemBodiesWanted);
    }

    [Fact]
    public void The_banner_tells_neither_the_value_of_the_system_nor_its_planets_the_panel_lists_them()
    {
        var state = Replay(Jump("19:00:00", "LHS 3447", 5306465653474));
        state.SetNotableBodies(5306465653474, NotableBodies.Parse(Bodies));
        Assert.NotEmpty(Notable(state));
        Assert.DoesNotContain(Briefing.Banner(state, Flying(), PadBattery.None, Arrival.AddSeconds(5)), c => c.Text.StartsWith("Système"));
    }

    [Fact]
    public void A_planet_mapped_then_lost_with_the_ship_is_to_map_again()
    {
        var state = Replay(
            Jump("18:00:00", "LHS 3447", 5306465653474), Honk("18:00:10", "LHS 3447", 5306465653474),
            Map("18:10:00", "LHS 3447 B 1", 5306465653474), Map("18:20:00", "LHS 3447 A 5", 5306465653474),
            Line("18:40:00", "\"event\":\"Died\""),
            Jump("19:00:00", "LHS 3447", 5306465653474));
        state.SetNotableBodies(5306465653474, NotableBodies.Parse(Bodies));
        Assert.Equal(["LHS 3447 B 1", "LHS 3447 B 2", "LHS 3447 A 5"], Notable(state));
    }

    [Fact]
    public void A_planet_mapped_after_the_system_was_sold_is_lost_with_the_ship_but_not_the_honk()
    {
        var state = Replay(
            Jump("18:00:00", "LHS 3447", 5306465653474), Honk("18:00:10", "LHS 3447", 5306465653474), Sell("18:05:00", "LHS 3447"),
            Map("18:10:00", "LHS 3447 B 1", 5306465653474),
            Line("18:40:00", "\"event\":\"Died\""),
            Jump("19:00:00", "LHS 3447", 5306465653474));
        state.SetNotableBodies(5306465653474, NotableBodies.Parse(Bodies));
        Assert.Equal(["LHS 3447 B 1", "LHS 3447 B 2", "LHS 3447 A 5"], Notable(state));
        Assert.Null(Warning(state, 5));
    }

    [Fact]
    public void What_was_sold_stays_through_a_death_even_scanned_again()
    {
        var state = Replay(
            Jump("18:00:00", "LHS 3447", 5306465653474), Honk("18:00:10", "LHS 3447", 5306465653474),
            Map("18:10:00", "LHS 3447 B 1", 5306465653474), Sell("18:30:00", "LHS 3447"),
            Honk("18:31:00", "LHS 3447", 5306465653474), Map("18:32:00", "LHS 3447 B 1", 5306465653474),
            Line("18:40:00", "\"event\":\"Died\""),
            Jump("19:00:00", "LHS 3447", 5306465653474));
        state.SetNotableBodies(5306465653474, NotableBodies.Parse(Bodies));
        Assert.Equal(["LHS 3447 B 2", "LHS 3447 A 5"], Notable(state));
        Assert.Null(Warning(state, 5));
    }

    private static string Scan(string time, string body, long address, string planetClass, string terraform = "", bool discovered = true, int ls = 1200, double mass = 1) =>
        Line(time, $"\"event\":\"Scan\", \"ScanType\":\"Detailed\", \"BodyName\":\"{body}\", \"BodyID\":3, \"SystemAddress\":{address}, " +
                   $"\"DistanceFromArrivalLS\":{ls}, \"TerraformState\":\"{terraform}\", \"PlanetClass\":\"{planetClass}\", \"MassEM\":{mass.ToString(CultureInfo.InvariantCulture)}, " +
                   $"\"WasDiscovered\":{(discovered ? "true" : "false")}, \"WasMapped\":false");

    private static List<string> Notable(GameState state) => state.NotableBodies.Select(b => b.Name).ToList();

    // In supercruise, the hyperdrive charging (Flags2 bit 19): leaving the system.
    private static GameStatus Leaving() => new((1L << 24) | (1L << 4) | (1L << 17), 1L << 19, 0, 16, 50_000_000, "Clean");

    [Fact]
    public void Charging_the_jump_with_a_valuable_planet_not_mapped_warns_that_it_stays_behind()
    {
        var state = Replay(Jump("19:00:00", "Itela", 5336236820851), Honk("19:00:10", "Itela", 5336236820851),
            Scan("19:01:00", "Itela A 4", 5336236820851, "Water world", "Terraformable"));

        var warning = Warning(state, 120, Leaving());

        Assert.Equal(("Planètes de valeur laissées ici", true), (warning?.Title, warning?.Big));
        Assert.StartsWith("A 4", warning?.Detail);
        Assert.Null(Warning(state, 120));

        state.Apply(Map("19:05:00", "Itela A 4", 5336236820851));
        Assert.Null(Warning(state, 400, Leaving()));
    }

    [Fact]
    public void A_scanned_planet_above_the_threshold_is_left_to_the_panel_the_best_paid_first_until_mapped()
    {
        var state = Replay(Jump("19:00:00", "Praea Euq TY-S b3-1", 2869977687585), Honk("19:00:10", "Praea Euq TY-S b3-1", 2869977687585),
            Scan("19:01:00", "Praea Euq TY-S b3-1 2", 2869977687585, "Rocky body"),
            Scan("19:01:10", "Praea Euq TY-S b3-1 3", 2869977687585, "High metal content body", "Terraformable", ls: 300));
        Assert.Equal(["Praea Euq TY-S b3-1 3"], Notable(state));
        // The panel on the left lists them: the banner does not repeat the most precious.
        Assert.DoesNotContain(Briefing.Banner(state, Flying(), PadBattery.None, Arrival.AddSeconds(5)), c => c.Text.StartsWith("Corps"));

        state.Apply(Scan("19:01:20", "Praea Euq TY-S b3-1 5", 2869977687585, "Water world", "Terraformable", discovered: false, ls: 2500));
        state.Apply(Scan("19:01:30", "Praea Euq TY-S b3-1 4", 2869977687585, "Earthlike body", ls: 1800));
        // A first discovery pays more than an earth-like found before.
        Assert.Equal(["Praea Euq TY-S b3-1 5", "Praea Euq TY-S b3-1 4", "Praea Euq TY-S b3-1 3"], Notable(state));

        state.Apply(Map("19:10:00", "Praea Euq TY-S b3-1 4", 2869977687585));
        state.Apply(Map("19:20:00", "Praea Euq TY-S b3-1 5", 2869977687585));
        state.Apply(Map("19:30:00", "Praea Euq TY-S b3-1 3", 2869977687585));
        Assert.Empty(Notable(state));
    }

    [Fact]
    public void The_planets_of_a_system_left_are_not_told_in_the_next()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1), Scan("19:01:00", "Alpha 1", 1, "Water world"), Jump("19:05:00", "Beta", 2));
        Assert.Empty(Notable(state));

        // A scan for another system than the one the commander is in is ignored.
        state.Apply(Scan("19:06:00", "Alpha 2", 1, "Earthlike body"));
        Assert.Empty(Notable(state));
    }

    // A system as EDSM's api-system-v1/bodies gives it, cut down to what is read.
    private const string Bodies = """
        {"id":13153,"id64":5306465653474,"name":"LHS 3447","bodyCount":4,"bodies":[
          {"name":"LHS 3447 A","type":"Star","subType":"M (Red dwarf) Star","distanceToArrival":0},
          {"name":"LHS 3447 A 5","type":"Planet","subType":"Water world","terraformingState":"Not terraformable","distanceToArrival":92190},
          {"name":"LHS 3447 B 1","type":"Planet","subType":"High metal content world","terraformingState":"Candidate for terraforming","distanceToArrival":1200},
          {"name":"LHS 3447 B 2","type":"Planet","subType":"Icy body","terraformingState":"Not terraformable","distanceToArrival":1300}]}
        """;

    [Fact]
    public void EDSM_tells_the_planets_of_a_known_system_before_any_scan()
    {
        // Their value is told apart, see A_notable_planet_is_worth_what_mapping_it_well_pays_whoever_tells_of_it.
        Assert.Equal(new[]
            {
                new NotableBody("LHS 3447 A 5", PlanetClass.Water, false, 92190),
                new NotableBody("LHS 3447 B 1", PlanetClass.HighMetal, true, 1200),
                new NotableBody("LHS 3447 B 2", PlanetClass.Icy, false, 1300),
            },
            NotableBodies.Parse(Bodies));
        Assert.Empty(NotableBodies.Parse("{}"));
        Assert.Empty(NotableBodies.Parse("[]"));

        var state = Replay(Jump("19:00:00", "LHS 3447", 5306465653474));
        state.SetNotableBodies(5306465653474, NotableBodies.Parse(Bodies));
        // Without their mass, their value is not known: all told, the nearest first.
        Assert.Equal(["LHS 3447 B 1", "LHS 3447 B 2", "LHS 3447 A 5"], Notable(state));

        // A late answer for a system left is dropped.
        state.Apply(Jump("19:05:00", "Itela", 5336236820851));
        state.SetNotableBodies(5306465653474, NotableBodies.Parse(Bodies));
        Assert.Empty(Notable(state));
    }

    [Fact]
    public void An_ammonia_world_comes_after_an_earth_like_and_before_a_water_world()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1),
            Scan("19:01:00", "Alpha 1", 1, "Water world", ls: 100),
            Scan("19:01:10", "Alpha 2", 1, "Ammonia world", ls: 900));
        Assert.Equal(["Alpha 2", "Alpha 1"], Notable(state));
    }

    // Two bodies as Spansh's api/bodies/search gives them, cut down to what is read; the second system's id64 is past 2^53.
    private const string Spansh = """
        {"count":2,"results":[
          {"name":"Beta 3","subtype":"Water world","terraforming_state":"Not terraformable","distance_to_arrival":540.5,"system_id64":2,"system_name":"Beta"},
          {"name":"Delta 1","subtype":"High metal content world","terraforming_state":"Terraformable","distance_to_arrival":12,"system_id64":9007199254740993,"system_name":"Delta"},
          {"name":"Delta 2","subtype":"Icy body","terraforming_state":"Not terraformable","distance_to_arrival":30,"system_id64":9007199254740993,"system_name":"Delta"}]}
        """;

    [Fact]
    public void Spansh_gives_the_notable_planets_of_the_route_by_system()
    {
        Assert.Equal(new[]
            {
                (2L, new NotableBody("Beta 3", PlanetClass.Water, false, 540.5)),
                (9007199254740993L, new NotableBody("Delta 1", PlanetClass.HighMetal, true, 12)),
                (9007199254740993L, new NotableBody("Delta 2", PlanetClass.Icy, false, 30)),
            },
            NotableBodies.ParseSpansh(Spansh));
        Assert.Empty(NotableBodies.ParseSpansh("{}"));
    }

    private static string NavRoute(string time, params (string System, long Address)[] hops) =>
        Line(time, $"\"event\":\"NavRoute\", \"Route\":[ {string.Join(", ", hops.Select(h => $"{{ \"StarSystem\":\"{h.System}\", \"SystemAddress\":{h.Address}, \"StarPos\":[0,0,0], \"StarClass\":\"K\" }}"))} ]");

    [Fact]
    public void The_panel_lists_the_planets_here_then_those_of_the_route_ahead_jump_by_jump()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1), NavRoute("19:00:05", ("Alpha", 1), ("Beta", 2), ("Gamma", 3), ("Delta", 4)));
        Assert.True(state.RouteNotableWanted);
        Assert.Empty(Briefing.Panel(state, Flying(), Arrival));

        state.SetRouteNotable(new Dictionary<long, List<NotableBody>>
        {
            [2] = [new NotableBody("Beta 3", PlanetClass.Water, false, 540, Value: 1_100_000)],
            // Delta 2's mass is not known: its system pays at least Delta 1's value.
            [4] = [new NotableBody("Delta 1", PlanetClass.Rocky, true, 12, Value: 900_000), new NotableBody("Delta 2", PlanetClass.EarthLike, false, 3000)],
        });
        state.Apply(Scan("19:01:00", "Alpha 1", 1, "Ammonia world", discovered: false, ls: 900));

        Assert.Equal(new[]
            {
                new PanelLine("Ici (1)", Tone.Info, Header: true),
                new PanelLine($"Monde ammoniaqué · 1 · {Ls(900)} ls · 2,4 M Cr · 1re découverte, 1re cartographie", Tone.Good, Loud: true),
                new PanelLine("Sur la route", Tone.Info, Header: true),
                new PanelLine("1 saut · Beta (1) · 1,1 M Cr", Tone.Good),
                new PanelLine("3 sauts · Delta (2) · 900 k Cr +", Tone.Good),
            },
            Briefing.Panel(state, Flying(), Arrival));
        Assert.Empty(Briefing.Panel(state, Docked(), Arrival));

        // Beta passed and its water world mapped there: the route counts from Beta.
        state.Apply(Jump("19:05:00", "Beta", 2));
        state.Apply(Map("19:10:00", "Beta 3", 2));
        Assert.Equal(new[] { "Sur la route", "2 sauts · Delta (2) · 900 k Cr +" },
            Briefing.Panel(state, Flying(), Arrival).Select(l => l.Text));
    }

    [Fact]
    public void Planets_whose_orbit_is_not_known_come_by_their_distance()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1));
        state.SetNotableBodies(1, [
            new NotableBody("Alpha B 1", PlanetClass.EarthLike, false, 9000, Value: 3_000_000),
            new NotableBody("Alpha A 10", PlanetClass.Water, false, 700),
            new NotableBody("Alpha A 5 a", PlanetClass.Water, false, 500),
            new NotableBody("Alpha A 4", PlanetClass.Rocky, true, 100),
        ]);

        Assert.Equal(new[]
            {
                "Ici (4)", "Corps rocheux terraformable · A 4 · 100 ls · valeur inconnue", "Monde aquatique · A 5 a · 500 ls · valeur inconnue",
                "Monde aquatique · A 10 · 700 ls · valeur inconnue", $"Terre respirable · B 1 · {Ls(9000)} ls · 3,0 M Cr",
            },
            Here(state));
    }

    [Fact]
    public void The_system_map_keeps_the_panel_for_the_planets_here_only()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1), NavRoute("19:00:05", ("Alpha", 1), ("Beta", 2)));
        state.SetRouteNotable(new Dictionary<long, List<NotableBody>> { [2] = [new NotableBody("Beta 3", PlanetClass.Water, false, 540)] });
        state.SetNotableBodies(1, [new NotableBody("Alpha 2", PlanetClass.Water, false, 300)]);
        GameStatus Focus(int gui) => new(1L << 24, 0, gui, 16, 50_000_000, "Clean");

        Assert.True(Briefing.PanelVisible(Focus(7)));
        Assert.False(Briefing.BannerVisible(Focus(7)));
        Assert.True(Briefing.PanelVisible(Flying()));
        Assert.False(Briefing.PanelVisible(Focus(8)));
        Assert.Equal(new[] { "Ici (1)", "Monde aquatique · 2 · 300 ls · valeur inconnue" }, Briefing.Panel(state, Focus(7), Arrival).Select(l => l.Text));
    }

    private static string StarScan(string time, string body, long address, bool discovered = true, int ls = 0) =>
        Line(time, $"\"event\":\"Scan\", \"ScanType\":\"AutoScan\", \"BodyName\":\"{body}\", \"BodyID\":0, \"SystemAddress\":{address}, " +
                   $"\"DistanceFromArrivalLS\":{ls}, \"StarType\":\"M\", \"WasDiscovered\":{(discovered ? "true" : "false")}, \"WasMapped\":false");

    private static string RiceScan(string time, string body, long address, string planetClass = "Icy body", bool discovered = true, bool mapped = true, int ls = 1200, double mass = 0.01) =>
        Line(time, $"\"event\":\"Scan\", \"ScanType\":\"Detailed\", \"BodyName\":\"{body}\", \"BodyID\":4, \"SystemAddress\":{address}, " +
                   $"\"DistanceFromArrivalLS\":{ls}, \"TerraformState\":\"\", \"PlanetClass\":\"{planetClass}\", \"MassEM\":{mass.ToString(CultureInfo.InvariantCulture)}, " +
                   $"\"WasDiscovered\":{(discovered ? "true" : "false")}, \"WasMapped\":{(mapped ? "true" : "false")}");

    private static string BeltScan(string time, string body, long address) =>
        Line(time, $"\"event\":\"Scan\", \"ScanType\":\"AutoScan\", \"BodyName\":\"{body}\", \"BodyID\":2, \"SystemAddress\":{address}, \"DistanceFromArrivalLS\":4, \"WasDiscovered\":true, \"WasMapped\":false");

    private static List<string> Here(GameState state) =>
        Briefing.Panel(state, Flying(), Arrival).TakeWhile(l => !l.Header || l.Text.StartsWith("Ici")).Select(l => l.Text).ToList();

    private static string WaterWorld(string time, string body, bool mapped) =>
        Line(time, $"\"event\":\"Scan\", \"ScanType\":\"Detailed\", \"BodyName\":\"{body}\", \"BodyID\":5, \"SystemAddress\":1, \"StarSystem\":\"Alpha\", " +
                   $"\"DistanceFromArrivalLS\":500, \"TerraformState\":\"\", \"PlanetClass\":\"Water world\", \"MassEM\":0.975783, \"WasDiscovered\":true, \"WasMapped\":{(mapped ? "true" : "false")}");

    [Fact]
    public void A_notable_planet_is_worth_what_mapping_it_well_pays_whoever_tells_of_it()
    {
        // Mapped within the probes of the target: what the formula of the players gives, a first mapping eight times the scan.
        var state = Replay(Jump("19:00:00", "Alpha", 1), WaterWorld("19:01:00", "Alpha 1", mapped: true), WaterWorld("19:01:10", "Alpha 2", mapped: false));
        state.MinNotableValue = 0;
        Assert.Equal([548_928, 1_333_171], state.NotableBodies.OrderBy(b => b.Name).Select(b => b.Value));

        Assert.Equal(1_594_270, NotableBodies.ParseSpansh("""{"results":[{"name":"Beta 7","subtype":"Water world","terraforming_state":"Terraformable","earth_masses":1.642756,"distance_to_arrival":1147,"system_id64":2}]}""").Single().Body.Value);
        Assert.Equal(548_928, NotableBodies.Parse("""{"bodies":[{"name":"Beta 8","subType":"Water world","terraformingState":"Not terraformable","earthMasses":0.975783,"distanceToArrival":1416}]}""").Single().Value);
    }

    [Fact]
    public void Below_the_value_chosen_a_notable_planet_is_not_told_here_nor_on_the_route()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1), WaterWorld("19:01:00", "Alpha 1", mapped: true), WaterWorld("19:01:10", "Alpha 2", mapped: false),
            NavRoute("19:02:00", ("Alpha", 1), ("Beta", 2)));
        state.SetRouteNotable(new Dictionary<long, List<NotableBody>>
        {
            [2] = [new NotableBody("Beta 7", PlanetClass.Water, true, 1147, Value: 1_594_270), new NotableBody("Beta 8", PlanetClass.Water, false, 1416, Value: 548_928)],
        });
        state.MinNotableValue = 700_000;

        Assert.Equal(new[] { "Ici (1)", "Monde aquatique · 2 · 500 ls · 1,3 M Cr · 1re cartographie", "Sur la route", "1 saut · Beta (1) · 1,6 M Cr" },
            Briefing.Panel(state, Flying(), Arrival).Select(l => l.Text));
    }

    private static string Signal(string time, string body) =>
        Line(time, $"\"event\":\"FSSSignalDiscovered\", \"SystemAddress\":1, {body}");

    [Fact]
    public void The_sources_of_materials_are_told_as_soon_as_seen_until_they_expire_or_the_system_is_left()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1),
            Signal("19:00:05", "\"SignalName\":\"$USS_HighGradeEmissions;\", \"USSType\":\"$USS_Type_VeryValuableSalvage;\", \"USSType_Localised\":\"Émissions d'intensité élevée\", \"SpawningState\":\"\", \"ThreatLevel\":0, \"TimeRemaining\":1200.0"),
            Signal("19:00:05", "\"SignalName\":\"$USS_EncodedEmissions;\", \"USSType\":\"$USS_Type_ValuableSalvage;\", \"USSType_Localised\":\"Émissions encodées\", \"ThreatLevel\":0, \"TimeRemaining\":600.0"),
            Signal("19:00:05", "\"SignalName\":\"$USS_DegradedEmissions;\", \"USSType\":\"$USS_Type_Salvage;\", \"USSType_Localised\":\"Émissions de mauvaise qualité\", \"ThreatLevel\":0, \"TimeRemaining\":300.0"),
            Signal("19:00:05", "\"SignalName\":\"$MULTIPLAYER_SCENARIO81_TITLE;\", \"SignalName_Localised\":\"Épave récupérable\", \"SignalType\":\"Generic\""),
            // Not a source of materials.
            Signal("19:00:05", "\"SignalName\":\"$USS_DistressCall;\", \"USSType\":\"$USS_Type_DistressSignal;\", \"USSType_Localised\":\"Appel de détresse\", \"ThreatLevel\":0, \"TimeRemaining\":900.0"),
            Signal("19:00:05", "\"SignalName\":\"$MULTIPLAYER_SCENARIO42_TITLE;\", \"SignalName_Localised\":\"Balise de navigation\", \"SignalType\":\"NavBeacon\""));

        Assert.Equal(new[]
            {
                new PanelLine("Matériaux (4)", Tone.Info, Header: true),
                new PanelLine("Émissions d'intensité élevée · grade 5 · 20 min", Tone.Good, Loud: true),
                new PanelLine("Émissions encodées · 10 min", Tone.Good),
                new PanelLine("Émissions de mauvaise qualité · 5 min", Tone.Good),
                new PanelLine("Épave récupérable", Tone.Good),
            },
            Briefing.Panel(state, Flying(), Arrival.AddSeconds(5)));

        Assert.Equal(new[] { "Matériaux (2)", "Émissions d'intensité élevée · grade 5 · 9 min", "Épave récupérable" },
            Briefing.Panel(state, Flying(), Arrival.AddMinutes(11)).Select(l => l.Text));

        state.Apply(Jump("19:12:00", "Beta", 2));
        Assert.Empty(Briefing.Panel(state, Flying(), Arrival.AddMinutes(12)));
    }

    [Fact]
    public void Bodies_without_a_bonus_nor_worth_say_nothing_honked_or_not()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1),
            Honk("19:00:10", "Alpha", 1),
            StarScan("19:00:11", "Alpha", 1, discovered: false),
            BeltScan("19:00:12", "Alpha A Belt Cluster 1", 1),
            RiceScan("19:01:00", "Alpha 1", 1));
        Assert.Empty(Briefing.Panel(state, Flying(), Arrival));
    }

    [Fact]
    public void Only_the_planets_worth_the_trip_are_told_with_their_bonus_until_mapped()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1),
            // Nobody found or mapped them, but they pay too little: not told.
            RiceScan("19:01:10", "Alpha 2", 1, discovered: false, mapped: false, ls: 900),
            RiceScan("19:01:20", "Alpha 3", 1, "High metal content body", mapped: false, ls: 300),
            Scan("19:01:40", "Alpha 4", 1, "Water world", discovered: false, ls: 2000),
            RiceScan("19:01:50", "Alpha 4", 1, "Water world", discovered: false, mapped: false, ls: 2000, mass: 1));
        // One EDSM tells of, not scanned yet: its value not known, it may pass the threshold.
        state.SetNotableBodies(1, [new NotableBody("Alpha 5", PlanetClass.Rocky, true, 3000)]);

        Assert.Equal(new[]
            {
                new PanelLine("Ici (2)", Tone.Info, Header: true),
                new PanelLine($"Monde aquatique · 4 · {Ls(2000)} ls · 1,6 M Cr · 1re découverte, 1re cartographie", Tone.Good, Loud: true),
                new PanelLine($"Corps rocheux terraformable · 5 · {Ls(3000)} ls · valeur inconnue", Tone.Good),
            },
            Briefing.Panel(state, Flying(), Arrival));

        state.Apply(Map("19:10:00", "Alpha 4", 1));
        Assert.Equal(new[] { "Ici (1)", $"Corps rocheux terraformable · 5 · {Ls(3000)} ls · valeur inconnue" }, Here(state));

        // A system left takes its planets with it.
        state.Apply(Jump("19:20:00", "Beta", 2));
        Assert.Empty(Briefing.Panel(state, Flying(), Arrival));
    }

    [Fact]
    public void A_system_of_the_route_with_a_first_discovery_is_filled()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1), NavRoute("19:00:05", ("Alpha", 1), ("Beta", 2)));
        state.SetRouteNotable(new Dictionary<long, List<NotableBody>> { [2] = [new NotableBody("Beta 3", PlanetClass.Water, false, 540, FirstDiscovery: true)] });

        Assert.Contains(new PanelLine("1 saut · Beta (1) · valeur inconnue", Tone.Good, Loud: true), Briefing.Panel(state, Flying(), Arrival));
    }

    [Fact]
    public void A_system_of_the_route_reached_tells_its_planets_here_from_what_Spansh_said()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1), NavRoute("19:00:05", ("Alpha", 1), ("Beta", 2)));
        state.SetRouteNotable(new Dictionary<long, List<NotableBody>> { [2] = [new NotableBody("Beta 3", PlanetClass.Water, false, 540)] });

        // EDSM does not know Beta: Spansh did.
        state.Apply(Jump("19:05:00", "Beta", 2));
        Assert.Equal(["Ici (1)", "Monde aquatique · 3 · 540 ls · valeur inconnue"], Here(state));

        state.Apply(Map("19:10:00", "Beta 3", 2));
        Assert.Empty(Here(state));
    }

    [Fact]
    public void The_planet_targeted_stands_out_in_the_game_s_orange()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1));
        state.SetNotableBodies(1, [new NotableBody("Alpha 2", PlanetClass.Water, false, 300, Value: 1_200_000), new NotableBody("Alpha 3", PlanetClass.Rocky, true, 400)]);
        var status = GameStatus.Parse("""{ "event":"Status", "Flags":16777216, "GuiFocus":0, "Destination":{ "System":1, "Body":3, "Name":"Alpha 3" } }""")!;

        Assert.Equal("Alpha 3", status.Destination);
        Assert.Equal(new[]
            {
                new PanelLine("Ici (2)", Tone.Info, Header: true),
                new PanelLine("Monde aquatique · 2 · 300 ls · 1,2 M Cr", Tone.Good),
                new PanelLine("› Corps rocheux terraformable · 3 · 400 ls · valeur inconnue", Tone.Info, Targeted: true),
            },
            Briefing.Panel(state, status, Arrival));
    }

    [Fact]
    public void The_panel_names_eight_systems_of_the_route_at_most()
    {
        var hops = Enumerable.Range(1, 10).Select(i => ($"S{i}", (long)i)).ToArray();
        var state = Replay(Jump("19:00:00", "S0", 100), NavRoute("19:00:05", [("S0", 100L), .. hops]));
        state.SetRouteNotable(hops.ToDictionary(h => h.Item2, h => new List<NotableBody> { new($"{h.Item1} 1", PlanetClass.Water, false, 100) }));

        Assert.Equal(8, Briefing.Panel(state, Flying(), Arrival).Count(l => l.Text.Contains(" sauts · ") || l.Text.Contains(" saut · ")));
    }

    private static string Loadout(string time, params string[] items) =>
        Line(time, $"\"event\":\"Loadout\", \"Ship\":\"mandalay\", \"ShipID\":12, \"Modules\":[ {string.Join(", ", items.Select(i => $"{{ \"Slot\":\"Slot01_Size2\", \"Item\":\"{i}\" }}"))} ]");

    [Fact]
    public void The_panel_shows_only_in_a_ship_with_a_surface_scanner()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1), Scan("19:01:00", "Alpha 1", 1, "Water world"),
            Loadout("19:02:00", "int_fuelscoop_size5_class5", "int_detailedsurfacescanner_tiny"));
        Assert.NotEmpty(Briefing.Panel(state, Flying(), Arrival));

        state.Apply(Loadout("19:03:00", "int_fuelscoop_size5_class5"));
        Assert.Empty(Briefing.Panel(state, Flying(), Arrival));
    }
}
