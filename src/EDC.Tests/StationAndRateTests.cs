using System.Globalization;
using EDC.Companion;
using Xunit;

namespace EDC.Tests;

public sealed class StationAndRateTests
{
    private static string Line(string time, string body) => $"{{ \"timestamp\":\"2026-10-09T{time}Z\", {body} }}";

    private static GameState Replay(params string[] lines)
    {
        var state = new GameState();
        foreach (var line in lines)
            state.Apply(line);
        return state;
    }

    private static GameStatus Docked() => new((1L << 24) | 1, 0, 0, 16, 50_000_000, "Clean");
    private static GameStatus Flying() => new(1L << 24, 0, 0, 16, 50_000_000, "Clean");

    private const string Missiles = "{ \"Slot\":\"MediumHardpoint1\", \"Item\":\"hpt_dumbfiremissilerack_fixed_medium\", \"On\":true, \"Health\":1.0, \"AmmoInClip\":8, \"AmmoInHopper\":16 }";
    private const string Mines = "{ \"Slot\":\"SmallHardpoint1\", \"Item\":\"hpt_minelauncher_fixed_small\", \"On\":true, \"Health\":1.0, \"AmmoInClip\":1, \"AmmoInHopper\":36 }";
    private const string Laser = "{ \"Slot\":\"LargeHardpoint1\", \"Item\":\"hpt_pulselaser_gimbal_large\", \"On\":true, \"Health\":1.0 }";

    private static string Loadout(string time, int shipId, params string[] modules) =>
        Line(time, $"\"event\":\"Loadout\", \"Ship\":\"krait_mkii\", \"ShipID\":{shipId}, \"HullHealth\":1.0, \"CargoCapacity\":0, \"Rebuy\":1000, \"MaxJumpRange\":20, \"Modules\":[ {string.Join(", ", modules)} ]");

    private static string Bounty(string time) =>
        Line(time, "\"event\":\"Bounty\", \"Rewards\":[ { \"Faction\":\"A\", \"Reward\":100000 } ], \"TotalReward\":100000");

    private static Chip? Rearm(GameState state) => Briefing.Reminders(state, Docked()).FirstOrDefault(c => c.Text == "Réarmer");

    [Fact]
    public void Missiles_and_mines_used_since_the_last_restock_are_reminded_at_the_station()
    {
        var state = Replay(Loadout("18:00:00", 1, Missiles, Mines, Laser), Bounty("18:30:00"));

        var chip = Rearm(state);

        Assert.NotNull(chip);
        Assert.Equal("Missiles, mines : entamés depuis le dernier réarmement", chip.Detail);
        Assert.Contains(Briefing.Banner(state, Docked(), EDC.Core.PadBattery.None, DateTime.UtcNow), c => c.Text == "Réarmer");
    }

    [Fact]
    public void Nothing_to_restock_after_buying_ammo_without_a_fight_or_without_ammo()
    {
        Assert.Null(Rearm(Replay(Loadout("18:00:00", 1, Missiles), Bounty("18:30:00"), Line("18:40:00", "\"event\":\"BuyAmmo\", \"Cost\":1200"))));
        Assert.Null(Rearm(Replay(Loadout("18:00:00", 1, Missiles))));
        Assert.Null(Rearm(Replay(Loadout("18:00:00", 1, Laser), Bounty("18:30:00"))));
        // Another ship comes out of the shipyard armed.
        Assert.Null(Rearm(Replay(Loadout("18:00:00", 1, Missiles), Bounty("18:30:00"), Loadout("18:40:00", 2, Missiles))));
    }

    private static string WaterWorld(string time, string system, string body) =>
        Line(time, $"\"event\":\"Scan\", \"ScanType\":\"Detailed\", \"StarSystem\":\"{system}\", \"BodyName\":\"{body}\", \"SystemAddress\":1, " +
                   "\"PlanetClass\":\"Water world\", \"TerraformState\":\"\", \"MassEM\":0.975783, \"WasDiscovered\":true, \"WasMapped\":false");

    private static string Sell(string time, string system, long earned) =>
        Line(time, $"\"event\":\"MultiSellExplorationData\", \"Discovered\":[ {{ \"SystemName\":\"{system}\", \"NumBodies\":1 }} ], \"BaseValue\":1, \"Bonus\":0, \"TotalEarnings\":{earned}");

    [Fact]
    public void The_exploration_rate_counts_what_was_scanned_this_session_sold_or_not()
    {
        var state = Replay(
            WaterWorld("18:00:00", "Alpha", "Alpha 2"),
            Line("19:00:00", "\"event\":\"LoadGame\", \"Commander\":\"Nova\", \"Credits\":1000"),
            WaterWorld("19:10:00", "Beta", "Beta 2"),
            WaterWorld("19:20:00", "Gamma", "Gamma 2"),
            Sell("19:30:00", "Alpha", 90_000),
            Sell("19:40:00", "Gamma", 90_000));

        Assert.Equal(2, state.ExplorationSessionCount);
        // Alpha 2 came from the session before: its sale is not this session's exploration.
        Assert.Equal(2 * 101_341L / 2, Briefing.ExplorationRate(state, new DateTime(2026, 10, 9, 21, 0, 0, DateTimeKind.Utc)));
        Assert.Null(Briefing.ExplorationRate(state, new DateTime(2026, 10, 9, 19, 5, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void The_banner_gives_the_exploration_rate_next_to_the_session_s()
    {
        var state = Replay(
            Line("19:00:00", "\"event\":\"LoadGame\", \"Commander\":\"Nova\", \"Credits\":1000"),
            WaterWorld("19:10:00", "Beta", "Beta 2"));

        var chips = Briefing.Banner(state, Flying(), EDC.Core.PadBattery.None, new DateTime(2026, 10, 9, 20, 0, 0, DateTimeKind.Utc));

        Assert.Contains(chips, c => c.Text == $"Explo {Briefing.Credits(101_341)}/h");
    }

    [Fact]
    public void The_tabs_take_what_there_is_to_sell_out_of_the_station_reminders()
    {
        var state = Replay(WaterWorld("18:00:00", "Alpha", "Alpha 2"), Bounty("18:30:00"),
            Line("18:40:00", "\"event\":\"FactionKillBond\", \"Reward\":50000, \"AwardingFaction\":\"B\", \"VictimFaction\":\"C\""));

        Assert.Contains(Briefing.Reminders(state, Docked()), c => c.Text.StartsWith("Exploration"));
        Assert.Contains(Briefing.Reminders(state, Docked()), c => c.Text.StartsWith("Encaisser"));
        Assert.DoesNotContain(Briefing.Reminders(state, Docked(), sales: false), c => c.Text.StartsWith("Exploration") || c.Text.StartsWith("Encaisser"));
        Assert.Equal($"Exploration {Briefing.Credits(101_341)}", Assert.Single(Briefing.ExplorationSales(state)).Text);
        Assert.Equal([$"Encaisser {Briefing.Credits(100_000)}", $"Encaisser {Briefing.Credits(50_000)} d'obligations"], Briefing.CombatSales(state).Select(c => c.Text));
    }

    [Fact]
    public void The_combat_rate_counts_the_bounties_and_bonds_of_the_session_not_the_exploration_sold()
    {
        var state = Replay(
            Line("19:00:00", "\"event\":\"LoadGame\", \"Commander\":\"Nova\", \"Credits\":1000"),
            Bounty("19:10:00"),
            Line("19:20:00", "\"event\":\"FactionKillBond\", \"Reward\":50000, \"AwardingFaction\":\"B\", \"VictimFaction\":\"C\""),
            Sell("19:30:00", "Alpha", 900_000));

        Assert.Equal(150_000 / 2, Briefing.CombatRate(state, new DateTime(2026, 10, 9, 21, 0, 0, DateTimeKind.Utc)));
        Assert.Null(Briefing.CombatRate(state, new DateTime(2026, 10, 9, 19, 5, 0, DateTimeKind.Utc)));
    }
}
