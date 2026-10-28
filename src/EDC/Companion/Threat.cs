namespace EDC.Companion;

public enum Danger { None, Tough, Deadly }

/// <summary>
/// A rough weight of a target against the commander's ship: what the hull is worth in a fight, times
/// the pilot's rank, which also sets how well the game fits and engineers the NPC. The NPC's actual
/// modules are not in the journal; this is an order of magnitude, not a simulation.
/// </summary>
public static class Threat
{
    /// <summary>The ship as NPCs fly it, on a scale of 10: hull, shields and hardpoints together.</summary>
    private static readonly Dictionary<string, double> Hulls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hauler"] = 0.5, ["sidewinder"] = 1, ["adder"] = 1, ["dolphin"] = 1, ["type6"] = 1,
        ["independent_fighter"] = 1, ["empire_fighter"] = 1, ["federation_fighter"] = 1,
        ["eagle"] = 1.5, ["type7"] = 1.5, ["empire_eagle"] = 2, ["asp_scout"] = 2, ["type8"] = 2,
        ["cobramkiii"] = 2.5, ["cobramkiv"] = 2.5, ["diamondback"] = 2.5, ["diamondbackxl"] = 2.5,
        ["type9"] = 2.5, ["independant_trader"] = 2.5, ["orca"] = 2.5,
        ["viper"] = 3, ["viper_mkiv"] = 3, ["asp"] = 3, ["empire_courier"] = 3, ["belugaliner"] = 3,
        ["cobramkv"] = 3.5, ["mandalay"] = 4, ["federation_dropship"] = 4,
        ["vulture"] = 4.5, ["krait_light"] = 4.5,
        ["federation_dropship_mkii"] = 5, ["federation_gunship"] = 5, ["empire_trader"] = 5, ["panthermkii"] = 5,
        ["typex_2"] = 5.5, ["typex"] = 6, ["mamba"] = 6, ["corsair"] = 6, ["typex_3"] = 6.5,
        ["ferdelance"] = 7, ["krait_mkii"] = 7, ["python"] = 7,
        ["python_nx"] = 7.5, ["type9_military"] = 7.5,
        ["anaconda"] = 8, ["cutter"] = 9, ["federation_corvette"] = 9.5,
    };

    /// <summary>From this ratio the target is a match to be careful with, and from the next one, one to avoid.</summary>
    public const double ToughRatio = 1.2;
    public const double DeadlyRatio = 1.5;

    public static double? Hull(string? ship) => ship is not null && Hulls.TryGetValue(ship, out var weight) ? weight : null;

    public static double RankFactor(string? rank) => rank switch
    {
        "Harmless" => 0.6,
        "Mostly Harmless" => 0.7,
        "Novice" => 0.8,
        "Competent" => 0.9,
        "Master" => 1.1,
        "Dangerous" => 1.2,
        "Deadly" => 1.35,
        "Elite" => 1.5,
        _ => 1,
    };

    /// <summary>
    /// The commander's ship: its hull, up to half again for engineering (a fully engineered combat fit
    /// reaches it), and a third without a weapon.
    /// </summary>
    public static double? Own(GameState state) =>
        Hull(state.ShipSymbol) is { } hull
            ? hull * (1 + 0.5 * Math.Min(1, state.Engineering / 12)) * (state.Armed ? 1 : 0.3)
            : null;

    /// <summary>The target against the ship, once its pilot's rank is known (scan stage 1).</summary>
    public static double? Ratio(GameState state, Target target) =>
        target.Rank is not null && Hull(target.ShipSymbol) is { } hull && Own(state) is { } own
            ? hull * RankFactor(target.Rank) / own
            : null;

    public static Danger Of(double? ratio) => ratio switch
    {
        >= DeadlyRatio => Danger.Deadly,
        >= ToughRatio => Danger.Tough,
        _ => Danger.None,
    };
}
