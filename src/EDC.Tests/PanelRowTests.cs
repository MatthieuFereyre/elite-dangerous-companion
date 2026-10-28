using EDC.Companion;
using EDC.ViewModels;
using Xunit;

namespace EDC.Tests;

public sealed class PanelRowTests
{
    [Fact]
    public void The_exploration_tab_shows_the_lines_of_the_panel()
    {
        var rows = PanelRow.From([new PanelLine("ICI (1)", Tone.Info, Header: true), new PanelLine("A 1 · terraformable · 1,5 M", Tone.Good, Loud: true)]);

        Assert.Equal([("ICI (1)", true, false), ("A 1 · terraformable · 1,5 M", false, true)], rows.Select(r => (r.Text, r.Header, r.Loud)));
    }

    [Fact]
    public void Nothing_to_report_is_said()
    {
        Assert.Equal("Rien à signaler ici ni sur la route", Assert.Single(PanelRow.From([])).Text);
    }
}
