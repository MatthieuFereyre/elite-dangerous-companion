using EDC.ViewModels;
using Xunit;

namespace EDC.Tests;

public sealed class ShipLineTests
{
    [Theory]
    [InlineData("Nova", "Krait", "Krait Mk II", "CMDR Nova · Krait (Krait Mk II)")]
    [InlineData("Nova", "", "Mandalay", "CMDR Nova · Mandalay")]
    [InlineData("Nova", null, "Mandalay", "CMDR Nova · Mandalay")]
    [InlineData("Nova", null, null, "CMDR Nova")]
    [InlineData(null, null, null, "Aucune partie lue")]
    public void The_line_above_the_tabs_names_the_commander_and_the_ship(string? commander, string? name, string? type, string expected)
    {
        Assert.Equal(expected, CompanionViewModel.ShipLine(commander, name, type));
    }
}
