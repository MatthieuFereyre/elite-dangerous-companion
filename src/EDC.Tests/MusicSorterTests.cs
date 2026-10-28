using System.IO;
using EDC.Core;
using NAudio.Wave;
using Xunit;

namespace EDC.Tests;

public sealed class MusicSorterTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("edc-sorter-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Measures_a_sound_as_the_model_was_trained()
    {
        // The same signal and the same figures as the numpy script the weights were fitted with.
        const int rate = MusicSorter.Rate;
        var samples = Enumerable.Range(0, 30 * rate).Select(i =>
        {
            var t = (double)i / rate;
            return (float)(0.3 * Math.Sin(2 * Math.PI * 440 * t) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 0.5 * t))
                           + 0.05 * Math.Sin(2 * Math.PI * 3000 * t) + 0.02 * Math.Sin(2 * Math.PI * 80 * t));
        }).ToArray();

        var sound = MusicSorter.Measure(samples)!;

        Assert.Equal(-19.1967, sound.DbMean, 3);
        Assert.Equal(-13.5621, sound.DbP90, 3);
        Assert.Equal(5.1127, sound.DbStd, 3);
        Assert.Equal(1092.46, sound.Centroid, 1);
        Assert.Equal(0.27002, sound.High, 4);
        Assert.Equal(0.10803, sound.Low, 4);
    }

    private static TrackSound Quiet(double offset = 0) => new(-30 + offset, -25 + offset, 4, 800 + offset * 10, 0.1, 0.2);
    private static readonly TrackSound Loud = new(-12, -8, 7, 2200, 0.4, 0.3);

    [Fact]
    public void The_loud_and_bright_tracks_of_a_folder_go_to_combat_the_others_stay_calm()
    {
        var modes = MusicSorter.Sort(new Dictionary<string, TrackSound?>
        {
            ["01 Opening.mp3"] = Quiet(),
            ["02 Drift.mp3"] = Quiet(1),
            ["03 Nebula.mp3"] = Quiet(2),
            ["04 Outpost.mp3"] = Quiet(-1),
            ["05 Clash.mp3"] = Loud,
            ["06 Credits.mp3"] = Loud,
            ["07 Battle Theme.mp3"] = Quiet(-2),
            ["08 Broken.mp3"] = null,
            ["09-raider_jazz.mp3"] = Loud,
            ["10-station_under_siege.mp3"] = new(-20, -16, 5.5, 1500, 0.25, 0.25),
        });

        Assert.Equal(TrackMode.Combat, modes["05 Clash.mp3"]);
        Assert.Equal(TrackMode.Calm, modes["06 Credits.mp3"]);
        Assert.Equal(TrackMode.Combat, modes["07 Battle Theme.mp3"]);
        Assert.Equal(TrackMode.Calm, modes["08 Broken.mp3"]);
        Assert.Equal(TrackMode.Calm, modes["09-raider_jazz.mp3"]);
        Assert.Equal(TrackMode.Combat, modes["10-station_under_siege.mp3"]);
        Assert.All(new[] { "01 Opening.mp3", "02 Drift.mp3", "03 Nebula.mp3", "04 Outpost.mp3" }, f => Assert.Equal(TrackMode.Calm, modes[f]));
    }

    [Fact]
    public void Too_few_tracks_to_compare_are_sorted_by_their_titles_only()
    {
        var modes = MusicSorter.Sort(new Dictionary<string, TrackSound?>
        {
            ["Loud.mp3"] = Loud,
            ["Quiet.mp3"] = Quiet(),
            [@"cd2\Final Battle.mp3"] = Quiet(),
        });

        Assert.Equal([TrackMode.Calm, TrackMode.Calm, TrackMode.Combat], modes.Values);
    }

    [Fact]
    public void Reads_a_file_and_skips_one_too_short()
    {
        WriteWav("long.wav", seconds: 25, amplitude: 0.3f);
        WriteWav("short.wav", seconds: 10, amplitude: 0.3f);

        var sound = MusicSorter.Measure(Path.Combine(_folder, "long.wav"));

        Assert.NotNull(sound);
        Assert.InRange(sound.DbMean, -16, -12);   // a 0.3 sine: about -13.5 dB
        Assert.InRange(sound.Centroid, 400, 700);
        Assert.Null(MusicSorter.Measure(Path.Combine(_folder, "short.wav")));
    }

    [Fact]
    public async Task Sorts_every_file_of_a_folder_and_reports_its_progress()
    {
        for (var i = 0; i < 5; i++)
            WriteWav($"calm{i}.wav", seconds: 22, amplitude: 0.05f + i * 0.005f);
        WriteWav("loud.wav", seconds: 22, amplitude: 0.6f, noise: true);
        var progress = new List<int>();

        var modes = await MusicSorter.SortFolderAsync(_folder, TrackLibrary.MusicFiles(_folder), new Progress<int>(progress.Add));

        Assert.Equal(TrackMode.Combat, modes["loud.wav"]);
        Assert.Equal(5, modes.Count(m => m.Value == TrackMode.Calm));
        await Task.Delay(100);
        Assert.Contains(6, progress);
    }

    private void WriteWav(string name, int seconds, float amplitude, bool noise = false)
    {
        var random = new Random(1);
        using var writer = new WaveFileWriter(Path.Combine(_folder, name), WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
        var buffer = new float[44100 * 2];
        for (var s = 0; s < seconds; s++)
        {
            for (var i = 0; i < 44100; i++)
            {
                var v = noise ? (float)(random.NextDouble() * 2 - 1) * amplitude : amplitude * (float)Math.Sin(2 * Math.PI * 500 * i / 44100.0);
                buffer[2 * i] = buffer[2 * i + 1] = v;
            }

            writer.WriteSamples(buffer, 0, buffer.Length);
        }
    }
}
