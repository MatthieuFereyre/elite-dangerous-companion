using System.IO;
using EDC.Audio;
using EDC.Core;
using Xunit;

namespace EDC.Tests;

public sealed class AppPathsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("edc-paths-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Everything_lives_in_the_user_profile_next_to_the_game_journals()
    {
        var paths = AppPaths.Resolve(null);

        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "Elite Dangerous Companion"), paths.Folder);
        Assert.Equal(Path.Combine(paths.Folder, "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine(paths.Folder, "library.json"), paths.LibraryFile);
        Assert.Equal(Path.Combine(paths.Folder, "edc.log"), paths.LogFile);
    }

    [Fact]
    public void A_first_start_without_anything_gets_an_empty_library()
    {
        var paths = AppPaths.Resolve(Path.Combine(_root, "data"));

        paths.Prepare();

        using var library = new TrackLibrary(paths.LibraryFile);
        Assert.Empty(library.Folders);
        Assert.Empty(library.Tracks);
    }

    [Fact]
    public void A_library_already_there_is_never_overwritten()
    {
        var paths = AppPaths.Resolve(Path.Combine(_root, "data"));
        paths.Prepare();
        File.WriteAllText(paths.LibraryFile, """{ "folders": [ { "name": "Mine", "path": "D:\\Music" } ], "tracks": [] }""");
        var mine = File.ReadAllText(paths.LibraryFile);

        paths.Prepare();

        Assert.Equal(mine, File.ReadAllText(paths.LibraryFile));
    }

    [Fact]
    public void Every_setting_of_the_window_is_kept()
    {
        var file = Path.Combine(_root, "settings.json");
        new PlayerSettings { Force = ForceMode.Combat, Language = "fr", LikeWeight = 4 }.Save(file);

        var read = PlayerSettings.Load(file);

        Assert.Equal((ForceMode.Combat, "fr", 4), (read.Force, read.Language, read.LikeWeight));
        Assert.Contains("\"force\": \"Combat\"", File.ReadAllText(file));
    }

    [Fact]
    public void The_banner_and_the_exploration_panel_are_set_apart()
    {
        var file = Path.Combine(_root, "settings.json");
        Assert.True(new PlayerSettings().ExplorationPanel);
        Assert.True(new PlayerSettings().ControllerInBanner);
        new PlayerSettings { CompanionBanner = true, ExplorationPanel = false, ControllerInBanner = false }.Save(file);

        var read = PlayerSettings.Load(file);

        Assert.Equal((true, false, false), (read.CompanionBanner, read.ExplorationPanel, read.ControllerInBanner));
    }
}
