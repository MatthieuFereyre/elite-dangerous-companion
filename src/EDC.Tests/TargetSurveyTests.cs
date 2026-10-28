using EDC.Companion;
using Xunit;

namespace EDC.Tests;

/// <summary>On the galaxy map, the panel tells whether the system targeted is still to discover, known in part, or fully scanned.</summary>
public sealed class TargetSurveyTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 19, 30, 0, DateTimeKind.Utc);

    private static string Line(string body) => $"{{ \"timestamp\":\"2026-10-09T19:00:00Z\", {body} }}";

    private static GameState Replay(params string[] lines)
    {
        var state = new GameState { MinNotableValue = 700_000 };
        state.Apply(Line("\"event\":\"FSDJump\", \"StarSystem\":\"Alpha\", \"SystemAddress\":1, \"StarPos\":[0,0,0], \"FuelUsed\":1, \"FuelLevel\":16"));
        foreach (var line in lines)
            state.Apply(line);
        return state;
    }

    private static string NavRoute(params (string System, long Address)[] hops) =>
        Line($"\"event\":\"NavRoute\", \"Route\":[ {string.Join(", ", hops.Select(h => $"{{ \"StarSystem\":\"{h.System}\", \"SystemAddress\":{h.Address}, \"StarPos\":[0,0,0], \"StarClass\":\"K\" }}"))} ]");

    private static string FsdTarget(string system, long address) =>
        Line($"\"event\":\"FSDTarget\", \"Name\":\"{system}\", \"SystemAddress\":{address}, \"StarClass\":\"K\"");

    private static GameStatus GalaxyMap() => new(1L << 24, 0, 6, 16, 50_000_000, "Clean");
    private static GameStatus Flying() => new(1L << 24, 0, 0, 16, 50_000_000, "Clean");

    // A system as EDSM's api-system-v1/bodies gives it, cut down to what is read.
    private static string Bodies(int? count, params string[] bodies) =>
        $$"""{"id":1,"id64":4,"name":"Delta","bodyCount":{{(count?.ToString() ?? "null")}},"bodies":[{{string.Join(",", bodies)}}]}""";

    private const string Star = """{"name":"Delta A","type":"Star","subType":"K (Yellow-Orange) Star","distanceToArrival":0}""";
    private const string EarthLike = """{"name":"Delta 1","type":"Planet","subType":"Earth-like world","terraformingState":"Not terraformable","earthMasses":1,"distanceToArrival":800}""";
    private const string Icy = """{"name":"Delta 2","type":"Planet","subType":"Icy body","terraformingState":"Not terraformable","earthMasses":0.01,"distanceToArrival":900}""";

    [Fact]
    public void EDSM_tells_whether_a_system_is_unknown_known_in_part_or_fully_scanned()
    {
        Assert.Equal(SurveyKind.Undiscovered, SystemSurvey.Parse("{}").Kind);
        Assert.Equal(SurveyKind.Undiscovered, SystemSurvey.Parse("[]").Kind);

        var partial = SystemSurvey.Parse(Bodies(5, Star, Icy));
        Assert.Equal((SurveyKind.Partial, 2, 5), (partial.Kind, partial.Known, partial.Count));
        Assert.Equal(SurveyKind.Partial, SystemSurvey.Parse(Bodies(null, Star)).Kind);
        Assert.Equal(SurveyKind.Partial, SystemSurvey.Parse(Bodies(null)).Kind);

        var complete = SystemSurvey.Parse(Bodies(3, Star, EarthLike, Icy));
        Assert.Equal((SurveyKind.Complete, 3, 3), (complete.Kind, complete.Known, complete.Count));
        Assert.Equal(["Delta 1", "Delta 2"], complete.Planets.Select(b => b.Name));
    }

    [Fact]
    public void The_target_is_the_end_of_the_route_or_the_system_targeted_without_one()
    {
        var state = Replay(FsdTarget("Beta", 2));
        Assert.Equal("Beta", state.TargetSystem?.System);

        state.Apply(NavRoute(("Alpha", 1), ("Beta", 2), ("Gamma", 3), ("Delta", 4)));
        state.Apply(FsdTarget("Beta", 2));
        Assert.Equal("Delta", state.TargetSystem?.System);
    }

    [Fact]
    public void Each_target_is_asked_of_EDSM_once_and_a_late_answer_waits_for_its_system()
    {
        var state = Replay(NavRoute(("Alpha", 1), ("Beta", 2), ("Delta", 4)));
        Assert.Equal(4, state.TargetSurveyWanted);
        state.SurveyAsked(4);
        Assert.Null(state.TargetSurveyWanted);
        Assert.Null(state.TargetSurvey);

        state.Apply(NavRoute(("Alpha", 1), ("Gamma", 3)));
        Assert.Equal(3, state.TargetSurveyWanted);
        state.SetSurvey(4, SystemSurvey.Parse("{}"));
        Assert.Null(state.TargetSurvey);

        // Back to Delta: its answer was kept.
        state.Apply(NavRoute(("Alpha", 1), ("Beta", 2), ("Delta", 4)));
        Assert.Null(state.TargetSurveyWanted);
        Assert.Equal(SurveyKind.Undiscovered, state.TargetSurvey?.Kind);
    }

    [Fact]
    public void On_the_galaxy_map_the_panel_tells_only_what_the_target_holds()
    {
        var state = Replay(NavRoute(("Alpha", 1), ("Beta", 2), ("Delta", 4)));
        state.SetSurvey(4, SystemSurvey.Parse("{}"));

        Assert.True(Briefing.PanelVisible(GalaxyMap()));
        Assert.Equal(new[]
            {
                new PanelLine("Cible · Delta", Tone.Info, Header: true),
                new PanelLine("Jamais découvert", Tone.Good, Loud: true),
            },
            Briefing.Panel(state, GalaxyMap(), Now));
        // In flight, the route tells of it.
        Assert.DoesNotContain(Briefing.Panel(state, Flying(), Now), l => l.Text.StartsWith("Cible"));

        state.SetSurvey(4, SystemSurvey.Parse(Bodies(5, Star, Icy)));
        Assert.Equal(new[] { "Cible · Delta", "Connu, incomplet · 2/5 corps" }, Briefing.Panel(state, GalaxyMap(), Now).Select(l => l.Text));

        // The planets above the threshold, with what they pay.
        state.SetSurvey(4, SystemSurvey.Parse(Bodies(3, Star, EarthLike, Icy)));
        Assert.Equal(new[] { "Cible · Delta", "Entièrement scanné · 3 corps", "1 planète · 1,5 M Cr" },
            Briefing.Panel(state, GalaxyMap(), Now).Select(l => l.Text));
    }

    [Fact]
    public void Without_an_answer_yet_or_a_target_the_galaxy_map_shows_no_panel()
    {
        var state = Replay(NavRoute(("Alpha", 1), ("Delta", 4)));
        Assert.Empty(Briefing.Panel(state, GalaxyMap(), Now));
        Assert.Empty(Briefing.Panel(Replay(), GalaxyMap(), Now));
    }
}
