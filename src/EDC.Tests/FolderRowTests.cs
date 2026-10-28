using System.IO;
using EDC.Core;
using EDC.ViewModels;
using Xunit;

namespace EDC.Tests;

public sealed class FolderRowTests
{
    [Fact]
    public void A_folder_says_how_its_tracks_are_sorted()
    {
        var folder = new MusicFolder("Nebula", AppContext.BaseDirectory);
        Track[] tracks =
        [
            new("a.mp3", "", TrackMode.Calm, true, "Nebula"),
            new("b.mp3", "", TrackMode.Calm, false, "Nebula"),
            new("c.mp3", "", TrackMode.Combat, false, "Nebula"),
            new("d.mp3", "", TrackMode.Off, false, "Nebula"),
            new("e.mp3", "", TrackMode.Calm, false, "Space Music"),
        ];

        var row = FolderRow.From(folder, tracks);

        Assert.Equal(("Nebula", true), (row.Name, row.Enabled));
        Assert.Equal("4 morceaux · 2 calmes · 1 de combat · 1 écarté · 1 aimé", row.Summary);
    }

    [Fact]
    public void A_folder_gone_from_the_disk_says_so()
    {
        var folder = new MusicFolder("Nebula", Path.Combine(AppContext.BaseDirectory, "absent"), Enabled: false);

        var row = FolderRow.From(folder, [new("a.mp3", "", TrackMode.Calm, false, "Nebula")]);

        Assert.False(row.Enabled);
        Assert.Equal("Dossier introuvable · 1 morceau", row.Summary);
    }
}
