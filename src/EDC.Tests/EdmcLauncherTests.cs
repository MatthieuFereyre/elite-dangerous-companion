using EDC.Core;
using EDC.Services;
using Xunit;

namespace EDC.Tests;

public sealed class EdmcLauncherTests
{
    private const string Installed = @"C:\Program Files (x86)\EDMarketConnector\EDMarketConnector.exe";

    [Fact]
    public void Edmc_is_found_where_it_was_installed()
    {
        var found = EdmcLauncher.Pick([@"D:\missing\EDMarketConnector.exe", Installed], path => path == Installed);

        Assert.Equal(Installed, found);
        Assert.Null(EdmcLauncher.Pick([@"D:\missing\EDMarketConnector.exe"], _ => false));
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public void It_is_launched_with_the_game_only_when_asked_installed_and_not_already_running(bool asked, bool installed, bool running, bool expected)
    {
        Assert.Equal(expected, EdmcLauncher.ShouldLaunch(asked, installed ? Installed : null, running));
    }

    [Fact]
    public void Launching_edmc_is_off_until_ticked()
    {
        Assert.False(new PlayerSettings().LaunchEdmc);
    }
}
