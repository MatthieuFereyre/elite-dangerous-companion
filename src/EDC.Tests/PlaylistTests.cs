using EDC.Core;
using Xunit;

namespace EDC.Tests;

public sealed class PlaylistTests
{
    private static List<Track> Tracks(int count, params int[] liked) =>
        Enumerable.Range(0, count)
            .Select(i => new Track($"{i:000}.mp3", "s", TrackMode.Calm, liked.Contains(i)))
            .ToList();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(42)]
    public void A_liked_track_comes_twice_a_round_never_twice_in_a_row(int seed)
    {
        var tracks = Tracks(10, 0, 1, 2, 3, 4, 5);
        var playlist = new Playlist(new Random(seed));

        string? previous = null;
        var drawn = new List<string>();
        for (var i = 0; i < 16 * 20; i++)
        {
            var file = playlist.Next(tracks, likeWeight: 2, previous)!;
            Assert.NotEqual(previous, file);
            drawn.Add(file);
            previous = file;
        }

        // Each round is 6 liked × 2 + 4 others = 16 draws.
        foreach (var round in drawn.Chunk(16))
        {
            Assert.All(Enumerable.Range(0, 6), i => Assert.Equal(2, round.Count(f => f == $"{i:000}.mp3")));
            Assert.All(Enumerable.Range(6, 4), i => Assert.Equal(1, round.Count(f => f == $"{i:000}.mp3")));
        }
    }

    [Fact]
    public void A_track_leaving_the_mode_is_not_drawn_again_and_one_joining_is_drawn_this_round()
    {
        var tracks = Tracks(20);
        var playlist = new Playlist(new Random(7));
        var first = playlist.Next(tracks, 2, null)!;

        var remaining = tracks.Where(t => t.File != "005.mp3").ToList();
        var newcomer = new Track("new.mp3", "s", TrackMode.Calm, false);
        remaining.Add(newcomer);

        var rest = Enumerable.Range(0, 19).Select(_ => playlist.Next(remaining, 2, null)!).ToList();

        Assert.DoesNotContain("005.mp3", rest.Where(f => f != first));
        Assert.Contains("new.mp3", rest);
    }

    [Fact]
    public void Upcoming_is_the_drawing_order_and_played_what_was_drawn()
    {
        var tracks = Tracks(5);
        var playlist = new Playlist(new Random(3));
        var first = playlist.Next(tracks, 2, null)!;
        var announced = playlist.Upcoming.ToList();

        string? previous = first;
        foreach (var expected in announced)
        {
            previous = playlist.Next(tracks, 2, previous);
            Assert.Equal(expected, previous);
        }

        Assert.Empty(playlist.Upcoming);
        Assert.Equal([first, .. announced], playlist.Played);
    }

    [Fact]
    public void A_track_played_out_of_turn_leaves_the_round_and_the_last_one_survives_a_new_round()
    {
        var tracks = Tracks(3);
        var playlist = new Playlist(new Random(5));
        var first = playlist.Next(tracks, 2, null)!;
        var chosen = playlist.Upcoming[^1];

        playlist.Take(chosen);

        Assert.DoesNotContain(chosen, playlist.Upcoming);
        Assert.Equal([first, chosen], playlist.Played);

        var last = playlist.Next(tracks, 2, chosen)!;
        var nextRound = playlist.Next(tracks, 2, last)!;
        Assert.Equal([last, nextRound], playlist.Played);
    }

    [Fact]
    public void Nothing_to_play_gives_null()
    {
        Assert.Null(new Playlist().Next([], 2, null));
    }

    [Fact]
    public void A_single_track_plays_again()
    {
        var one = Tracks(1);
        var playlist = new Playlist();
        Assert.Equal("000.mp3", playlist.Next(one, 2, null));
        Assert.Equal("000.mp3", playlist.Next(one, 2, "000.mp3"));
    }
}
