using System.IO;
using EDC.Companion;
using EDC.Core;
using EDC.ViewModels;
using Xunit;

namespace EDC.Tests;

public sealed class PowerSettingTests
{
    [Theory]
    [InlineData(null, "Yuri Grom", "Yuri Grom")]
    [InlineData("auto", "Yuri Grom", "Yuri Grom")]
    [InlineData("auto", null, null)]
    [InlineData("none", "Yuri Grom", null)]
    [InlineData("Aisling Duval", "Yuri Grom", "Aisling Duval")]
    public void The_hunt_looks_for_the_power_chosen_or_else_the_game_s(string? setting, string? game, string? expected)
    {
        Assert.Equal(expected, CompanionViewModel.HuntPower(setting, game));
    }

    [Fact]
    public void The_twelve_powers_are_named_as_spansh_names_them()
    {
        Assert.Equal(12, HuntFinder.Powers.Count);
        Assert.Contains("A. Lavigny-Duval", HuntFinder.Powers);
        Assert.Contains("Jerome Archer", HuntFinder.Powers);
    }

    [Fact]
    public void The_power_chosen_is_kept()
    {
        var file = Path.Combine(Path.GetTempPath(), $"edc-power-{Guid.NewGuid():N}.json");
        try
        {
            new PlayerSettings { Power = "Nakato Kaine" }.Save(file);
            Assert.Equal("Nakato Kaine", PlayerSettings.Load(file).Power);
            Assert.Equal("auto", new PlayerSettings().Power);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
