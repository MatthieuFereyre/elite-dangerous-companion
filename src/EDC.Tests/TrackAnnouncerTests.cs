using EDC.Core;
using Xunit;

namespace EDC.Tests;

public sealed class TrackAnnouncerTests
{
    [Fact]
    public void Each_new_track_is_announced_once()
    {
        var announcer = new TrackAnnouncer();
        Assert.True(announcer.Check("a.mp3", playing: true, inGame: true));
        Assert.False(announcer.Check("a.mp3", playing: true, inGame: true));
        Assert.True(announcer.Check("b.mp3", playing: true, inGame: true));
    }

    [Fact]
    public void Nothing_out_of_game_or_paused()
    {
        var announcer = new TrackAnnouncer();
        Assert.False(announcer.Check("a.mp3", playing: true, inGame: false));
        Assert.False(announcer.Check("a.mp3", playing: false, inGame: true));
        Assert.False(announcer.Check(null, playing: true, inGame: true));
        Assert.True(announcer.Check("a.mp3", playing: true, inGame: true));
    }

    [Fact]
    public void A_pause_does_not_announce_the_same_track_again()
    {
        var announcer = new TrackAnnouncer();
        Assert.True(announcer.Check("a.mp3", playing: true, inGame: true));
        Assert.False(announcer.Check("a.mp3", playing: false, inGame: true));
        Assert.False(announcer.Check("a.mp3", playing: true, inGame: true));
    }

    [Fact]
    public void A_new_game_session_announces_even_the_same_track()
    {
        var announcer = new TrackAnnouncer();
        Assert.True(announcer.Check("a.mp3", playing: true, inGame: true));
        announcer.Reset();
        Assert.True(announcer.Check("a.mp3", playing: true, inGame: true));
    }
}
