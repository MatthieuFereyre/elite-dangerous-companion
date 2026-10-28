using EDC.Core;
using EDC.Resources;

namespace EDC.Companion;

/// <summary>The ranks the journal's Rank and Progress events give, in the order of the game's status panel.</summary>
public enum RankKind { Combat, Trade, Explore, Soldier, Exobiologist, Cqc, Federation, Empire }

/// <summary>A rank as the Commander tab shows it: <paramref name="Unlocks"/> only for a navy rank whose next gives something.</summary>
public sealed record CommanderRank(RankKind Kind, string Title, string Name, int Progress, string Next, string How, string? Unlocks = null);

/// <summary>
/// The ranks with their progress, and the reputation with each superpower. The rank names are the game's
/// own, kept in English: proper names, as the ships are.
/// </summary>
public static class Commander
{
    private static readonly string[] Elite = ["Elite", "Elite I", "Elite II", "Elite III", "Elite IV", "Elite V"];

    private static string[] Pilot(params string[] names) => [.. names, .. Elite];

    private static readonly Dictionary<RankKind, string[]> Names = new()
    {
        [RankKind.Combat] = Pilot("Harmless", "Mostly Harmless", "Novice", "Competent", "Expert", "Master", "Dangerous", "Deadly"),
        [RankKind.Trade] = Pilot("Penniless", "Mostly Penniless", "Peddler", "Dealer", "Merchant", "Broker", "Entrepreneur", "Tycoon"),
        [RankKind.Explore] = Pilot("Aimless", "Mostly Aimless", "Scout", "Surveyor", "Trailblazer", "Pathfinder", "Ranger", "Pioneer"),
        [RankKind.Soldier] = Pilot("Defenceless", "Mostly Defenceless", "Rookie", "Soldier", "Gunslinger", "Warrior", "Gladiator", "Deadeye"),
        [RankKind.Exobiologist] = Pilot("Directionless", "Mostly Directionless", "Compiler", "Collector", "Cataloguer", "Taxonomist", "Ecologist", "Geneticist"),
        [RankKind.Cqc] = Pilot("Helpless", "Mostly Helpless", "Amateur", "Semi Professional", "Professional", "Champion", "Hero", "Legend"),
        [RankKind.Federation] = ["None", "Recruit", "Cadet", "Midshipman", "Petty Officer", "Chief Petty Officer", "Warrant Officer", "Ensign",
            "Lieutenant", "Lieutenant Commander", "Post Commander", "Post Captain", "Rear Admiral", "Vice Admiral", "Admiral"],
        [RankKind.Empire] = ["None", "Outsider", "Serf", "Master", "Squire", "Knight", "Lord", "Baron", "Viscount", "Count", "Earl",
            "Marquis", "Duke", "Prince", "King"],
    };

    // What reaching a navy rank gives: its ships, its permits, an engineer's invitation.
    private sealed record Unlock(string[] Ships, string[] Permits, string? Engineer = null);

    private static readonly Dictionary<(RankKind, int), Unlock> Unlocks = new()
    {
        [(RankKind.Federation, 3)] = new(["Federal Dropship"], [], "The Sarge"),
        [(RankKind.Federation, 4)] = new([], ["Sol"]),
        [(RankKind.Federation, 5)] = new(["Federal Assault Ship"], ["Vega", "Beta Hydri"]),
        [(RankKind.Federation, 6)] = new([], ["PLX 695"]),
        [(RankKind.Federation, 7)] = new(["Federal Gunship"], ["Ross 128"]),
        [(RankKind.Federation, 8)] = new([], ["Exbeur"]),
        [(RankKind.Federation, 10)] = new([], ["Hors"]),
        [(RankKind.Federation, 12)] = new(["Federal Corvette"], []),
        [(RankKind.Empire, 1)] = new([], [], "Hera Tani"),
        [(RankKind.Empire, 3)] = new(["Imperial Courier"], []),
        [(RankKind.Empire, 4)] = new([], ["Achenar"]),
        [(RankKind.Empire, 7)] = new(["Imperial Clipper"], ["Summerland"]),
        [(RankKind.Empire, 10)] = new([], ["Facece"]),
        [(RankKind.Empire, 12)] = new(["Imperial Cutter"], []),
    };

    private static bool Navy(RankKind kind) => kind is RankKind.Federation or RankKind.Empire;

    private static string Title(RankKind kind) => kind switch
    {
        RankKind.Combat => Strings.RankCombat,
        RankKind.Trade => Strings.RankTrade,
        RankKind.Explore => Strings.RankExplore,
        RankKind.Soldier => Strings.RankSoldier,
        RankKind.Exobiologist => Strings.RankExobiologist,
        RankKind.Cqc => Strings.RankCqc,
        RankKind.Federation => Strings.RankFederation,
        _ => Strings.RankEmpire,
    };

    private static string How(RankKind kind) => kind switch
    {
        RankKind.Combat => Strings.RankCombatHow,
        RankKind.Trade => Strings.RankTradeHow,
        RankKind.Explore => Strings.RankExploreHow,
        RankKind.Soldier => Strings.RankSoldierHow,
        RankKind.Exobiologist => Strings.RankExobiologistHow,
        RankKind.Cqc => Strings.RankCqcHow,
        _ => Strings.RankNavyHow,
    };

    /// <summary>The ranks, once the game has given them at its start.</summary>
    public static IReadOnlyList<CommanderRank> Ranks(GameState state) =>
        Enum.GetValues<RankKind>()
            .Where(state.Ranks.ContainsKey)
            .Select(kind => Rank(kind, state.Ranks[kind], state.RankProgress.GetValueOrDefault(kind)))
            .ToList();

    private static CommanderRank Rank(RankKind kind, int rank, int progress)
    {
        var names = Names[kind];
        var name = rank >= 0 && rank < names.Length ? names[rank] : rank.ToString(Loc.Culture);
        var next = rank + 1 < names.Length ? names[rank + 1] : null;
        var text = next is null ? Strings.RankTop
            : Navy(kind) && progress >= 100 ? string.Format(Strings.RankPromotion, next)
            : string.Format(Strings.RankProgressTo, progress, next);
        return new CommanderRank(kind, Title(kind), name, progress, text, How(kind),
            next is not null && Unlocks.TryGetValue((kind, rank + 1), out var unlock) ? string.Format(Strings.RankUnlocks, next, Describe(unlock)) : null);
    }

    private static string Describe(Unlock unlock) => string.Join(", ",
        unlock.Ships
            .Concat(unlock.Permits.Select(p => string.Format(Strings.RankPermit, p)))
            .Concat(unlock.Engineer is { } engineer ? [string.Format(Strings.RankInvitation, engineer)] : []));

    /// <summary>"Fédération · 99 % · alliée": the standing with each superpower, as the game's status panel names it.</summary>
    public static IReadOnlyList<string> Reputation(GameState state) =>
        new[] { ("Federation", Strings.RepFederation), ("Empire", Strings.RepEmpire), ("Alliance", Strings.RepAlliance), ("Independent", Strings.RepIndependent) }
            .Where(p => state.Reputation.ContainsKey(p.Item1))
            .Select(p => $"{p.Item2} · {Math.Round(state.Reputation[p.Item1]).ToString(Loc.Culture)} % · {Standing(state.Reputation[p.Item1])}")
            .ToList();

    // The thresholds of the journal's documentation.
    private static string Standing(double reputation) => reputation switch
    {
        > 90 => Strings.RepAllied,
        > 35 => Strings.RepFriendly,
        > 4 => Strings.RepCordial,
        > -35 => Strings.RepNeutral,
        > -90 => Strings.RepUnfriendly,
        _ => Strings.RepHostile,
    };
}
