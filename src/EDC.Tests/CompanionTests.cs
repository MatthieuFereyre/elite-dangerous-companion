using EDC.Companion;
using EDC.Core;
using Xunit;

namespace EDC.Tests;

public sealed class CompanionTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 20, 0, 0, DateTimeKind.Utc);

    private static string Line(string time, string body) => $"{{ \"timestamp\":\"2026-10-09T{time}Z\", {body} }}";

    private static GameState Replay(params string[] lines)
    {
        var state = new GameState();
        foreach (var line in lines)
            state.Apply(line);
        return state;
    }

    private const string Route = "\"event\":\"NavRoute\", \"Route\":[ " +
        "{ \"StarSystem\":\"Alpha\", \"SystemAddress\":1, \"StarClass\":\"G\" }, " +
        "{ \"StarSystem\":\"Bravo\", \"SystemAddress\":2, \"StarClass\":\"T\" }, " +
        "{ \"StarSystem\":\"Charlie\", \"SystemAddress\":3, \"StarClass\":\"Y\" }, " +
        "{ \"StarSystem\":\"Delta\", \"SystemAddress\":4, \"StarClass\":\"K_OrangeGiant\" } ]";

    private static string Jump(string time, string system, long address, double fuelUsed = 4) =>
        Line(time, $"\"event\":\"FSDJump\", \"StarSystem\":\"{system}\", \"SystemAddress\":{address}, \"FuelUsed\":{fuelUsed}, \"FuelLevel\":10");

    // The discovery scanner sounded in Alpha: no reminder to honk over the fill warning.
    private static readonly string Honked = Line("19:00:02", "\"event\":\"FSSDiscoveryScan\", \"BodyCount\":9, \"SystemName\":\"Alpha\", \"SystemAddress\":1");

    private static GameStatus Flying(double fuel = 16) => new(1L << 24, 0, 0, fuel, 50_000_000, "Clean");
    private static GameStatus Docked(double fuel = 16) => new((1L << 24) | 1, 0, 0, fuel, 50_000_000, "Clean");

    [Theory]
    [InlineData("K", true)]
    [InlineData("M", true)]
    [InlineData("K_OrangeGiant", true)]
    [InlineData("A_BlueWhiteSuperGiant", true)]
    [InlineData("MS", false)]
    [InlineData("TTS", false)]
    [InlineData("T", false)]
    [InlineData("DA", false)]
    [InlineData("N", false)]
    public void Scoopable_stars_are_KGBFOAM(string starClass, bool scoopable) =>
        Assert.Equal(scoopable, Stars.IsScoopable(starClass));

    [Fact]
    public void Route_counts_the_jumps_left_from_where_the_ship_is()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1), Line("19:00:01", Route));
        Assert.Equal(3, state.JumpsLeft);
        Assert.Equal("Delta", state.Destination?.System);
        Assert.Equal("Bravo", state.NextHop?.System);

        state.Apply(Jump("19:01:00", "Bravo", 2));
        Assert.Equal(2, state.JumpsLeft);
        Assert.Equal("Charlie", state.NextHop?.System);

        state.Apply(Jump("19:03:00", "Delta", 4));
        Assert.Equal(0, state.JumpsLeft);
        Assert.Null(state.Destination);
    }

    [Fact]
    public void A_cleared_route_is_gone()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1), Line("19:00:01", Route), Line("19:00:05", "\"event\":\"NavRouteClear\""));
        Assert.Equal(0, state.JumpsLeft);
        Assert.Empty(Briefing.Flight(state, Flying()));
    }

    [Fact]
    public void A_route_event_without_its_route_asks_for_the_file()
    {
        var state = Replay(Line("19:00:00", "\"event\":\"NavRoute\""));
        Assert.True(state.RouteFileWanted);
        state.Apply(Line("19:00:01", "\"event\":\"NavRouteClear\""));
        Assert.False(state.RouteFileWanted);
    }

    [Fact]
    public void Next_star_not_scoopable_is_a_warning()
    {
        var state = Replay(Jump("19:00:00", "Alpha", 1), Line("19:00:01", Route));
        var chips = Briefing.Flight(state, Flying());
        Assert.Equal("Delta · 3 sauts", chips[0].Text);
        Assert.Equal(new Chip("Suivant T non écopable", Tone.Warn, "Bravo"), chips[1]);
    }

    [Fact]
    public void In_the_SRV_the_route_without_the_SRV_s_tank()
    {
        // In the SRV Status.json carries the SRV's fuel: empty, it must not read as the ship's.
        var state = Replay(Jump("19:00:00", "Alpha", 1, fuelUsed: 4), Line("19:00:01", Route));
        var srv = new GameStatus((1L << 26) | (1L << 19), 0, 0, 0, 50_000_000, "Clean");
        Assert.Equal(["Delta · 3 sauts", "Suivant T non écopable"], Briefing.Flight(state, srv).Select(c => c.Text));
    }

    [Fact]
    public void Fuel_short_of_the_next_scoopable_star_is_an_alert()
    {
        // Two dry stars ahead, then a scoopable one: three jumps at 4 t, with 10 t in the tank.
        var state = Replay(Jump("19:00:00", "Alpha", 1, fuelUsed: 4), Line("19:00:01", Route));
        Assert.Null(Briefing.FuelShortfall(state, Flying(fuel: 12)));

        var chip = Briefing.FuelShortfall(state, Flying(fuel: 10));
        Assert.Equal(Tone.Alert, chip?.Tone);
        Assert.Equal("Carburant ~2 sauts", chip?.Text);
    }

    private const string NeutronRoute = "\"event\":\"NavRoute\", \"Route\":[ " +
        "{ \"StarSystem\":\"Alpha\", \"SystemAddress\":1, \"StarClass\":\"G\" }, " +
        "{ \"StarSystem\":\"Bravo\", \"SystemAddress\":2, \"StarClass\":\"N\" }, " +
        "{ \"StarSystem\":\"Charlie\", \"SystemAddress\":3, \"StarClass\":\"T\" }, " +
        "{ \"StarSystem\":\"Delta\", \"SystemAddress\":4, \"StarClass\":\"K\" } ]";

    private static GameState OnNeutronRoute(string system, long address) => Replay(
        Line("18:00:00", "\"event\":\"Loadout\", \"Ship\":\"krait_mkii\", \"ShipID\":1, \"HullHealth\":1.0, \"CargoCapacity\":0, \"Rebuy\":1000, \"FuelCapacity\":{ \"Main\":32.0 }, \"Modules\":[ ]"),
        Jump("19:00:00", system, address), Line("19:00:01", NeutronRoute));

    [Fact]
    public void Neutron_star_next_says_to_fill_up_first_of_the_route()
    {
        var chips = Briefing.Flight(OnNeutronRoute("Alpha", 1), Flying(fuel: 20));
        Assert.Equal(new Chip("Étoile à neutrons ensuite · faire le plein", Tone.Alert,
            "Bravo, non écopable ; réservoir 62 %, l'étoile d'ici est écopable", Loud: true), chips[0]);
        Assert.Equal("Delta · 3 sauts", chips[1].Text);
    }

    [Fact]
    public void Neutron_star_next_with_a_full_tank_still_shows()
    {
        var chip = Briefing.Flight(OnNeutronRoute("Alpha", 1), Flying(fuel: 32))[0];
        Assert.Equal(("Étoile à neutrons ensuite · plein fait", Tone.Warn, true), (chip.Text, chip.Tone, chip.Loud));
    }

    [Fact]
    public void Neutron_star_next_from_a_star_that_does_not_refuel_gives_the_tank()
    {
        var state = OnNeutronRoute("Charlie", 3);
        state.SetRoute([.. state.Route.Take(3), new RouteHop("Echo", 5, "N"), state.Route[3]]);
        Assert.Equal("Étoile à neutrons ensuite · réservoir 62 %", Briefing.Flight(state, Flying(fuel: 20))[0].Text);
    }

    [Fact]
    public void Supercharged_FSD_shows_until_the_jump_uses_it()
    {
        var state = OnNeutronRoute("Bravo", 2);
        Assert.NotEqual(Tone.Good, Briefing.Flight(state, Flying())[0].Tone);

        state.Apply(Line("19:02:00", "\"event\":\"JetConeBoost\", \"BoostValue\":4.0"));
        Assert.Equal(new Chip("FSD suralimenté ×4", Tone.Good, "Élan pris dans le jet : portée ×4 pour le prochain saut", Loud: true),
            Briefing.Flight(state, Flying())[0]);

        state.Apply(Jump("19:03:00", "Charlie", 3));
        Assert.Null(state.Supercharge);
    }

    // Alpha at the origin, Bravo (a neutron star) 20 al away, Charlie 60 al beyond it, Delta 100 al beyond that.
    private static GameState OnPlacedRoute(string system, long address, double x) => Replay(
        Line("18:00:00", "\"event\":\"Loadout\", \"Ship\":\"python_nx\", \"ShipID\":1, \"HullHealth\":1.0, \"CargoCapacity\":0, \"Rebuy\":1000, \"MaxJumpRange\":25.5, \"FuelCapacity\":{ \"Main\":16.0 }, \"Modules\":[ ]"),
        Line("19:00:00", $"\"event\":\"FSDJump\", \"StarSystem\":\"{system}\", \"SystemAddress\":{address}, \"StarPos\":[{x},0,0], \"FuelUsed\":2, \"FuelLevel\":16"),
        Line("19:00:01", "\"event\":\"NavRoute\", \"Route\":[ " +
            "{ \"StarSystem\":\"Alpha\", \"SystemAddress\":1, \"StarPos\":[0,0,0], \"StarClass\":\"G\" }, " +
            "{ \"StarSystem\":\"Bravo\", \"SystemAddress\":2, \"StarPos\":[20,0,0], \"StarClass\":\"N\" }, " +
            "{ \"StarSystem\":\"Charlie\", \"SystemAddress\":3, \"StarPos\":[80,0,0], \"StarClass\":\"K\" }, " +
            "{ \"StarSystem\":\"Delta\", \"SystemAddress\":4, \"StarPos\":[180,0,0], \"StarClass\":\"K\" } ]"));

    [Fact]
    public void Next_jump_within_range_is_quiet()
    {
        var chips = Briefing.Flight(OnPlacedRoute("Alpha", 1, 0), Flying());
        Assert.Contains(new Chip("Saut 20 / 25,5 al", Tone.Muted, "Portée 25,5 al au plus, soute vide"), chips);
    }

    [Fact]
    public void Next_jump_out_of_range_from_a_neutron_star_asks_for_the_jet_then_fits()
    {
        var state = OnPlacedRoute("Bravo", 2, 20);
        var chip = Briefing.Flight(state, Flying())[0];
        Assert.Equal(("Saut 60 / 25,5 al · élan requis", Tone.Alert, true), (chip.Text, chip.Tone, chip.Loud));

        state.Apply(Line("19:02:00", "\"event\":\"JetConeBoost\", \"BoostValue\":4.0"));
        Assert.Contains(Briefing.Flight(state, Flying()), c => c.Text == "Saut 60 / 102 al" && !c.Loud);
    }

    [Fact]
    public void Next_jump_beyond_any_push_is_out_of_range()
    {
        var chip = Briefing.Flight(OnPlacedRoute("Charlie", 3, 80), Flying())[0];
        Assert.Equal(("Saut 100 / 25,5 al · hors de portée", Tone.Alert), (chip.Text, chip.Tone));
    }

    [Fact]
    public void Interdiction_and_overheating_are_loud_alerts()
    {
        var status = new GameStatus((1L << 24) | (1L << 23) | (1L << 20), 0, 0, 16, 50_000_000, "Clean");
        Assert.Equal(["Interdiction !", "Surchauffe"], Briefing.Alerts(new GameState(), status).Where(c => c.Loud).Select(c => c.Text));
    }

    [Fact]
    public void A_picked_up_material_shows_its_count_against_the_cap_for_a_while()
    {
        var state = Replay(
            Line("19:00:00", "\"event\":\"Materials\", \"Raw\":[ { \"Name\":\"polonium\", \"Count\":97 }, { \"Name\":\"nickel\", \"Count\":10 } ], \"Manufactured\":[ ], \"Encoded\":[ ]"),
            Line("19:01:00", "\"event\":\"MaterialCollected\", \"Category\":\"Raw\", \"Name\":\"nickel\", \"Count\":3"));
        Assert.Equal(new Chip("Nickel 13/300", Tone.Good, "Grade 1"), Briefing.Pickup(state, new DateTime(2026, 10, 9, 19, 1, 20, DateTimeKind.Utc)));
        Assert.Null(Briefing.Pickup(state, new DateTime(2026, 10, 9, 19, 1, 31, DateTimeKind.Utc)));

        // Polonium is grade 4: 150 at most; the craft took 50 of the 97.
        state.Apply(Line("19:02:00", "\"event\":\"EngineerCraft\", \"Ingredients\":[ { \"Name\":\"polonium\", \"Count\":50 } ]"));
        state.Apply(Line("19:03:00", "\"event\":\"MaterialCollected\", \"Category\":\"Raw\", \"Name\":\"polonium\", \"Name_Localised\":\"Polonium\", \"Count\":3"));
        Assert.Equal("Polonium 50/150", Briefing.Pickup(state, new DateTime(2026, 10, 9, 19, 3, 5, DateTimeKind.Utc))?.Text);

        state.Apply(Line("19:04:00", "\"event\":\"MaterialTrade\", \"TraderType\":\"raw\", \"Paid\":{ \"Material\":\"nickel\", \"Quantity\":6 }, \"Received\":{ \"Material\":\"polonium\", \"Quantity\":99 }"));
        state.Apply(Line("19:05:00", "\"event\":\"MaterialCollected\", \"Category\":\"Raw\", \"Name\":\"polonium\", \"Count\":3"));
        var chip = Briefing.Pickup(state, new DateTime(2026, 10, 9, 19, 5, 5, DateTimeKind.Utc));
        Assert.Equal(("Polonium 150/150 · plein", Tone.Alert), (chip?.Text, chip?.Tone));
        Assert.Equal(7, state.MaterialCount("nickel"));
    }

    [Fact]
    public void Bounties_worth_more_than_the_rebuy_turn_red()
    {
        var state = Replay(
            Line("07:00:00", "\"event\":\"Loadout\", \"Ship\":\"python_nx\", \"ShipID\":1, \"HullHealth\":1.0, \"CargoCapacity\":0, \"Rebuy\":5000000, \"FuelCapacity\":{ \"Main\":16.0 }, \"Modules\":[ ]"),
            Line("12:00:00", "\"event\":\"Bounty\", \"Rewards\":[ { \"Faction\":\"A\", \"Reward\":4000000 } ], \"TotalReward\":4000000"));
        Assert.Contains(Briefing.Banner(state, Flying(), PadBattery.None, Now), c => c.Text == "Primes 4,0 M Cr" && c.Tone == Tone.Good);

        state.Apply(Line("12:05:00", "\"event\":\"Bounty\", \"Rewards\":[ { \"Faction\":\"A\", \"Reward\":2000000 } ], \"TotalReward\":2000000"));
        Assert.Contains(Briefing.Banner(state, Flying(), PadBattery.None, Now), c => c.Text == "Primes 6,0 M Cr" && c.Tone == Tone.Alert);
    }

    [Fact]
    public void Bounties_add_up_until_cashed_in_faction_by_faction()
    {
        var state = Replay(
            Line("12:00:00", "\"event\":\"Bounty\", \"Rewards\":[ { \"Faction\":\"A\", \"Reward\":100000 } ], \"TotalReward\":100000"),
            Line("12:01:00", "\"event\":\"Bounty\", \"Rewards\":[ { \"Faction\":\"B\", \"Reward\":50000 }, { \"Faction\":\"A\", \"Reward\":20000 } ], \"TotalReward\":70000"));
        Assert.Equal(170_000, state.BountiesPending);

        state.Apply(Line("12:10:00", "\"event\":\"RedeemVoucher\", \"Type\":\"bounty\", \"Amount\":120000, \"Factions\":[ { \"Faction\":\"A\", \"Amount\":120000 } ]"));
        Assert.Equal(50_000, state.BountiesPending);

        state.Apply(Line("12:20:00", "\"event\":\"Died\""));
        Assert.Equal(0, state.BountiesPending);
    }

    [Fact]
    public void A_cashed_in_line_without_faction_and_with_10_percent_more_is_matched_by_its_amount()
    {
        // Cashed in on 2026-10-21: each line 10 % over what the journal earned, no faction given.
        var state = Replay(
            Line("12:00:00", "\"event\":\"Bounty\", \"Rewards\":[ { \"Faction\":\"Federal Reclamation Co\", \"Reward\":5474685 } ], \"TotalReward\":5474685"),
            Line("12:01:00", "\"event\":\"Bounty\", \"Rewards\":[ { \"Faction\":\"Polus Independents\", \"Reward\":798337 } ], \"TotalReward\":798337"));

        state.Apply(Line("12:10:00", "\"event\":\"RedeemVoucher\", \"Type\":\"bounty\", \"Amount\":6900323, " +
                                     "\"Factions\":[ { \"Faction\":\"\", \"Amount\":6022153 }, { \"Faction\":\"\", \"Amount\":878170 } ]"));
        Assert.Equal(0, state.BountiesPending);
    }

    [Fact]
    public void The_rate_counts_the_session_from_its_load()
    {
        var state = Replay(
            Line("19:00:00", "\"event\":\"Bounty\", \"Rewards\":[ { \"Faction\":\"A\", \"Reward\":999999 } ], \"TotalReward\":999999"),
            Line("19:00:00", "\"event\":\"LoadGame\", \"Commander\":\"Nova\", \"Credits\":1000"),
            Line("19:10:00", "\"event\":\"Bounty\", \"Rewards\":[ { \"Faction\":\"A\", \"Reward\":500000 } ], \"TotalReward\":500000"),
            Line("19:20:00", "\"event\":\"MultiSellExplorationData\", \"TotalEarnings\":500000"));

        Assert.Null(Briefing.Rate(state, new DateTime(2026, 10, 9, 19, 5, 0, DateTimeKind.Utc)));
        Assert.Equal(1_000_000, Briefing.Rate(state, new DateTime(2026, 10, 9, 20, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(1, state.SessionKills);
    }

    [Fact]
    public void Wanted_target_shows_its_bounty_a_clean_one_warns()
    {
        var wanted = Replay(Line("12:16:34", "\"event\":\"ShipTargeted\", \"TargetLocked\":true, \"Ship\":\"vulture\", \"ScanStage\":3, " +
                                             "\"PilotName_Localised\":\"zimrich\", \"PilotRank\":\"Deadly\", \"Faction\":\"Bugas Group\", \"LegalStatus\":\"Wanted\", \"Bounty\":406019"));
        Assert.Equal(new Chip("Cible Vulture · 406 k Cr", Tone.Good, "zimrich (mortel), recherché, Bugas Group"), Briefing.Target(wanted));

        var clean = Replay(Line("12:19:59", "\"event\":\"ShipTargeted\", \"TargetLocked\":true, \"Ship\":\"asp\", \"Ship_Localised\":\"Asp Explorer\", \"ScanStage\":3, \"LegalStatus\":\"Clean\", \"Bounty\":0"));
        Assert.Equal("Cible Asp Explorer · propre", Briefing.Target(clean)?.Text);

        clean.Apply(Line("12:20:08", "\"event\":\"ShipTargeted\", \"TargetLocked\":false"));
        Assert.Null(Briefing.Target(clean));
    }

    [Fact]
    public void High_grade_emissions_of_this_system_until_they_expire()
    {
        var state = Replay(
            Jump("19:50:00", "Alpha", 1),
            Line("19:50:10", "\"event\":\"FSSSignalDiscovered\", \"SystemAddress\":1, \"SignalName\":\"$USS_HighGradeEmissions;\", \"USSType\":\"$USS_Type_VeryValuableSalvage;\", \"TimeRemaining\":1200.0"),
            Line("19:50:10", "\"event\":\"FSSSignalDiscovered\", \"SystemAddress\":1, \"SignalName\":\"$USS_EncodedEmissions;\", \"USSType\":\"$USS_Type_ValuableSalvage;\", \"TimeRemaining\":1200.0"));

        Assert.Equal("HGE · niv. 5", Briefing.HighGrade(state, Now)?.Text);
        Assert.Null(Briefing.HighGrade(state, Now.AddMinutes(11)));

        state.Apply(Jump("19:55:00", "Bravo", 2));
        Assert.Null(Briefing.HighGrade(state, Now));
    }

    [Fact]
    public void Docked_reminders_limpets_fuel_hull_bounties()
    {
        var state = Replay(
            Line("07:00:00", "\"event\":\"Loadout\", \"Ship\":\"asp\", \"ShipID\":23, \"HullHealth\":0.87, \"CargoCapacity\":64, \"Rebuy\":1000, \"FuelCapacity\":{ \"Main\":32.0, \"Reserve\":0.6 }, \"Modules\":[ { \"Slot\":\"Slot01\", \"Item\":\"int_dronecontrol_collection_size3_class5\" } ]"),
            Line("07:00:01", "\"event\":\"Cargo\", \"Vessel\":\"Ship\", \"Count\":52, \"Inventory\":[ { \"Name\":\"drones\", \"Count\":52 } ]"),
            Line("08:00:00", "\"event\":\"Cargo\", \"Vessel\":\"Ship\", \"Count\":12, \"Inventory\":[ { \"Name\":\"drones\", \"Count\":12 } ]"),
            Line("08:10:00", "\"event\":\"Bounty\", \"Faction\":\"A\", \"Reward\":30000"));

        var reminders = Briefing.Reminders(state, Docked(fuel: 20));
        Assert.Equal(["Limpets 12/52", "Plein 62 %", "Coque 87 %", "Encaisser 30 k Cr"], reminders.Select(c => c.Text));

        state.Apply(Line("08:20:00", "\"event\":\"RepairAll\", \"Cost\":100"));
        state.Apply(Line("08:21:00", "\"event\":\"BuyDrones\", \"Type\":\"Drones\", \"Count\":40"));
        Assert.Equal(["Plein 62 %", "Encaisser 30 k Cr"], Briefing.Reminders(state, Docked(fuel: 20)).Select(c => c.Text));
    }

    [Fact]
    public void Banner_puts_the_alerts_first_and_the_controller_last()
    {
        var state = Replay(
            Line("07:00:00", "\"event\":\"Loadout\", \"Ship\":\"asp\", \"ShipID\":23, \"HullHealth\":1.0, \"CargoCapacity\":0, \"Rebuy\":90000000, \"FuelCapacity\":{ \"Main\":32.0 }, \"Modules\":[ ]"));
        var status = new GameStatus((1L << 24) | 1, 0, 0, 32, 50_000_000, "Speeding");

        var chips = Briefing.Banner(state, status, new PadBattery(PadLink.Wireless, 15), Now);
        Assert.Equal(["Rachat non couvert", "Excès de vitesse", "Prêt au départ", "Manette 15 %"], chips.Select(c => c.Text));
        Assert.Equal(Tone.Alert, chips[^1].Tone);
    }

    [Theory]
    [InlineData(0L, 0L, 0, false)]                    // main menu
    [InlineData(1L << 24, 0L, 0, true)]               // in the ship
    [InlineData(1L << 24, 0L, 6, false)]              // galaxy map
    [InlineData(1L << 24, 0L, 2, true)]               // right panel
    [InlineData(1L << 26, 0L, 0, true)]               // in the SRV
    [InlineData(0L, 1L, 0, true)]                     // on foot
    [InlineData(0L, 1L, 7, false)]                    // on foot, system map
    public void Banner_in_the_ship_the_SRV_or_on_foot_outside_the_maps(long flags, long flags2, int focus, bool visible) =>
        Assert.Equal(visible, Briefing.BannerVisible(new GameStatus(flags, flags2, focus, null, null, null)));

    [Theory]
    [InlineData(950, "950 Cr")]
    [InlineData(406019, "406 k Cr")]
    [InlineData(2814759, "2,8 M Cr")]
    public void Credits_read_short(long amount, string text) =>
        Assert.Equal(text, Briefing.Credits(amount));

    // ------------------------------------------------------------------------------- the warning

    private static DateTime At(string time) => DateTime.Parse($"2026-10-09T{time}Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);

    private const string Python = "\"event\":\"Loadout\", \"Ship\":\"python_nx\", \"ShipID\":1, \"HullHealth\":1.0, \"CargoCapacity\":0, \"Rebuy\":1000, \"FuelCapacity\":{ \"Main\":16.0 }, \"Modules\":[ ]";

    [Fact]
    public void Fallen_shields_warn_big_then_smaller_and_big_again_on_a_hit()
    {
        var state = Replay(Line("18:00:00", Python), Line("19:00:00", "\"event\":\"ShieldState\", \"ShieldsUp\":false"));

        Assert.Equal(new Warning("Boucliers tombés", "Coque 100 % · depuis 5 s", SeeThrough: true), Briefing.Warning(state, Flying(), At("19:00:05")));
        Assert.False(Briefing.Warning(state, Flying(), At("19:00:30"))?.Big);
        Assert.Contains(Briefing.Alerts(state, Flying()), c => c is { Text: "Boucliers tombés", Loud: true });

        state.Apply(Line("19:00:40", "\"event\":\"UnderAttack\", \"Target\":\"You\""));
        Assert.True(Briefing.Warning(state, Flying(), At("19:00:45"))?.Big);

        state.Apply(Line("19:01:30", "\"event\":\"ShieldState\", \"ShieldsUp\":true"));
        Assert.Null(Briefing.Warning(state, Flying(), At("19:01:31")));
    }

    [Fact]
    public void Shields_dropped_at_the_station_say_nothing()
    {
        // Outfitting drops and raises them; and a new session starts with them up.
        var state = Replay(Line("18:00:00", Python), Line("19:00:00", "\"event\":\"ShieldState\", \"ShieldsUp\":false"));
        Assert.Null(Briefing.Warning(state, Docked(), At("19:00:01")));
        Assert.DoesNotContain(Briefing.Alerts(state, Docked()), c => c.Text == "Boucliers tombés");

        state.Apply(Line("20:00:00", "\"event\":\"LoadGame\", \"Commander\":\"Nova\", \"Ship\":\"python_nx\", \"ShipID\":1"));
        Assert.Null(Briefing.Warning(state, Flying(), At("20:00:01")));
    }

    [Fact]
    public void A_hull_hit_below_60_percent_warns_for_a_while()
    {
        var state = Replay(Line("18:00:00", Python), Line("19:00:00", "\"event\":\"HullDamage\", \"Health\":0.45, \"PlayerPilot\":true, \"Fighter\":false"));
        Assert.Equal(new Warning("Coque 45 %", "La coque prend des coups", SeeThrough: true), Briefing.Warning(state, Flying(), At("19:00:03")));
        Assert.Null(Briefing.Warning(state, Flying(), At("19:00:20")));
    }

    private static string Targeted(string time, string ship, string rank, string legal = "Wanted") =>
        Line(time, $"\"event\":\"ShipTargeted\", \"TargetLocked\":true, \"Ship\":\"{ship}\", \"ScanStage\":3, " +
            $"\"PilotName_Localised\":\"Jo\", \"PilotRank\":\"{rank}\", \"LegalStatus\":\"{legal}\", \"Bounty\":200000, \"Faction\":\"F\"");

    private const string AspWithLasers = "\"event\":\"Loadout\", \"Ship\":\"asp\", \"ShipID\":2, \"HullHealth\":1.0, \"CargoCapacity\":0, \"Rebuy\":1000, " +
        "\"Modules\":[ { \"Slot\":\"MediumHardpoint1\", \"Item\":\"hpt_pulselaser_gimbal_medium\" } ]";

    [Fact]
    public void A_target_far_stronger_than_the_ship_is_a_danger()
    {
        var state = Replay(Line("18:00:00", AspWithLasers), Targeted("19:00:00", "python", "Dangerous"));

        var chip = Briefing.Target(state);
        Assert.Equal("Cible Python · 200 k Cr · danger", chip?.Text);
        Assert.Equal("Python dangereux face à votre Asp Explorer : menace ×2,8, à éviter", chip?.Detail);
        Assert.True(chip?.Loud);

        var warning = Briefing.Warning(state, Flying(), At("19:00:04"));
        Assert.Equal("Cible trop dangereuse", warning?.Title);
        Assert.True(warning?.SeeThrough);
        Assert.Null(Briefing.Warning(state, Flying(), At("19:00:30")));
    }

    [Fact]
    public void A_clean_target_is_weighed_but_not_warned_in_the_middle()
    {
        var state = Replay(Line("18:00:00", AspWithLasers), Targeted("19:00:00", "anaconda", "Elite", legal: "Clean"));
        Assert.True(Briefing.Target(state)?.Loud);
        Assert.Null(Briefing.Warning(state, Flying(), At("19:00:04")));
    }

    [Fact]
    public void Engineering_weighs_in_the_ship_s_favour()
    {
        // Twelve combat modules at grade 5: half again the Python Mk II's weight.
        var modules = string.Join(", ", Enumerable.Range(1, 12).Select(i =>
            $"{{ \"Slot\":\"LargeHardpoint{i}\", \"Item\":\"hpt_multicannon_gimbal_large\", \"Engineering\":{{ \"Level\":5 }} }}"));
        var loadout = $"\"event\":\"Loadout\", \"Ship\":\"python_nx\", \"ShipID\":1, \"HullHealth\":1.0, \"CargoCapacity\":0, \"Rebuy\":1000, \"Modules\":[ {modules} ]";

        var anaconda = Replay(Line("18:00:00", loadout), Targeted("19:00:00", "anaconda", "Elite"));
        Assert.Equal("Cible Anaconda · 200 k Cr", Briefing.Target(anaconda)?.Text);

        var corvette = Replay(Line("18:00:00", loadout), Targeted("19:00:00", "federation_corvette", "Elite"));
        Assert.Equal(new Chip("Cible Corvette · 200 k Cr · coriace", Tone.Warn,
            "Corvette élite face à votre Python Mk II : menace ×1,3, prudence"), Briefing.Target(corvette));
    }

    [Fact]
    public void A_dry_stretch_ahead_asks_to_fill_the_tank_here()
    {
        // Alpha, a G star, then two dry stars: three jumps at 4 t before Delta.
        var state = Replay(Line("18:00:00", Python), Jump("19:00:00", "Alpha", 1, fuelUsed: 4), Line("19:00:01", Route), Honked);

        var warning = Briefing.Warning(state, Flying(fuel: 12), At("19:00:05"));
        Assert.Equal(new Warning("Faites le plein ici", "Réservoir 75 % : ~3 sauts pour 3 jusqu'à une étoile écopable", true, Tone.Warn), warning);
        Assert.False(Briefing.Warning(state, Flying(fuel: 12), At("19:00:30"))?.Big);

        var scooping = new GameStatus((1L << 24) | (1L << 11), 0, 0, 13, 50_000_000, "Clean");
        Assert.False(Briefing.Warning(state, scooping, At("19:00:05"))?.Big);

        // A full tank, or a jump to spare, and nothing to say.
        Assert.Null(Briefing.Warning(state, Flying(fuel: 16), At("19:00:05")));
    }

    [Fact]
    public void A_full_tank_short_of_the_stretch_is_left_to_the_band()
    {
        // 8 t a jump: a full tank of 16 t holds two of the three, and filling more cannot help.
        var state = Replay(Line("18:00:00", Python), Jump("19:00:00", "Alpha", 1, fuelUsed: 8), Line("19:00:01", Route), Honked);
        Assert.Null(Briefing.Warning(state, Flying(fuel: 16), At("19:00:05")));
        Assert.Equal("Carburant ~2 sauts", Briefing.FuelShortfall(state, Flying(fuel: 16))?.Text);
    }

    [Fact]
    public void A_dry_star_here_cannot_be_filled_at()
    {
        var state = Replay(Line("18:00:00", Python), Jump("19:00:00", "Bravo", 2, fuelUsed: 4), Line("19:00:01", Route));
        Assert.Null(Briefing.FillHere(state, Flying(fuel: 4), At("19:00:05")));
    }

    [Fact]
    public void A_tank_filled_here_is_not_asked_again_as_supercruise_burns_a_little()
    {
        // Filled before the jump to Alpha: that was the system before, it does not count here.
        var state = Replay(Line("18:00:00", Python), Line("18:59:00", "\"event\":\"FuelScoop\", \"Scooped\":2.0, \"Total\":16.0"),
            Jump("19:00:00", "Alpha", 1, fuelUsed: 4), Line("19:00:01", Route), Honked);
        Assert.NotNull(Briefing.Warning(state, Flying(fuel: 12), At("19:00:05")));

        state.Apply(Line("19:01:00", "\"event\":\"FuelScoop\", \"Scooped\":4.0, \"Total\":16.0"));
        Assert.Null(Briefing.Warning(state, Flying(fuel: 15.8), At("19:02:00")));
    }

    [Theory]
    [InlineData(17)] // FSD charging
    [InlineData(30)] // in the jump
    public void The_jump_under_way_hides_the_fill_warning(int flag)
    {
        var state = Replay(Line("18:00:00", Python), Jump("19:00:00", "Alpha", 1, fuelUsed: 4), Line("19:00:01", Route), Honked);
        var jumping = new GameStatus((1L << 24) | (1L << flag), 0, 0, 12, 50_000_000, "Clean");
        Assert.Null(Briefing.Warning(state, jumping, At("19:00:05")));
    }
}
