using EDC.Services;
using Xunit;

namespace EDC.Tests;

public sealed class UpdaterTests
{
    private const string Latest = """
        { "tag_name": "v1.2.3", "name": "1.2.3", "assets": [
          { "name": "notes.txt", "browser_download_url": "https://github.com/x/y/releases/download/v1.2.3/notes.txt" },
          { "name": "EliteDangerousCompanion-Setup-1.2.3.exe", "browser_download_url": "https://github.com/x/y/releases/download/v1.2.3/EliteDangerousCompanion-Setup-1.2.3.exe" } ] }
        """;

    [Fact]
    public void The_latest_release_gives_its_version_and_its_installer()
    {
        var release = Updater.ParseRelease(Latest);

        Assert.Equal(new Version(1, 2, 3), release?.Version);
        Assert.Equal("https://github.com/x/y/releases/download/v1.2.3/EliteDangerousCompanion-Setup-1.2.3.exe", release?.Installer);
    }

    [Fact]
    public void A_release_without_an_installer_or_a_version_is_none()
    {
        Assert.Null(Updater.ParseRelease("""{ "tag_name": "v1.2.3", "assets": [] }"""));
        Assert.Null(Updater.ParseRelease("""{ "tag_name": "nightly", "assets": [ { "name": "a.exe", "browser_download_url": "https://x/a.exe" } ] }"""));
    }

    [Theory]
    [InlineData("1.0.92", "1.1.0", true)]
    [InlineData("0.0.96", "1.1.0", true)]
    [InlineData("1.1.0", "1.1.0", false)]
    [InlineData("1.2.0", "1.1.5", false)]
    public void Only_a_newer_release_is_installed(string current, string latest, bool expected)
    {
        Assert.Equal(expected, Updater.IsNewer(Version.Parse(current), Version.Parse(latest)));
    }

    [Theory]
    [InlineData("0.0.0", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("1.1.0", false)]
    public void A_build_from_the_sources_never_updates_itself(string version, bool local)
    {
        Assert.Equal(local, Updater.IsLocalBuild(Version.Parse(version)));
    }
}
