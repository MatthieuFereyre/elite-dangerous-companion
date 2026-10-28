using System.IO;
using System.Windows.Threading;
using EDC.Core;
using NAudio.Wave;

namespace EDC.Audio;

/// <summary>
/// Two falling notes over the music, on their own output: heard in game, where a Windows notification
/// is held back. Played whether the music is on or not.
/// </summary>
public static class Chime
{
    private const int SampleRate = 44100;
    private const float Amplitude = 0.3f;

    /// <summary>Plays the two notes <paramref name="times"/> times: once for low, twice for nearly empty.</summary>
    public static void Play(int times)
    {
        try
        {
            var output = new WaveOut();
            output.Init(new RawSourceWaveStream(new MemoryStream(Samples(times)), WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1)));
            // The event may come from the playback thread, which must not dispose its own output.
            var dispatcher = Dispatcher.CurrentDispatcher;
            output.PlaybackStopped += (_, _) => dispatcher.BeginInvoke(output.Dispose);
            output.Play();
        }
        catch (Exception ex)
        {
            AppLog.Write($"alerte sonore impossible : {ex.Message}");
        }
    }

    internal static byte[] Samples(int times)
    {
        var samples = new List<float>();
        for (var i = 0; i < times; i++)
        {
            Note(samples, 880, 0.18);
            Silence(samples, 0.05);
            Note(samples, 659.25, 0.28);
            Silence(samples, 0.3);
        }

        var bytes = new byte[samples.Count * sizeof(float)];
        Buffer.BlockCopy(samples.ToArray(), 0, bytes, 0, bytes.Length);
        return bytes;
    }

    // A soft 10 ms attack, then a decay: a bell rather than a beep.
    private static void Note(List<float> samples, double frequency, double seconds)
    {
        var count = (int)(SampleRate * seconds);
        var attack = SampleRate / 100;
        for (var n = 0; n < count; n++)
        {
            var envelope = n < attack ? (double)n / attack : Math.Exp(-4.0 * (n - attack) / count);
            samples.Add((float)(Amplitude * envelope * Math.Sin(2 * Math.PI * frequency * n / SampleRate)));
        }
    }

    private static void Silence(List<float> samples, double seconds) =>
        samples.AddRange(Enumerable.Repeat(0f, (int)(SampleRate * seconds)));
}
