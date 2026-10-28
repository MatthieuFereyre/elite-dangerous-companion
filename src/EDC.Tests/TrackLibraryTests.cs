using System.IO;
using EDC.Core;
using Xunit;

namespace EDC.Tests;

public sealed class TrackLibraryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("edc-library-tests-").FullName;
    private readonly string _path;
    private readonly byte[] _original;

    public TrackLibraryTests()
    {
        _path = Path.Combine(_folder, "tracks.json");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "legacy-tracks.json"), _path);
        _original = File.ReadAllBytes(_path);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Loads_every_track()
    {
        using var library = new TrackLibrary(_path);

        Assert.Equal(@"E:\Music\Space", Assert.Single(library.Folders).Path);
        Assert.Equal(6, library.Tracks.Count);
        Assert.Contains(library.Tracks, t => t.Like);
        Assert.Contains(library.Tracks, t => t.Mode == TrackMode.Off);
    }

    [Fact]
    public void A_change_undone_gives_back_the_same_bytes()
    {
        using var library = new TrackLibrary(_path);
        var track = library.Tracks.First(t => !t.Like && t.Mode == TrackMode.Calm);

        library.SetLike(track.Key, true);
        Assert.True(library.Find(track.Key)!.Like);
        Assert.NotEqual(_original, File.ReadAllBytes(_path));

        library.SetLike(track.Key, false);
        library.SetMode(track.Key, TrackMode.Combat);
        library.SetMode(track.Key, TrackMode.Calm);

        Assert.Equal(_original, File.ReadAllBytes(_path));
    }

    [Fact]
    public void A_like_is_written_after_the_mode_and_touches_nothing_else()
    {
        using var library = new TrackLibrary(_path);
        var track = library.Tracks.First(t => !t.Like && t.File.Contains('\''));

        library.SetLike(track.Key, true);

        var before = File.ReadAllLines(_path).ToList();
        var original = System.Text.Encoding.UTF8.GetString(_original).Split("\r\n").ToList();
        var at = before.FindIndex(l => l.Contains($"\"file\": \"{track.File}\""));
        Assert.Equal("      \"mode\": \"calm\",", before[at + 2]);
        Assert.Equal("      \"like\": true", before[at + 3]);
        before.RemoveAt(at + 3);
        before[at + 2] = before[at + 2].TrimEnd(',');
        Assert.Equal(original.Where(l => l.Length > 0), before.Where(l => l.Length > 0));
    }

    [Fact]
    public void A_hand_edit_made_meanwhile_is_kept()
    {
        using var library = new TrackLibrary(_path);
        var first = library.Tracks[0];
        var second = library.Tracks[1];

        // Someone excludes the first track by hand after the library was read…
        File.WriteAllText(_path, File.ReadAllText(_path).Replace(
            $"\"file\": \"{first.File}\",\r\n      \"section\": \"{first.Section}\",\r\n      \"mode\": \"calm\"",
            $"\"file\": \"{first.File}\",\r\n      \"section\": \"{first.Section}\",\r\n      \"mode\": \"off\""));

        // …then the player likes the second one.
        library.SetLike(second.Key, true);

        Assert.Equal(TrackMode.Off, library.Find(first.Key)!.Mode);
        Assert.True(library.Find(second.Key)!.Like);
    }

    // ------------------------------------------------------------------------------------- folders

    /// <summary>tracks.json as it was before the folders: one musicRoot, a liked track in it.</summary>
    private string OldFormat()
    {
        var space = Mp3Folder("Space Music", "a.mp3");
        File.WriteAllText(_path, $$"""
            {
              "musicRoot": "{{space.Replace(@"\", @"\\")}}",
              "likeWeight": 2,
              "tracks": [
                {
                  "file": "a.mp3",
                  "section": "Ambient",
                  "mode": "calm",
                  "like": true
                }
              ]
            }
            """);
        return space;
    }

    private string Mp3Folder(string name, params string[] files)
    {
        var folder = Path.Combine(_folder, name);
        foreach (var file in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, file))!);
            File.WriteAllText(Path.Combine(folder, file), "");
        }

        return folder;
    }

    [Fact]
    public void The_old_music_root_reads_as_the_only_folder()
    {
        var space = OldFormat();
        using var library = new TrackLibrary(_path);

        var folder = Assert.Single(library.Folders);
        Assert.Equal(("Space Music", space, true), (folder.Name, folder.Path, folder.Enabled));
        var track = Assert.Single(library.Tracks);
        Assert.Equal("Space Music", track.Folder);
        Assert.Equal(Path.Combine(space, "a.mp3"), library.FullPath(track));
        Assert.Single(library.Playable(TrackMode.Calm));
    }

    [Fact]
    public void An_added_folder_brings_its_mp3s_and_wavs_in_as_calm_and_keeps_the_tracks_already_sorted()
    {
        OldFormat();
        var deepSpace = Mp3Folder("deep-space-soundtrack", "02 b.MP3", @"bonus\01 c.mp3", "cover.jpg", "title.wav");
        using var library = new TrackLibrary(_path);

        var (folder, added) = library.AddFolder(deepSpace);

        Assert.Equal(("Deep Space Soundtrack", deepSpace, 3), (folder.Name, folder.Path, added));
        Assert.Equal(["Space Music", "Deep Space Soundtrack"], library.Folders.Select(f => f.Name));
        var a = library.Tracks.Single(t => t.File == "a.mp3");
        Assert.Equal(("Space Music", "Ambient", TrackMode.Calm, true), (a.Folder, a.Section, a.Mode, a.Like));
        Assert.Equal(["02 b.MP3", @"bonus\01 c.mp3", "title.wav"], library.Tracks.Where(t => t.Folder == folder.Name).Select(t => t.File));
        Assert.All(library.Tracks.Where(t => t.Folder == folder.Name), t => Assert.Equal((folder.Name, TrackMode.Calm), (t.Section, t.Mode)));
        Assert.Equal(4, library.Playable(TrackMode.Calm).Count());

        // Written to the file: the old entry untouched, so it still belongs to the first folder.
        var text = File.ReadAllText(_path);
        Assert.DoesNotContain("musicRoot", text);
        Assert.Single(new TrackLibrary(_path).Tracks, t => t.Folder == "Space Music" && t.Like);
        Assert.Equal(4, new TrackLibrary(_path).Tracks.Count);
    }

    [Fact]
    public void An_added_folder_takes_the_modes_it_is_given_calm_otherwise()
    {
        OldFormat();
        using var library = new TrackLibrary(_path);
        var path = Mp3Folder("hw", "x.mp3", "y.mp3");

        library.AddFolder(path, modes: new Dictionary<string, TrackMode> { ["x.mp3"] = TrackMode.Combat });

        Assert.Equal((TrackMode.Combat, TrackMode.Calm), (library.Tracks.Single(t => t.File == "x.mp3").Mode, library.Tracks.Single(t => t.File == "y.mp3").Mode));
        Assert.Equal(["x.mp3", "y.mp3"], TrackLibrary.MusicFiles(path));
    }

    [Fact]
    public void A_folder_can_be_given_its_name()
    {
        OldFormat();
        using var library = new TrackLibrary(_path);

        var (folder, _) = library.AddFolder(Mp3Folder("hw", "x.mp3"), "Nebula");

        Assert.Equal("Nebula", folder.Name);
        Assert.Equal("Nebula", library.Tracks.Single(t => t.File == "x.mp3").Section);
    }

    [Fact]
    public void The_same_file_name_in_two_folders_makes_two_tracks()
    {
        OldFormat();
        using var library = new TrackLibrary(_path);
        library.AddFolder(Mp3Folder("other", "a.mp3"));

        var other = library.Tracks.Single(t => t.Folder == "Other");
        library.SetLike(other.Key, true);
        library.SetMode(other.Key, TrackMode.Combat);

        Assert.Equal((true, TrackMode.Combat), (library.Find(other.Key)!.Like, library.Find(other.Key)!.Mode));
        var space = library.Tracks.Single(t => t.Folder == "Space Music");
        Assert.Equal((true, TrackMode.Calm), (space.Like, space.Mode));
    }

    [Fact]
    public void Adding_a_folder_again_brings_only_its_new_files()
    {
        OldFormat();
        var path = Mp3Folder("hw", "x.mp3");
        using var library = new TrackLibrary(_path);
        var (folder, _) = library.AddFolder(path);
        library.SetLike(library.Tracks.Single(t => t.File == "x.mp3").Key, true);
        Mp3Folder("hw", "y.mp3");

        var (again, added) = library.AddFolder(path + @"\");

        Assert.Equal((folder.Name, 1), (again.Name, added));
        Assert.Equal(2, library.Folders.Count);
        Assert.True(library.Tracks.Single(t => t.File == "x.mp3").Like);
        Assert.Contains(library.Tracks, t => t.File == "y.mp3");
    }

    [Fact]
    public void A_folder_switched_off_is_not_played_and_stays_so()
    {
        OldFormat();
        using var library = new TrackLibrary(_path);
        var (folder, _) = library.AddFolder(Mp3Folder("hw", "x.mp3"));

        library.SetFolderEnabled(folder.Name, false);

        Assert.Equal(["a.mp3"], library.Playable(TrackMode.Calm).Select(t => t.File));
        Assert.False(new TrackLibrary(_path).Folders[1].Enabled);

        library.SetFolderEnabled(folder.Name, true);
        Assert.Equal(2, library.Playable(TrackMode.Calm).Count());
        Assert.DoesNotContain("enabled", File.ReadAllText(_path));
    }

    [Fact]
    public void A_folder_removed_takes_its_tracks_with_it()
    {
        OldFormat();
        using var library = new TrackLibrary(_path);
        var (folder, _) = library.AddFolder(Mp3Folder("hw", "x.mp3"));

        library.RemoveFolder("Space Music");

        Assert.Equal([folder.Name], library.Folders.Select(f => f.Name));
        Assert.Equal(["x.mp3"], library.Tracks.Select(t => t.File));
        Assert.Equal(["x.mp3"], new TrackLibrary(_path).Tracks.Select(t => t.File));
    }
}
