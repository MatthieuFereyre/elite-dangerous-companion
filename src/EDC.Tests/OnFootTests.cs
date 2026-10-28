using EDC.Companion;
using EDC.Core;
using Xunit;

namespace EDC.Tests;

public sealed class OnFootTests
{
    private static string Line(string time, string body) => $"{{ \"timestamp\":\"2026-10-09T{time}Z\", {body} }}";

    private static DateTime At(string time) => DateTime.Parse($"2026-10-09T{time}Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);

    private static GameState Replay(params string[] lines)
    {
        var state = new GameState();
        foreach (var line in lines)
            state.Apply(line);
        return state;
    }

    /// <summary>On foot on a planet, or in a station (where the ship stays docked).</summary>
    private static GameStatus OnFoot(double health = 1, double oxygen = 1, long more = 0, bool station = false) =>
        new(station ? 1 : 0, 1L | (station ? 1L << 3 : 1L << 4) | more, 0, null, 50_000_000, "Clean", health, oxygen, 293);

    private static GameStatus Flying() => new(1L << 24, 0, 0, 16, 50_000_000, "Clean");
    private static GameStatus Docked() => new((1L << 24) | 1, 0, 0, 16, 50_000_000, "Clean");

    private static string Backpack(int medkits, int cells) => "\"event\":\"Backpack\", \"Items\":[ ], \"Components\":[ ], \"Consumables\":[ " +
        $"{{ \"Name\":\"healthpack\", \"Name_Localised\":\"Medkit\", \"OwnerID\":0, \"Count\":{medkits} }}, " +
        $"{{ \"Name\":\"energycell\", \"Name_Localised\":\"Cellule d'énergie\", \"OwnerID\":0, \"Count\":{cells} }} ], \"Data\":[ ]";

    private const string EmptyBackpack = "\"event\":\"Backpack\", \"Items\":[ ], \"Components\":[ ], \"Consumables\":[ ], \"Data\":[ ]";

    private static string Disembark(bool planet) =>
        $"\"event\":\"Disembark\", \"SRV\":false, \"Taxi\":false, \"Multicrew\":false, \"ID\":22, \"StarSystem\":\"Ross 1057\", " +
        $"\"SystemAddress\":2869440947617, \"Body\":\"Ross 1057 A 1\", \"BodyID\":8, \"OnStation\":{(!planet).ToString().ToLowerInvariant()}, \"OnPlanet\":{planet.ToString().ToLowerInvariant()}";

    // ------------------------------------------------------------------------------- the suit

    [Fact]
    public void Status_json_gives_the_suit_s_health_oxygen_and_temperature()
    {
        var status = GameStatus.Parse("{ \"timestamp\":\"2026-10-18T20:07:02Z\", \"event\":\"Status\", \"Flags\":5, \"Flags2\":81929, " +
            "\"Oxygen\":0.400000, \"Health\":0.300000, \"Temperature\":293.000000, \"SelectedWeapon\":\"\", \"LegalState\":\"Clean\", \"BodyName\":\"Curtis Outpost\", \"Balance\":88709050 }");
        Assert.NotNull(status);
        Assert.Equal((0.3, 0.4, 293.0), (status.Health, status.Oxygen, status.Temperature));
        Assert.True(status.OnFoot);
        Assert.True(status.OnFootInStation);
        Assert.False(status.OnFootOnPlanet);
    }

    [Fact]
    public void The_banner_shows_on_foot_too() =>
        Assert.True(Briefing.BannerVisible(OnFoot()));

    [Fact]
    public void The_backpack_counts_its_consumables_as_they_come_and_go()
    {
        var state = Replay(Line("19:00:00", Backpack(medkits: 2, cells: 3)));
        Assert.Equal((2, 3), (state.Packed("healthpack"), state.Packed("energycell")));

        state.Apply(Line("19:05:00", "\"event\":\"BackpackChange\", \"Removed\":[ { \"Name\":\"healthpack\", \"Name_Localised\":\"Medkit\", \"OwnerID\":0, \"Count\":1, \"Type\":\"Consumable\" } ]"));
        state.Apply(Line("19:06:00", "\"event\":\"BackpackChange\", \"Added\":[ { \"Name\":\"energycell\", \"OwnerID\":0, \"Count\":1, \"Type\":\"Consumable\" } ]"));
        Assert.Equal((1, 4), (state.Packed("healthpack"), state.Packed("energycell")));

        state.Apply(Line("19:10:00", EmptyBackpack));
        Assert.Equal(0, state.Packed("healthpack"));
    }

    [Fact]
    public void A_backpack_or_locker_event_without_its_lists_asks_for_the_file()
    {
        var state = Replay(Line("19:00:00", "\"event\":\"Backpack\""), Line("19:00:00", "\"event\":\"ShipLocker\""));
        Assert.True(state.BackpackFileWanted);
        Assert.True(state.ShipLockerFileWanted);
    }

    [Fact]
    public void Backpack_json_and_ShipLocker_json_fill_the_counts()
    {
        var state = new GameState();
        using (var bag = System.Text.Json.JsonDocument.Parse("{ " + Backpack(medkits: 2, cells: 1) + " }"))
            state.SetBackpackFromFile(bag.RootElement);
        using (var locker = System.Text.Json.JsonDocument.Parse("{ " + Backpack(medkits: 5, cells: 4).Replace("\"Backpack\"", "\"ShipLocker\"") + " }"))
            state.SetShipLockerFromFile(locker.RootElement);
        Assert.Equal((2, 1, 5, 4), (state.Packed("healthpack"), state.Packed("energycell"), state.Stored("healthpack"), state.Stored("energycell")));
    }

    [Fact]
    public void The_ship_locker_keeps_its_own_count()
    {
        var state = Replay(Line("19:00:00", Backpack(medkits: 3, cells: 1).Replace("\"Backpack\"", "\"ShipLocker\"")));
        Assert.Equal((3, 1), (state.Stored("healthpack"), state.Stored("energycell")));
        Assert.Equal(0, state.Packed("healthpack"));
    }

    // ------------------------------------------------------------------------------- the warning

    [Fact]
    public void Low_health_warns_big_see_through_with_the_medkits_left()
    {
        var state = Replay(Line("19:00:00", Backpack(medkits: 2, cells: 3)));
        Assert.Equal(new Warning("Santé 18 %", "2 medkits dans le sac", SeeThrough: true), Briefing.Warning(state, OnFoot(health: 0.18), At("19:10:00")));
        Assert.Equal(new Warning("Santé 42 %", "2 medkits dans le sac", Big: false, SeeThrough: true), Briefing.Warning(state, OnFoot(health: 0.42), At("19:10:00")));
        Assert.Null(Briefing.Warning(state, OnFoot(health: 0.8), At("19:10:00")));
    }

    [Fact]
    public void Low_health_without_a_medkit_says_to_take_cover()
    {
        var state = Replay(Line("19:00:00", EmptyBackpack));
        Assert.Equal("Aucun medkit : à couvert", Briefing.Warning(state, OnFoot(health: 0.2), At("19:10:00"))?.Detail);
    }

    [Fact]
    public void Low_oxygen_means_a_flat_battery_and_points_to_the_energy_cells()
    {
        var state = Replay(Line("19:00:00", Backpack(medkits: 2, cells: 3)));
        Assert.Equal(new Warning("Oxygène 20 %", "Batterie à plat : 3 cellules d'énergie dans le sac", SeeThrough: true),
            Briefing.Warning(state, OnFoot(oxygen: 0.2), At("19:10:00")));
        Assert.False(Briefing.Warning(state, OnFoot(oxygen: 0.45), At("19:10:00"))?.Big);

        var empty = Replay(Line("19:00:00", EmptyBackpack));
        Assert.Equal("Batterie à plat, sans cellule d'énergie : regagnez un abri", Briefing.Warning(empty, OnFoot(oxygen: 0.2), At("19:10:00"))?.Detail);
    }

    [Fact]
    public void Health_comes_before_oxygen()
    {
        var state = Replay(Line("19:00:00", Backpack(medkits: 1, cells: 1)));
        Assert.Equal("Santé 20 %", Briefing.Warning(state, OnFoot(health: 0.2, oxygen: 0.2), At("19:10:00"))?.Title);
        Assert.Equal("1 medkit dans le sac", Briefing.Warning(state, OnFoot(health: 0.2), At("19:10:00"))?.Detail);
    }

    [Fact]
    public void Leaving_on_a_planet_with_an_empty_bag_warns_for_a_moment()
    {
        var state = Replay(Line("19:00:00", Disembark(planet: true)), Line("19:00:01", EmptyBackpack));
        Assert.Equal(new Warning("Sac sans medkit ni cellule d'énergie", "Rien pour se soigner ni pour recharger la batterie pendant la sortie",
            Tone: Tone.Warn, SeeThrough: true), Briefing.Warning(state, OnFoot(), At("19:00:03")));
        Assert.Null(Briefing.Warning(state, OnFoot(), At("19:00:20")));

        var cellsOnly = Replay(Line("19:00:00", Disembark(planet: true)), Line("19:00:01", Backpack(medkits: 0, cells: 2)));
        Assert.Equal("Sac sans medkit", Briefing.Warning(cellsOnly, OnFoot(), At("19:00:03"))?.Title);

        var full = Replay(Line("19:00:00", Disembark(planet: true)), Line("19:00:01", Backpack(medkits: 2, cells: 2)));
        Assert.Null(Briefing.Warning(full, OnFoot(), At("19:00:03")));
    }

    [Fact]
    public void Leaving_the_ship_in_a_station_is_no_danger()
    {
        var state = Replay(Line("19:00:00", Disembark(planet: false)), Line("19:00:01", EmptyBackpack));
        Assert.Null(Briefing.Warning(state, OnFoot(station: true), At("19:00:03")));
    }

    // ------------------------------------------------------------------------------- the banner

    [Fact]
    public void On_foot_the_banner_gives_the_suit_and_the_bag_not_the_ship()
    {
        var state = Replay(
            Line("18:00:00", "\"event\":\"Loadout\", \"Ship\":\"asp\", \"ShipID\":23, \"HullHealth\":0.5, \"CargoCapacity\":0, \"Rebuy\":1000, \"Modules\":[ ]"),
            Line("19:00:00", Backpack(medkits: 1, cells: 0)));

        var chips = Briefing.Banner(state, OnFoot(health: 0.45, station: true), PadBattery.None, At("19:10:00"));
        Assert.Equal(["Santé 45 %", "Medkits 1 · cellules 0"], chips.Select(c => c.Text));
        Assert.Equal([Tone.Alert, Tone.Warn], chips.Select(c => c.Tone));

        Assert.Equal(new Chip("Oxygène 30 %", Tone.Alert, "Batterie à plat, sans cellule d'énergie : regagnez un abri"),
            Briefing.Banner(state, OnFoot(oxygen: 0.3), PadBattery.None, At("19:10:00"))[0]);

        var fine = Replay(Line("19:00:00", Backpack(medkits: 2, cells: 2)));
        Assert.Equal(new Chip("Medkits 2 · cellules 2", Tone.Good), Assert.Single(Briefing.Banner(fine, OnFoot(), PadBattery.None, At("19:10:00"))));
    }

    [Theory]
    [InlineData(10, "Très froid")]
    [InlineData(11, "Très chaud")]
    public void Extreme_temperatures_hurt_the_suit(int flag, string text)
    {
        var chips = Briefing.Banner(new GameState(), OnFoot(more: 1L << flag), PadBattery.None, At("19:10:00"));
        Assert.Equal(new Chip(text, Tone.Alert, "La combinaison prend des dégâts"), chips[0]);
    }

    // ------------------------------------------------------------------------------- missions

    private const string OnFootMission = "\"event\":\"MissionAccepted\", \"Faction\":\"F\", \"Name\":\"Mission_OnFoot_OnslaughtIllegal_MB\", " +
        "\"LocalisedName\":\"Éliminer les criminels\", \"MissionID\":5, \"Reward\":200000";

    [Fact]
    public void An_on_foot_mission_with_nothing_to_heal_is_a_reminder_at_the_station()
    {
        var state = Replay(Line("19:00:00", EmptyBackpack), Line("19:00:00", EmptyBackpack.Replace("\"Backpack\"", "\"ShipLocker\"")),
            Line("19:01:00", OnFootMission));
        Assert.Contains(new Chip("Mission à pied sans medkit ni cellule d'énergie", Tone.Warn, "Pioneer Supplies, à la station"),
            Briefing.Reminders(state, Docked()));

        state.Apply(Line("19:02:00", Backpack(medkits: 2, cells: 2).Replace("\"Backpack\"", "\"ShipLocker\"")));
        Assert.DoesNotContain(Briefing.Reminders(state, Docked()), c => c.Text.StartsWith("Mission à pied"));
    }

    [Fact]
    public void On_foot_missions_end_with_their_outcome_and_come_back_with_the_list()
    {
        var state = Replay(Line("19:00:00", EmptyBackpack), Line("19:01:00", OnFootMission),
            Line("19:30:00", "\"event\":\"MissionCompleted\", \"Faction\":\"F\", \"Name\":\"Mission_OnFoot_OnslaughtIllegal_MB_name\", \"MissionID\":5, \"Reward\":200000"));
        Assert.DoesNotContain(Briefing.Reminders(state, Docked()), c => c.Text.StartsWith("Mission à pied"));

        state.Apply(Line("20:00:00", "\"event\":\"Missions\", \"Active\":[ { \"MissionID\":9, \"Name\":\"Mission_OnFoot_Collect_MB_name\", \"PassengerMission\":false, \"Expires\":80000 } ], \"Failed\":[ ], \"Complete\":[ ]"));
        Assert.Contains(Briefing.Reminders(state, Docked()), c => c.Text.StartsWith("Mission à pied"));

        state.Apply(Line("20:10:00", "\"event\":\"MissionAbandoned\", \"Name\":\"Mission_OnFoot_Collect_MB_name\", \"MissionID\":9"));
        Assert.DoesNotContain(Briefing.Reminders(state, Docked()), c => c.Text.StartsWith("Mission à pied"));
    }

    [Fact]
    public void A_mission_gone_while_the_game_was_off_leaves_with_the_next_list()
    {
        // Expired between two sessions: no journal line says so, the list at the load leaves it out.
        var state = Replay(Line("19:00:00", EmptyBackpack), Line("19:01:00", OnFootMission),
            Line("20:00:00", "\"event\":\"Missions\", \"Active\":[ ], \"Failed\":[ ], \"Complete\":[ ]"));
        Assert.DoesNotContain(Briefing.Reminders(state, Docked()), c => c.Text.StartsWith("Mission à pied"));
    }

    // ------------------------------------------------------------------------------- combat bonds

    private static string Bond(string time, long reward, string faction = "East India Company") =>
        Line(time, $"\"event\":\"FactionKillBond\", \"Reward\":{reward}, \"AwardingFaction\":\"{faction}\", \"VictimFaction\":\"Revolutionary Party of LHS 1212\"");

    [Fact]
    public void Combat_bonds_add_up_until_cashed_in()
    {
        var state = Replay(Bond("13:13:16", 27238), Bond("13:13:51", 22238));
        Assert.Equal((49_476, 2), (state.BondsPending, state.BondsPendingCount));

        var chip = new Chip("Obligations 49 k Cr", Tone.Good, "2 obligations de combat à encaisser : East India Company");
        Assert.Contains(chip, Briefing.Banner(state, Flying(), PadBattery.None, At("13:20:00")));
        Assert.Contains(chip, Briefing.Banner(state, OnFoot(), PadBattery.None, At("13:20:00")));
        Assert.Contains(Briefing.Reminders(state, Docked()), c => c.Text == "Encaisser 49 k Cr d'obligations");

        state.Apply(Line("13:22:47", "\"event\":\"RedeemVoucher\", \"Type\":\"CombatBond\", \"Amount\":49476, \"Faction\":\"East India Company\""));
        Assert.Equal((0, 0), (state.BondsPending, state.BondsPendingCount));
    }

    [Fact]
    public void Combat_bonds_are_lost_with_death_and_kept_per_faction()
    {
        var state = Replay(Bond("13:00:00", 10000), Bond("13:01:00", 20000, "Other"),
            Line("13:10:00", "\"event\":\"RedeemVoucher\", \"Type\":\"CombatBond\", \"Amount\":10000, \"Faction\":\"East India Company\""));
        Assert.Equal((20_000, 1), (state.BondsPending, state.BondsPendingCount));

        state.Apply(Line("13:20:00", "\"event\":\"Died\""));
        Assert.Equal(0, state.BondsPending);
    }

    // ------------------------------------------------------------------------------- exobiology

    private static string Scan(string time, string type, string species = "$Codex_Ent_Bacterial_04_Name;", string name = "Bacterium Acies",
        string genus = "$Codex_Ent_Bacterial_Genus_Name;") =>
        Line(time, $"\"event\":\"ScanOrganic\", \"ScanType\":\"{type}\", \"Genus\":\"{genus}\", \"Genus_Localised\":\"Bacterium\", " +
            $"\"Species\":\"{species}\", \"Species_Localised\":\"{name}\", \"SystemAddress\":1, \"Body\":12");

    [Fact]
    public void A_species_being_sampled_shows_its_progress_value_and_spacing()
    {
        var state = Replay(Scan("19:00:00", "Log"));
        var chip = new Chip("Bacterium Acies 1/3 · 1,0 M Cr", Tone.Info, "Prélèvement suivant à plus de 500 m des précédents");
        Assert.Equal(chip, Briefing.Organic(state));
        Assert.Contains(chip, Briefing.Banner(state, OnFoot(), PadBattery.None, At("19:01:00")));
        Assert.Contains(chip, Briefing.Banner(state, Flying(), PadBattery.None, At("19:01:00")));

        state.Apply(Scan("19:03:00", "Sample"));
        Assert.Equal("Bacterium Acies 2/3 · 1,0 M Cr", Briefing.Organic(state)?.Text);
    }

    [Fact]
    public void An_analysed_species_waits_to_be_sold_and_is_lost_with_death()
    {
        var state = Replay(Scan("19:00:00", "Log"), Scan("19:03:00", "Sample"), Scan("19:05:00", "Sample"), Scan("19:05:00", "Analyse"),
            Scan("19:20:00", "Log", "$Codex_Ent_Stratum_07_Name;", "Stratum Tectonicas", "$Codex_Ent_Stratum_Genus_Name;"),
            Scan("19:22:00", "Sample", "$Codex_Ent_Stratum_07_Name;", "Stratum Tectonicas", "$Codex_Ent_Stratum_Genus_Name;"),
            Scan("19:24:00", "Analyse", "$Codex_Ent_Stratum_07_Name;", "Stratum Tectonicas", "$Codex_Ent_Stratum_Genus_Name;"));

        Assert.Null(Briefing.Organic(state));
        Assert.Equal(20_010_800, state.OrganicUnsold);
        var chip = new Chip("Exobio 20,0 M Cr à vendre", Tone.Good, "2 espèces analysées, perdues en cas de mort : Vista Genomics");
        Assert.Contains(chip, Briefing.Banner(state, Flying(), PadBattery.None, At("19:30:00")));
        Assert.Contains(chip, Briefing.Reminders(state, Docked()));

        state.Apply(Line("19:40:00", "\"event\":\"Died\""));
        Assert.Equal(0, state.OrganicUnsold);
    }

    [Fact]
    public void Selling_the_data_takes_the_sold_species_off()
    {
        var state = Replay(Scan("19:00:00", "Analyse"),
            Scan("19:10:00", "Analyse", "$Codex_Ent_Stratum_07_Name;", "Stratum Tectonicas", "$Codex_Ent_Stratum_Genus_Name;"),
            Line("20:00:00", "\"event\":\"SellOrganicData\", \"MarketID\":1, \"BioData\":[ { \"Genus\":\"$Codex_Ent_Bacterial_Genus_Name;\", " +
                "\"Species\":\"$Codex_Ent_Bacterial_04_Name;\", \"Value\":1000000, \"Bonus\":0 } ]"));
        Assert.Equal(19_010_800, state.OrganicUnsold);
    }

    [Fact]
    public void Starting_another_species_drops_the_one_left_half_done()
    {
        // The game keeps one species in the sampler: logging another throws the samples away.
        var state = Replay(Scan("19:00:00", "Log"), Scan("19:02:00", "Sample"),
            Scan("19:10:00", "Log", "$Codex_Ent_Stratum_07_Name;", "Stratum Tectonicas", "$Codex_Ent_Stratum_Genus_Name;"));
        Assert.Equal(new Chip("Stratum Tectonicas 1/3 · 19,0 M Cr", Tone.Info, "Prélèvement suivant à plus de 500 m des précédents"), Briefing.Organic(state));
    }

    [Fact]
    public void A_species_out_of_the_table_shows_without_value()
    {
        var state = Replay(Scan("19:00:00", "Log", "$Codex_Ent_Unknown_Name;", "Inconnue", "$Codex_Ent_Unknown_Genus_Name;"));
        Assert.Equal(new Chip("Inconnue 1/3", Tone.Info), Briefing.Organic(state));
    }
}
