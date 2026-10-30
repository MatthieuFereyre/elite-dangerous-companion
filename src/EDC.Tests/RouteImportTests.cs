using System.IO;
using EDC.Companion;
using EDC.Core;
using EDC.ViewModels;
using Xunit;

namespace EDC.Tests;

/// <summary>
/// A route plotted on spansh.co.uk, given by the link of its results: pasted in the Route tab or passed
/// with --route. Its steps are reached by arriving there, as the neutron route's are.
/// </summary>
public sealed class RouteImportTests
{
    // Spansh's road to riches from Delta, cut to three systems, back to Delta: a list of systems.
    private const string Riches = """
        {"job":"81AE387E-D497-11F1-929E-BF12A0EE1EAE","parameters":{"loop":"1","range":"85","source":"Delta"},"result":[
          {"bodies":[],"id64":"1","jumps":1,"name":"Delta","x":0,"y":0,"z":0},
          {"bodies":[{"name":"Epsilon A 1"}],"id64":"2","jumps":2,"name":"Epsilon","x":30,"y":40,"z":0},
          {"bodies":[{"name":"Zeta 3"}],"id64":"3","jumps":1,"name":"Zeta","x":30,"y":40,"z":60},
          {"bodies":[],"id64":"1","jumps":2,"name":"Delta","x":0,"y":0,"z":0}],
         "state":"completed","status":"ok"}
        """;

    // Spansh's tourist planner from Delta to Delta by way of Zeta and Epsilon: its jumps.
    private const string Tourist = """
        {"job":"AD9B931E-D497-11F1-B91D-8FE779A83374","result":{"destination_systems":["Epsilon","Zeta"],"final_destination_system":"Eta",
          "job":"AD9B931E-D497-11F1-B91D-8FE779A83374","range":"85","source_system":"Delta","system_jumps":[
          {"distance":0,"id64":1,"jumps":0,"system":"Delta","x":0,"y":0,"z":0},
          {"distance":50,"id64":2,"jumps":1,"system":"Epsilon","x":30,"y":40,"z":0},
          {"distance":60,"id64":3,"jumps":1,"system":"Zeta","x":30,"y":40,"z":60},
          {"distance":60,"id64":4,"jumps":1,"system":"Eta","x":30,"y":40,"z":120}]},
         "state":"completed","status":"ok"}
        """;

    private static NeutronRoute RichesRoute => NeutronPlotter.ParseResult(Riches)!;

    // ------------------------------------------------------------------------------- the link

    [Fact]
    public void A_results_link_gives_the_job_and_what_to_plot_it_again_with()
    {
        var link = SpanshLink.Parse(" https://spansh.co.uk/riches/results/E556F464-D209-11F1-A19E-8FCB01ECED59?from=Synuefe%20EN-H%20d11-96&loop=1&range=85 ");

        Assert.NotNull(link);
        Assert.Equal(("E556F464-D209-11F1-A19E-8FCB01ECED59", "riches/route"), (link.Job, link.Endpoint));
        Assert.Equal([("from", "Synuefe EN-H d11-96"), ("loop", "1"), ("range", "85")], link.Parameters);
    }

    [Fact]
    public void A_list_in_the_link_is_given_one_value_at_a_time()
    {
        var link = SpanshLink.Parse("https://spansh.co.uk/tourist/results/AD9B?destination=%5B%22Epsilon%22%2C%22Zeta%22%5D&range=85&source=Delta");

        Assert.Equal("tourist/route", link?.Endpoint);
        Assert.Equal([("destination", "Epsilon"), ("destination", "Zeta"), ("range", "85"), ("source", "Delta")], link?.Parameters);
    }

    [Fact]
    public void The_neutron_plotter_s_link_is_plotted_by_its_own_address()
    {
        Assert.Equal("route", SpanshLink.Parse("https://spansh.co.uk/plotter/results/73858FA8?efficiency=60&from=Sol&range=20&to=Colonia")?.Endpoint);
    }

    [Theory]
    [InlineData("https://example.com/riches/results/E556?from=Sol")]
    [InlineData("Synuefe EN-H d11-96")]
    [InlineData("https://spansh.co.uk/")]
    [InlineData("")]
    public void Anything_else_is_no_route(string text) => Assert.Null(SpanshLink.Parse(text));

    // ------------------------------------------------------------------------------- Spansh's answers

    [Fact]
    public void A_road_to_riches_is_its_systems_from_the_first_to_the_last()
    {
        var route = RichesRoute;

        Assert.Equal(["Delta", "Epsilon", "Zeta", "Delta"], route.Steps.Select(s => s.System));
        Assert.Equal(("Delta", "Delta", 85.0), (route.From, route.To, route.Range));
        // The first is where it starts from: no jump to it.
        Assert.Equal([0, 2, 1, 2], route.Steps.Select(s => s.Jumps));
        // What is left is the way still to go, step by step: 50, 60, then about 78 back.
        Assert.Equal(188.1, route.Steps[0].DistanceLeft, 1);
        Assert.Equal(138.1, route.Steps[1].DistanceLeft, 1);
        Assert.Equal(0, route.Steps[3].DistanceLeft);
        Assert.All(route.Steps, s => Assert.False(s.Neutron));
    }

    [Fact]
    public void A_tourist_route_is_its_jumps_to_its_last_destination()
    {
        var route = NeutronPlotter.ParseResult(Tourist)!;

        Assert.Equal(["Delta", "Epsilon", "Zeta", "Eta"], route.Steps.Select(s => s.System));
        Assert.Equal(("Delta", "Eta", 85.0, 3), (route.From, route.To, route.Range, route.TotalJumps));
        Assert.Equal(170, route.Steps[0].DistanceLeft, 1);
    }

    // ------------------------------------------------------------------------------- the tab

    private sealed class Harness
    {
        public readonly PlayerSettings Settings = new();
        public readonly List<string> Copied = [];
        public readonly List<string> Imported = [];
        public int Saves;
        public Func<NeutronRoute> Answer = () => RichesRoute;
        public readonly NeutronViewModel Model;

        public Harness()
        {
            Model = new NeutronViewModel(Settings, () => Saves++, text => { Copied.Add(text); return true; },
                import: (link, _) => { Imported.Add(link); return Task.FromResult(Answer()); });
        }
    }

    private static GameState In(string system)
    {
        var state = new GameState();
        state.Apply($"{{ \"timestamp\":\"2026-10-30T19:00:00Z\", \"event\":\"FSDJump\", \"StarSystem\":\"{system}\", \"SystemAddress\":9, \"FuelUsed\":2, \"FuelLevel\":10 }}");
        return state;
    }

    [Fact]
    public async Task A_route_loaded_away_from_it_starts_with_its_first_system_copied()
    {
        var harness = new Harness();
        harness.Model.Refresh(In("Sol"));
        harness.Model.Link = "https://spansh.co.uk/riches/results/81AE";

        await harness.Model.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["https://spansh.co.uk/riches/results/81AE"], harness.Imported);
        Assert.Equal(["Delta", "Epsilon", "Zeta", "Delta"], harness.Model.Steps.Select(s => s.System));
        Assert.Equal((0, "Delta"), (harness.Model.Next?.Number, harness.Model.Next?.System));
        Assert.Equal(["Delta"], harness.Copied);
        Assert.Equal(-1, harness.Settings.NeutronRoute?.Reached);
        Assert.Equal(1, harness.Saves);
    }

    [Fact]
    public async Task A_route_loaded_from_one_of_its_systems_goes_on_from_there()
    {
        var harness = new Harness();
        harness.Model.Refresh(In("Epsilon"));

        await harness.Model.ImportAsync("https://spansh.co.uk/riches/results/81AE");

        Assert.Equal("Zeta", harness.Model.Next?.System);
        Assert.Equal(["Zeta"], harness.Copied);

        // Then arriving at the next one moves it on, as with a neutron route.
        harness.Model.Refresh(In("Zeta"));
        Assert.Equal((3, "Delta"), (harness.Model.Next?.Number, harness.Model.Next?.System));
        Assert.Equal(["Zeta", "Delta"], harness.Copied);
    }

    [Fact]
    public async Task A_link_spansh_cannot_give_says_why_and_keeps_the_route()
    {
        var harness = new Harness();
        await harness.Model.ImportAsync("https://spansh.co.uk/riches/results/81AE");
        harness.Answer = () => throw new NeutronException("Ce n'est pas le lien d'une route Spansh");

        await harness.Model.ImportAsync("Sol");

        Assert.Equal("Ce n'est pas le lien d'une route Spansh", harness.Model.StatusText);
        Assert.Equal(4, harness.Model.Steps.Count);
    }

    [Fact]
    public void The_neutron_plotter_is_shown_unless_unticked_and_kept_so()
    {
        var harness = new Harness();
        Assert.True(harness.Model.ShowPlotter);

        harness.Model.ShowPlotter = false;

        Assert.False(harness.Settings.ShowNeutronPlotter);
        Assert.Equal(1, harness.Saves);
        Assert.False(new NeutronViewModel(harness.Settings, () => { }).ShowPlotter);
    }

    // ------------------------------------------------------------------------------- --route

    [Fact]
    public void A_link_given_to_a_second_launch_is_taken_once_by_the_first()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"edc-route-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var paths = new AppPaths(folder);
            RouteRequest.Put(paths, "https://spansh.co.uk/riches/results/81AE");

            Assert.Equal("https://spansh.co.uk/riches/results/81AE", RouteRequest.Take(paths));
            Assert.Null(RouteRequest.Take(paths));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
