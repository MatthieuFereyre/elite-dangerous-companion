using System.IO;
using EDC.Core;
using NAudio.Wave;
using Xunit;

namespace EDC.Tests;

public sealed class TrackDurationsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("edc-durations-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task A_duration_is_read_in_the_background_then_kept()
    {
        var wav = Path.Combine(_folder, "three.wav");
        using (var writer = new WaveFileWriter(wav, new WaveFormat(44100, 16, 2)))
            writer.Write(new byte[44100 * 4 * 3], 0, 44100 * 4 * 3);
        var missing = Path.Combine(_folder, "missing.mp3");
        var durations = new TrackDurations();
        var changed = new TaskCompletionSource();
        durations.Changed += () => changed.TrySetResult();

        Assert.Null(durations.Get(wav));
        durations.Request([wav, missing]);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, durations.Get(wav)!.Value.TotalSeconds, 1);
        Assert.Null(durations.Get(missing));
    }
}
