using EDC.Companion;
using Xunit;

namespace EDC.Tests;

/// <summary>The Commander tab: each rank, its progress to the next, and what that one asks and gives.</summary>
public sealed class RankTests
{
    private static string Line(string body) => $"{{ \"timestamp\":\"2026-10-28T09:46:08Z\", {body} }}";

    private const string Rank = "\"event\":\"Rank\", \"Combat\":4, \"Trade\":5, \"Explore\":7, \"Soldier\":0, \"Exobiologist\":0, \"Empire\":2, \"Federation\":4, \"CQC\":0";
    private const string Progress = "\"event\":\"Progress\", \"Combat\":93, \"Trade\":9, \"Explore\":21, \"Soldier\":0, \"Exobiologist\":0, \"Empire\":100, \"Federation\":16, \"CQC\":5";
    private const string Reputation = "\"event\":\"Reputation\", \"Empire\":99.102303, \"Federation\":98.698799, \"Independent\":7.496940, \"Alliance\":82.416603";

    private static GameState Replay(params string[] bodies)
    {
        var state = new GameState();
        foreach (var body in bodies)
            state.Apply(Line(body));
        return state;
    }

    private static CommanderRank Row(GameState state, RankKind kind) => Commander.Ranks(state).Single(r => r.Kind == kind);

    [Fact]
    public void Each_rank_is_told_with_its_progress_to_the_next()
    {
        var state = Replay(Rank, Progress);

        Assert.Equal([RankKind.Combat, RankKind.Trade, RankKind.Explore, RankKind.Soldier, RankKind.Exobiologist, RankKind.Cqc, RankKind.Federation, RankKind.Empire],
            Commander.Ranks(state).Select(r => r.Kind));
        var combat = Row(state, RankKind.Combat);
        Assert.Equal(("Combat", "Expert", 93, "93 % vers Master"), (combat.Title, combat.Name, combat.Progress, combat.Next));
        Assert.Equal(("Pioneer", "21 % vers Elite"), (Row(state, RankKind.Explore).Name, Row(state, RankKind.Explore).Next));
        Assert.Equal("Defenceless", Row(state, RankKind.Soldier).Name);
        Assert.StartsWith("Détruire des vaisseaux", combat.How);
    }

    [Fact]
    public void Nothing_is_told_before_the_game_gives_the_ranks()
    {
        Assert.Empty(Commander.Ranks(new GameState()));
    }

    [Fact]
    public void A_promotion_takes_the_next_rank_from_nothing()
    {
        var state = Replay(Rank, Progress, "\"event\":\"Promotion\", \"Explore\":8");

        var explore = Row(state, RankKind.Explore);
        Assert.Equal(("Elite", 0, "0 % vers Elite I"), (explore.Name, explore.Progress, explore.Next));

        state.Apply(Line("\"event\":\"Promotion\", \"Explore\":13"));
        Assert.Equal(("Elite V", "Rang le plus haut"), (Row(state, RankKind.Explore).Name, Row(state, RankKind.Explore).Next));
    }

    [Fact]
    public void A_navy_rank_tells_what_the_next_gives_and_at_100_the_promotion_mission()
    {
        var state = Replay(Rank, Progress);

        var empire = Row(state, RankKind.Empire);
        Assert.Equal(("Marine impériale", "Serf"), (empire.Title, empire.Name));
        Assert.Equal("100 % : une mission de promotion pour devenir Master", empire.Next);
        Assert.Equal("Master débloque : Imperial Courier", empire.Unlocks);

        var federation = Row(state, RankKind.Federation);
        Assert.Equal(("Petty Officer", "16 % vers Chief Petty Officer"), (federation.Name, federation.Next));
        Assert.Equal("Chief Petty Officer débloque : Federal Assault Ship, permis Vega, permis Beta Hydri", federation.Unlocks);

        // A rank that gives nothing says nothing.
        state.Apply(Line("\"event\":\"Promotion\", \"Federation\":8"));
        Assert.Null(Row(state, RankKind.Federation).Unlocks);
    }

    [Fact]
    public void The_reputation_with_each_superpower_is_told()
    {
        var state = Replay(Rank, Progress, Reputation);

        Assert.Equal(["Fédération · 99 % · alliée", "Empire · 99 % · alliée", "Alliance · 82 % · amicale", "Indépendants · 7 % · cordiale"],
            Commander.Reputation(state));
    }
}
