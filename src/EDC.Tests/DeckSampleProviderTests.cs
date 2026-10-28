using System.IO;
using EDC.Audio;
using NAudio.Wave;
using Xunit;

namespace EDC.Tests;

/// <summary>Decoding and gains, read straight from the provider: no output device, no sound.</summary>
public sealed class DeckSampleProviderTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("edc-audio-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>One second of a 440 Hz sine, mono at 48 kHz: the provider has to convert both.</summary>
    private string Sine()
    {
        var path = Path.Combine(_folder, "sine.wav");
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1));
        var samples = Enumerable.Range(0, 48000).Select(i => 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / 48000)).ToArray();
        writer.WriteSamples(samples, 0, samples.Length);
        return path;
    }

    private static float Peak(float[] buffer) => buffer.Max(MathF.Abs);

    [Fact]
    public void Plays_the_file_converted_then_silence_and_ended()
    {
        using var provider = new DeckSampleProvider();
        provider.Load(Sine());
        provider.SetVolume(1, immediate: true);
        provider.FadeTo(1, TimeSpan.Zero);

        var total = 0;
        var buffer = new float[4410 * 2];
        var peaks = new List<float>();
        while (!provider.Ended && total < 10 * 44100 * 2)
        {
            Assert.Equal(buffer.Length, provider.Read(buffer));
            peaks.Add(Peak(buffer));
            total += buffer.Length;
        }

        Assert.True(provider.Ended);
        Assert.InRange(peaks[2], 0.45f, 0.55f);
        // Ten buffers of 0.1 s for one second of sound, plus the read that found the end.
        Assert.InRange(peaks.Count, 10, 12);

        provider.Read(buffer);
        Assert.Equal(0, Peak(buffer));
    }

    [Fact]
    public void Fades_and_volume_scale_the_sound()
    {
        using var provider = new DeckSampleProvider();
        provider.Load(Sine());
        provider.SetVolume(MusicController.VolumeGain(50), immediate: true);
        provider.FadeTo(1, TimeSpan.Zero);

        var buffer = new float[4410 * 2];
        provider.Read(buffer);
        provider.Read(buffer);
        Assert.InRange(Peak(buffer), 0.5f * 0.25f * 0.95f, 0.5f * 0.25f * 1.05f);

        provider.FadeTo(0, TimeSpan.FromSeconds(0.05));
        provider.Read(buffer);
        Assert.True(provider.IsSilent);
        Assert.Equal(0, Peak(buffer[^2000..]));
    }

    [Fact]
    public void Seeking_jumps_within_the_track_clamped_to_it()
    {
        using var provider = new DeckSampleProvider();
        var sine = Sine();
        provider.Load(sine);
        var buffer = new float[4410 * 2];

        provider.Seek(TimeSpan.FromSeconds(0.9));
        Assert.InRange(provider.Position.TotalSeconds, 0.85, 0.95);
        // About 0.1 s left: one read reaches the end, the next finds it.
        provider.Read(buffer);
        provider.Read(buffer);
        Assert.True(provider.Ended);

        provider.Load(sine);
        provider.Seek(TimeSpan.FromSeconds(-3));
        Assert.Equal(TimeSpan.Zero, provider.Position);
        provider.Seek(TimeSpan.FromSeconds(30));
        Assert.Equal(provider.Duration, provider.Position);
    }

    [Fact]
    public void A_file_that_will_not_decode_throws_and_leaves_the_deck_ended()
    {
        var path = Path.Combine(_folder, "broken.mp3");
        File.WriteAllText(path, "not an mp3");

        using var provider = new DeckSampleProvider();
        Assert.ThrowsAny<Exception>(() => provider.Load(path));
        Assert.True(provider.Ended);
    }
}
