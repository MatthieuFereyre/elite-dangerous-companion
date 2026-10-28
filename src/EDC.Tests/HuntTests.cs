using System.Globalization;
using EDC.Companion;
using Xunit;

namespace EDC.Tests;

public sealed class HuntTests
{
    private static string Ls(int ls) => ls.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"));

    // Rows of edtools.cc/res.php?s=Alioth&f_hz=on, as the page gives them.
    private const string Edtools = """
        <table><tr><th>Distance</th><th>System</th><th>Beacon</th><th>Haz</th><th>High</th><th>Reg</th><th>Low</th><th>Notes</th></tr>
        <tr class="even"><td class="dst">17.29</td><td><a href="javascript:setCopied(51);" class="btn" title="Copy system name to clipboard" data-clipboard-text="NLTT 34715"><img src="ico/copy.png" height="16px" width="16px"></a> <span id="cpb_51">NLTT 34715</span></td><td></td><td><span class="hvr">yes (x2)<span class="ttip">A1:443 ls<br>A3:670 ls</span></span></td><td></td><td></td><td></td><td></td></tr>
        <tr><td class="dst">29.25</td><td><a href="javascript:setCopied(49);" class="btn" data-clipboard-text="Ori"><img src="ico/copy.png"></a> <span id="cpb_49">Ori</span></td><td></td><td>A3:2148 ls</td><td></td><td></td><td>A3:2148 ls</td><td>Closest outpost 430K ls!</td></tr>
        <tr><td class="dst">31.02</td><td><a class="btn" data-clipboard-text="Wolf &#039;s Rest"><img src="ico/copy.png"></a></td><td></td><td>2:500 ls</td><td></td><td></td><td></td><td></td></tr>
        <tr><td class="dst">33.10</td><td><a class="btn" data-clipboard-text="Asellus Primus"><img src="ico/copy.png"></a></td><td></td><td>Asellus_3:1365 ls</td><td></td><td></td><td></td><td></td></tr>
        <tr><td class="dst">35.40</td><td><a class="btn" data-clipboard-text="YZ Ceti"><img src="ico/copy.png"></a></td><td></td><td><span class="hvr">yes (x2)<span class="ttip">A11<br> A10:1231 ls</span></span></td><td></td><td></td><td></td><td></td></tr>
        </table>
        """;

    [Fact]
    public void Edtools_gives_the_systems_with_a_hazardous_RES_nearest_first()
    {
        var hazards = HuntFinder.ParseHazards(Edtools);
        Assert.Equal(["NLTT 34715", "Ori", "Wolf 's Rest", "Asellus Primus", "YZ Ceti"], hazards.Select(h => h.System));
        Assert.Equal([17.29, 29.25, 31.02, 33.10, 35.40], hazards.Select(h => h.Distance));
    }

    [Fact]
    public void Each_system_says_on_which_planet_s_rings_its_hazardous_RES_are()
    {
        // edtools writes « A10:1231 ls », the game « YZ Ceti A 10 »; the nearest to the arrival star first.
        Assert.Equal(
            ["Planètes A 1 · 443 ls, A 3 · 670 ls", $"Planète A 3 · {Ls(2148)} ls", "Planète 2 · 500 ls",
             $"Planète Asellus 3 · {Ls(1365)} ls", $"Planètes A 10 · {Ls(1231)} ls, A 11"],
            HuntFinder.ParseHazards(Edtools).Select(h => h.Where));
    }

    // Spansh's systems search, cut to what is read.
    private const string Spansh = """
        { "count":3, "results":[
          { "name":"NLTT 34715", "government":"Anarchy", "controlling_power":"Edmund Mahon" },
          { "name":"Ori", "government":"Corporate", "controlling_power":"Edmund Mahon" },
          { "name":"Wolf 's Rest", "government":"Patronage", "controlling_power":"Jerome Archer" } ] }
        """;

    [Fact]
    public void Spansh_gives_each_system_its_government_and_power()
    {
        var systems = HuntFinder.ParseSystems(Spansh);
        Assert.Equal(new HuntSystem("Anarchy", "Edmund Mahon"), systems["NLTT 34715"]);
        Assert.Equal(new HuntSystem("Patronage", "Jerome Archer"), systems["Wolf 's Rest"]);
    }

    [Fact]
    public void The_nearest_leaves_out_anarchies_and_the_power_s_one_follows_the_pledge()
    {
        // An anarchy has no law: its pirates are clean there, no bounty to claim.
        var result = HuntFinder.Pick(HuntFinder.ParseHazards(Edtools), HuntFinder.ParseSystems(Spansh), "Jerome Archer");
        Assert.Equal(new HuntSpot("Ori", 29.25, "Edmund Mahon", $"Planète A 3 · {Ls(2148)} ls"), result.Nearest);
        Assert.Equal(new HuntSpot("Wolf 's Rest", 31.02, "Jerome Archer", "Planète 2 · 500 ls"), result.OfPower);
    }

    [Fact]
    public void A_system_Spansh_does_not_know_may_be_the_nearest_but_not_the_power_s()
    {
        var hazards = new[] { new HuntCandidate("Unknown", 5), new HuntCandidate("Ori", 29.25) };
        var result = HuntFinder.Pick(hazards, HuntFinder.ParseSystems(Spansh), "Jerome Archer");
        Assert.Equal(new HuntSpot("Unknown", 5, null), result.Nearest);
        Assert.Null(result.OfPower);
    }

    [Fact]
    public void Without_a_pledge_there_is_no_power_s_system()
    {
        var result = HuntFinder.Pick(HuntFinder.ParseHazards(Edtools), HuntFinder.ParseSystems(Spansh), null);
        Assert.NotNull(result.Nearest);
        Assert.Null(result.OfPower);
    }

    [Theory]
    [InlineData(47.02, 25.5, "47 al · ~2 sauts")]
    [InlineData(17.29, 25.5, "17,3 al · ~1 saut")]
    [InlineData(51.0, 25.5, "51 al · ~2 sauts")]
    [InlineData(10.18, 0, "10,2 al")]
    [InlineData(0, 25.5, "ici")]
    [InlineData(0, 0, "ici")]
    public void A_spot_reads_its_distance_and_the_jumps_at_the_ship_s_range(double distance, double range, string text)
    {
        // The range is the most the FSD does: the jumps are a floor, the route between the stars adds some.
        Assert.Equal(text, new HuntSpot("Ross 490", distance, "Jerome Archer").Trip(range));
    }

    private static string Line(string body) => $"{{ \"timestamp\":\"2026-10-09T19:00:00Z\", {body} }}";

    [Fact]
    public void The_power_comes_from_the_journal_and_leaves_with_the_pledge()
    {
        var state = new GameState();
        state.Apply(Line("\"event\":\"Powerplay\", \"Power\":\"Jerome Archer\", \"Rank\":0, \"Merits\":14008"));
        Assert.Equal("Jerome Archer", state.Power);

        state.Apply(Line("\"event\":\"PowerplayDefect\", \"FromPower\":\"Jerome Archer\", \"ToPower\":\"Edmund Mahon\""));
        Assert.Equal("Edmund Mahon", state.Power);

        state.Apply(Line("\"event\":\"PowerplayLeave\", \"Power\":\"Edmund Mahon\""));
        Assert.Null(state.Power);

        state.Apply(Line("\"event\":\"PowerplayJoin\", \"Power\":\"Jerome Archer\""));
        Assert.Equal("Jerome Archer", state.Power);
    }
}
