using System.IO;
using EDC.Core;
using Xunit;

namespace EDC.Tests;

public sealed class CombatDetectionTests
{
    private static string Line(string time, string body) => $"{{ \"timestamp\":\"2026-10-04T{time}Z\", {body} }}";

    [Theory]
    [InlineData("\"event\":\"Music\", \"MusicTrack\":\"Combat_Dogfight\"", "Music Combat_Dogfight")]
    [InlineData("\"event\":\"UnderAttack\", \"Target\":\"You\"", "UnderAttack")]
    [InlineData("\"event\":\"ShieldState\", \"ShieldsUp\":false", "ShieldState")]
    [InlineData("\"event\":\"HullDamage\", \"Health\":0.8", "HullDamage")]
    [InlineData("\"event\":\"Interdicted\", \"Submitted\":false", "Interdicted")]
    [InlineData("\"event\":\"Bounty\", \"Reward\":1000", "Bounty")]
    public void Combat_signals(string body, string why)
    {
        var signal = CombatSignals.Parse(Line("20:00:00", body));
        Assert.NotNull(signal);
        Assert.Equal(why, signal.Why);
        Assert.Equal(new DateTime(2026, 10, 4, 20, 0, 0, DateTimeKind.Utc), signal.Time);
    }

    [Theory]
    [InlineData("\"event\":\"Music\", \"MusicTrack\":\"Exploration\"")]
    [InlineData("\"event\":\"Music\", \"MusicTrack\":\"NoTrack\"")]
    [InlineData("\"event\":\"FSDJump\", \"StarSystem\":\"Sol\"")]
    [InlineData("\"event\":\"HeatWarning\"")]
    public void Not_combat(string body)
    {
        Assert.Null(CombatSignals.Parse(Line("20:00:00", body)));
    }

    [Fact]
    public void Calm_comes_back_120_s_after_the_last_signal()
    {
        string[] journal =
        [
            Line("20:00:00", "\"event\":\"Fileheader\""),
            Line("20:01:00", "\"event\":\"UnderAttack\""),
            Line("20:02:30", "\"event\":\"HullDamage\""),
            Line("20:04:20", "\"event\":\"ReceiveText\""),
            Line("20:04:31", "\"event\":\"ReceiveText\""),
            Line("20:10:00", "\"event\":\"Music\", \"MusicTrack\":\"Combat_Dogfight\""),
        ];

        var switches = JournalReplay.Run(journal, TimeSpan.FromSeconds(120))
            .Skip(1)
            .Select(s => s[10..].Trim())
            .ToList();

        Assert.Equal(["-> combat  (UnderAttack)", "-> calm    (120 s sans signal)", "-> combat  (Music Combat_Dogfight)"], switches);
    }

    [Fact]
    public void The_tail_reads_only_new_whole_lines_and_a_new_journal_from_its_start()
    {
        var folder = Directory.CreateTempSubdirectory("edc-journal-").FullName;
        try
        {
            var first = Path.Combine(folder, "Journal.2026-10-04T200000.01.log");
            File.WriteAllText(first, "old line\n");
            var tail = new JournalTail(folder);
            tail.Start();

            File.AppendAllText(first, "one\ntwo, half");
            Assert.Equal(["one"], tail.ReadNewLines());

            File.AppendAllText(first, " done\n");
            Assert.Equal(["two, half done"], tail.ReadNewLines());

            var second = Path.Combine(folder, "Journal.2026-10-04T210000.01.log");
            File.WriteAllText(second, "fresh\n");
            File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(["fresh"], tail.ReadNewLines());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
