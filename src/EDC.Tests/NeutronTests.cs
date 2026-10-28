using System.Globalization;
using System.IO;
using System.Net.Http;
using EDC.Companion;
using EDC.Core;
using EDC.ViewModels;
using Xunit;

namespace EDC.Tests;

public sealed class NeutronTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    // Spansh's answers for Ross 1057 → Colonia at 25.5 al: /api/route, then /api/results/<job>, cut to four steps.
    private const string Job = """{"job":"73858FA8-CB33-11F1-B73B-B2C966D86A3D","status":"queued"}""";
    private const string Queued = """
        {"created_at":"2026-10-18T20:42:24","job":"6CB6E43C-CB34-11F1-9993-9135E3A406D6","parameters":{"efficiency":"60","from":"Sol","range":"20",
         "to":"Beagle Point","via":[]},"state":"started","status":"queued","updated_at":"2026-10-18T20:42:24"}
        """;
    private const string Done = """
        {"created_at":"2026-10-18T20:35:26","job":"73858FA8-CB33-11F1-B73B-B2C966D86A3D","result":{"destination_system":"Colonia","distance":21998.8008155283,
         "efficiency":"60","range":"25.5","source_system":"Ross 1057","system_jumps":[
          {"distance_jumped":0,"distance_left":21998.8008155283,"id64":2869440947617,"jumps":0,"neutron_star":false,"system":"Ross 1057","x":-32.3125,"y":26.1875,"z":-12.4375},
          {"distance_jumped":421.557170787294,"distance_left":21629.3877108911,"id64":22712681061,"jumps":19,"neutron_star":true,"system":"PSR J1752-2806","x":-10.9,"y":-6.8,"z":407.2},
          {"distance_jumped":379.383912838054,"distance_left":21349.9114306519,"id64":5531763309,"jumps":14,"neutron_star":true,"system":"Nova Aquila No 3","x":-363.0,"y":3.3,"z":548.2},
          {"distance_jumped":49.7434433493501,"distance_left":0,"id64":3238296097059,"jumps":1,"neutron_star":false,"system":"Colonia","x":-9530.5,"y":-910.2,"z":19808.1}],
         "total_jumps":3,"via":[]},"state":"completed","status":"ok"}
        """;

    private static NeutronRoute Route => NeutronPlotter.ParseResult(Done)!;

    private static string Line(string time, string body) => $"{{ \"timestamp\":\"2026-10-09T{time}Z\", {body} }}";

    private static string Jump(string system) => Line("19:00:00", $"\"event\":\"FSDJump\", \"StarSystem\":\"{system}\", \"SystemAddress\":1, \"FuelUsed\":2, \"FuelLevel\":10");

    // ------------------------------------------------------------------------------- Spansh

    [Fact]
    public void Spansh_gives_a_job_then_the_route_once_done()
    {
        Assert.Equal("73858FA8-CB33-11F1-B73B-B2C966D86A3D", NeutronPlotter.ParseJob(Job));
        Assert.Null(NeutronPlotter.ParseResult(Queued));

        var route = Route;
        Assert.Equal(("Ross 1057", "Colonia", 25.5, 34, 4), (route.From, route.To, route.Range, route.TotalJumps, route.Steps.Count));
        Assert.Equal(new NeutronStep("PSR J1752-2806", 19, true, 21629.3877108911), route.Steps[1]);
    }

    [Theory]
    [InlineData("Could not find finishing system", "Système d'arrivée introuvable")]
    [InlineData("Could not find starting system", "Système de départ introuvable")]
    [InlineData("Something else", "Something else")]
    public void Spansh_s_errors_are_said_in_French(string error, string message)
    {
        var json = $$"""{"error":"{{error}}"}""";
        Assert.Equal(message, Assert.Throws<NeutronException>(() => NeutronPlotter.ParseJob(json)).Message);
        Assert.Equal(message, Assert.Throws<NeutronException>(() => NeutronPlotter.ParseResult(json)).Message);
    }

    [Fact]
    public void A_step_reads_where_its_jumps_start_its_star_and_what_is_left()
    {
        var steps = new Harness(Route).Model.Steps;
        Assert.Equal($"départ · {21999.ToString("N0", French)} al", steps[0].Detail);
        Assert.Equal($"19 sauts depuis Ross 1057 · étoile à neutrons · reste {21629.ToString("N0", French)} al", steps[1].Detail);
        Assert.Equal("1 saut depuis Nova Aquila No 3 · arrivée", steps[3].Detail);
    }

    // ------------------------------------------------------------------------------- progress

    [Fact]
    public void The_route_moves_on_as_its_systems_are_reached_never_back()
    {
        var route = Route;
        Assert.Equal("PSR J1752-2806", route.Next?.System);

        // A jump between two steps, where the game's own route goes through plain stars.
        route = route.Advance("Wolf 123");
        Assert.Equal(0, route.Reached);

        route = route.Advance("psr j1752-2806");
        Assert.Equal((1, "Nova Aquila No 3"), (route.Reached, route.Next?.System));

        route = route.Advance("Ross 1057");
        Assert.Equal(1, route.Reached);

        route = route.Advance("Colonia");
        Assert.Equal(3, route.Reached);
        Assert.Null(route.Next);
    }

    [Fact]
    public void The_route_and_where_it_stands_are_kept_in_the_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"neutron-{Guid.NewGuid():N}.json");
        try
        {
            new PlayerSettings { NeutronRoute = Route.Advance("PSR J1752-2806"), NeutronAutoCopy = true }.Save(path);
            var loaded = PlayerSettings.Load(path);
            Assert.Equal(("Ross 1057", "Colonia", 25.5, 1, true), (loaded.NeutronRoute?.From, loaded.NeutronRoute?.To, loaded.NeutronRoute?.Range, loaded.NeutronRoute?.Reached, loaded.NeutronAutoCopy));
            Assert.Equal(Route.Steps, loaded.NeutronRoute?.Steps);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ------------------------------------------------------------------------------- the tab

    private sealed class Harness
    {
        public readonly PlayerSettings Settings = new();
        public readonly List<string> Copied = [];
        public readonly List<(string From, string To, double Range)> Plotted = [];
        public int Saves;
        public Func<NeutronRoute> Answer = () => Route;
        public readonly NeutronViewModel Model;

        public Harness(NeutronRoute? kept = null)
        {
            Settings.NeutronRoute = kept;
            Model = new NeutronViewModel(Settings, () => Saves++, text => { Copied.Add(text); return true; },
                (from, to, range, _) => { Plotted.Add((from, to, range)); return Task.FromResult(Answer()); });
        }
    }

    private static GameState InRoss() => Replay(
        Line("18:00:00", "\"event\":\"Loadout\", \"Ship\":\"python_nx\", \"ShipID\":1, \"HullHealth\":1.0, \"CargoCapacity\":0, \"Rebuy\":1000, \"MaxJumpRange\":25.5, \"Modules\":[ ]"),
        Jump("Ross 1057"));

    private static GameState Replay(params string[] lines)
    {
        var state = new GameState();
        foreach (var line in lines)
            state.Apply(line);
        return state;
    }

    [Fact]
    public void The_start_and_the_range_come_from_the_game_until_typed()
    {
        var harness = new Harness();
        harness.Model.Refresh(InRoss());
        Assert.Equal(("Ross 1057", "25,5"), (harness.Model.From, harness.Model.RangeText));

        harness.Model.From = "Sol";
        harness.Model.Refresh(InRoss());
        Assert.Equal("Sol", harness.Model.From);
    }

    private static GameState InMandalay() => Replay(
        Line("18:00:00", "\"event\":\"Loadout\", \"Ship\":\"python_nx\", \"ShipID\":1, \"HullHealth\":1.0, \"CargoCapacity\":0, \"Rebuy\":1000, \"MaxJumpRange\":25.5, \"Modules\":[ ]"),
        Jump("Ross 1057"),
        Line("18:10:00", "\"event\":\"Loadout\", \"Ship\":\"mandalay\", \"ShipID\":7, \"HullHealth\":1.0, \"CargoCapacity\":0, \"Rebuy\":1000, \"MaxJumpRange\":61.27, \"Modules\":[ ]"));

    [Fact]
    public void The_range_is_the_current_ship_s_even_with_a_route_kept_from_another()
    {
        var harness = new Harness(Route);

        harness.Model.Refresh(InMandalay());

        Assert.Equal("61,27", harness.Model.RangeText);
    }

    [Fact]
    public void A_range_typed_holds_until_the_ship_changes()
    {
        var harness = new Harness();
        harness.Model.Refresh(InRoss());
        harness.Model.RangeText = "20";

        harness.Model.Refresh(InRoss());
        Assert.Equal("20", harness.Model.RangeText);

        harness.Model.Refresh(InMandalay());
        Assert.Equal("61,27", harness.Model.RangeText);
    }

    [Fact]
    public async Task Plotting_lists_the_steps_points_to_the_next_and_keeps_the_route()
    {
        var harness = new Harness();
        harness.Model.Refresh(InRoss());
        harness.Model.To = "Colonia";
        harness.Model.RangeText = "25.5";
        await harness.Model.PlotCommand.ExecuteAsync(null);

        Assert.Equal(("Ross 1057", "Colonia", 25.5), Assert.Single(harness.Plotted));
        Assert.Equal(["Ross 1057", "PSR J1752-2806", "Nova Aquila No 3", "Colonia"], harness.Model.Steps.Select(s => s.System));
        Assert.Equal("PSR J1752-2806", harness.Model.Next?.System);
        Assert.Equal($"4 étapes · 34 sauts · {21999.ToString("N0", French)} al", harness.Model.Summary);
        Assert.Equal("Cliquer sur une étape pour la copier, puis la coller dans la carte de la galaxie", harness.Model.StatusText);
        Assert.NotNull(harness.Settings.NeutronRoute);
        Assert.Equal(1, harness.Saves);
    }

    [Fact]
    public async Task A_route_without_a_neutron_star_says_to_plot_straight_to_the_end()
    {
        var harness = new Harness
        {
            Answer = () => new NeutronRoute("Bugas", "Muang", 25.53,
                [new NeutronStep("Bugas", 0, false, 197), new NeutronStep("Muang", 9, false, 0)]),
        };
        harness.Model.Refresh(InRoss());
        harness.Model.To = "Muang";
        await harness.Model.PlotCommand.ExecuteAsync(null);
        Assert.Equal("Aucune étoile à neutrons ne raccourcit ce trajet : tracer Muang dans la carte de la galaxie, le jeu y va en 9 sauts",
            harness.Model.StatusText);
    }

    [Fact]
    public async Task Without_a_destination_nothing_is_asked()
    {
        var harness = new Harness();
        harness.Model.Refresh(InRoss());
        await harness.Model.PlotCommand.ExecuteAsync(null);
        Assert.Empty(harness.Plotted);
        Assert.Equal("Indiquez le système d'arrivée", harness.Model.StatusText);
    }

    [Fact]
    public async Task A_failed_plot_says_why()
    {
        var harness = new Harness { Answer = () => throw new NeutronException("Système d'arrivée introuvable") };
        harness.Model.Refresh(InRoss());
        harness.Model.To = "Nowhere";
        await harness.Model.PlotCommand.ExecuteAsync(null);
        Assert.Equal("Système d'arrivée introuvable", harness.Model.StatusText);

        harness.Answer = () => throw new HttpRequestException("timeout");
        await harness.Model.PlotCommand.ExecuteAsync(null);
        Assert.Equal("Calcul impossible : timeout", harness.Model.StatusText);
    }

    [Fact]
    public void A_click_copies_the_step()
    {
        var harness = new Harness(Route);
        harness.Model.CopyStepCommand.Execute(harness.Model.Steps[2]);
        Assert.Equal(["Nova Aquila No 3"], harness.Copied);
        Assert.Equal("« Nova Aquila No 3 » copié : à coller dans la carte de la galaxie", harness.Model.StatusText);
    }

    [Fact]
    public void Reaching_a_step_marks_it_done_and_copies_the_next_unless_unticked()
    {
        var harness = new Harness(Route);
        Assert.True(harness.Model.AutoCopy);
        harness.Model.Refresh(Replay(Jump("PSR J1752-2806")));
        Assert.Equal([true, true, false, false], harness.Model.Steps.Select(s => s.Done));
        Assert.True(harness.Model.Steps[2].IsNext);
        Assert.Equal(1, harness.Settings.NeutronRoute?.Reached);
        Assert.Equal(["Nova Aquila No 3"], harness.Copied);

        // The same system again: nothing new to copy.
        harness.Model.Refresh(Replay(Jump("PSR J1752-2806")));
        Assert.Single(harness.Copied);

        harness.Model.AutoCopy = false;
        Assert.False(harness.Settings.NeutronAutoCopy);
        harness.Model.Refresh(Replay(Jump("Nova Aquila No 3")));
        Assert.Single(harness.Copied);
    }

    [Fact]
    public void Copying_the_next_step_is_on_by_default() =>
        Assert.True(new PlayerSettings().NeutronAutoCopy);

    [Fact]
    public void A_route_kept_from_before_comes_back_with_where_it_stood()
    {
        var harness = new Harness(Route.Advance("PSR J1752-2806"));
        Assert.Equal(4, harness.Model.Steps.Count);
        Assert.Equal("Nova Aquila No 3", harness.Model.Next?.System);
        Assert.Equal(("Ross 1057", "Colonia"), (harness.Model.From, harness.Model.To));
    }

    [Fact]
    public void Clearing_forgets_the_route()
    {
        var harness = new Harness(Route);
        harness.Model.ClearCommand.Execute(null);
        Assert.Empty(harness.Model.Steps);
        Assert.Null(harness.Model.Next);
        Assert.Null(harness.Settings.NeutronRoute);
    }

    [Fact]
    public void One_tab_shown_hides_all_the_others()
    {
        var model = new PlayerViewModel { ShowNeutron = true };
        Assert.Equal((false, false, false, true, false), (model.ShowMusic, model.ShowCombat, model.ShowExploration, model.ShowNeutron, model.ShowSettings));

        // The radio group unchecking a tab writes false: it must change nothing.
        model.ShowMusic = false;
        Assert.True(model.ShowNeutron);
        model.ShowSettings = true;
        Assert.Equal((false, false, false, false, true), (model.ShowMusic, model.ShowCombat, model.ShowExploration, model.ShowNeutron, model.ShowSettings));
    }

    [Fact]
    public void The_music_tab_shows_what_comes_next_or_the_folders()
    {
        var model = new PlayerViewModel();
        Assert.Equal((true, false), (model.ShowPlaylistPane, model.ShowFoldersPane));

        model.ShowFoldersPane = true;
        model.ShowPlaylistPane = false;

        Assert.Equal((false, true), (model.ShowPlaylistPane, model.ShowFoldersPane));
    }
}
