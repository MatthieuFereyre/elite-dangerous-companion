using System.IO;
using System.Text.RegularExpressions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace EDC.Core;

/// <summary>How a track sounds, over its loud frames: what tells a fight from the calm.</summary>
public sealed record TrackSound(double DbMean, double DbP90, double DbStd, double Centroid, double High, double Low);

/// <summary>
/// Files the tracks of a new folder as calm or combat, by ear: louder, more dynamic and brighter
/// than the rest of its folder means combat. The weights come from a logistic regression on the
/// 200 space-game tracks sorted by hand (84 % right in cross-validation); each folder is compared with
/// itself, since soundtracks are not mastered at the same level. The title has the last word:
/// "battle" is combat whatever it sounds like, "credits" is calm.
/// </summary>
public static partial class MusicSorter
{
    public const int Rate = 11025;
    private const int Frame = 1024;

    private static readonly double[] Weights = [1.0435, 0.3964, 0.4723, 0.6216, 0.7143, 0.2347];
    private const double Bias = -1.2119;

    [GeneratedRegex(@"\b(battle|combat|fight|attack|attacks|assault|siege|invasion|war|engage|defend|hunted|bombers|missile|strike|raid|raider)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CombatTitle();

    [GeneratedRegex(@"\b(credits|personal log|message from|address|prologue|epilogue|pray|sacrifice|jazz|front end|menu|tutorial|ambient|ambience|lullaby)\b|^title$", RegexOptions.IgnoreCase)]
    private static partial Regex CalmTitle();

    /// <summary>Measures 4 minutes from 15 s in, as the model was trained; null when the file is shorter than 20 s or unreadable.</summary>
    public static TrackSound? Measure(string path)
    {
        try
        {
            using var reader = new MediaFoundationReader(path);
            if (reader.TotalTime < TimeSpan.FromSeconds(20))
                return null;

            reader.CurrentTime = TimeSpan.FromSeconds(15);
            var mono = new WdlResamplingSampleProvider(new Downmix(reader.ToSampleProvider()), Rate);
            var samples = new float[240 * Rate];
            int read, total = 0;
            while (total < samples.Length && (read = mono.Read(samples.AsSpan(total, Math.Min(Rate, samples.Length - total)))) > 0)
                total += read;

            return Measure(samples.AsSpan(0, total).ToArray());
        }
        catch (Exception ex)
        {
            AppLog.Write($"écoute impossible : {Path.GetFileName(path)} ({ex.Message})");
            return null;
        }
    }

    /// <summary>The measures of mono samples at <see cref="Rate"/>; null under 5 s.</summary>
    internal static TrackSound? Measure(float[] samples)
    {
        if (samples.Length < 5 * Rate)
            return null;

        var frames = samples.Length / Frame;
        var db = new double[frames];
        var centroid = new double[frames];
        var high = new double[frames];
        var low = new double[frames];
        var window = Enumerable.Range(0, Frame).Select(n => 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (Frame - 1))).ToArray();
        var re = new double[Frame];
        var im = new double[Frame];

        for (var f = 0; f < frames; f++)
        {
            double squares = 0;
            for (var n = 0; n < Frame; n++)
            {
                double v = samples[f * Frame + n];
                squares += v * v;
                re[n] = v * window[n];
                im[n] = 0;
            }

            db[f] = 20 * Math.Log10(Math.Sqrt(squares / Frame) + 1e-9);
            Fft(re, im);

            double sum = 0, weighted = 0, above = 0, below = 0;
            for (var k = 0; k <= Frame / 2; k++)
            {
                var magnitude = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                var hz = (double)k * Rate / Frame;
                sum += magnitude;
                weighted += magnitude * hz;
                if (hz > 2000)
                    above += magnitude;
                if (hz < 150)
                    below += magnitude;
            }

            sum += 1e-9;
            centroid[f] = weighted / sum;
            high[f] = above / sum;
            low[f] = below / sum;
        }

        var floor = Percentile(db, 10);
        var loud = Enumerable.Range(0, frames).Where(f => db[f] > floor).ToArray();
        var loudDb = loud.Select(f => db[f]).ToArray();
        var mean = loudDb.Average();
        return new TrackSound(
            mean,
            Percentile(db, 90),
            Math.Sqrt(loudDb.Average(v => (v - mean) * (v - mean))),
            loud.Average(f => centroid[f]),
            loud.Average(f => high[f]),
            loud.Average(f => low[f]));
    }

    /// <summary>Calm or combat for each file; below four measured tracks there is nothing to compare, and only titles count.</summary>
    public static IReadOnlyDictionary<string, TrackMode> Sort(IReadOnlyDictionary<string, TrackSound?> sounds)
    {
        var measured = sounds.Values.OfType<TrackSound>().Select(Vector).ToList();
        var compare = measured.Count >= 4;
        var means = new double[Weights.Length];
        var spreads = new double[Weights.Length];
        for (var k = 0; compare && k < Weights.Length; k++)
        {
            means[k] = measured.Average(v => v[k]);
            spreads[k] = Math.Sqrt(measured.Average(v => (v[k] - means[k]) * (v[k] - means[k])));
        }

        return sounds.ToDictionary(pair => pair.Key, pair =>
        {
            // "station_under_siege": the separators read as spaces, or \b would not see the words.
            var title = Path.GetFileNameWithoutExtension(pair.Key).Replace('_', ' ').Replace('-', ' ');
            if (CalmTitle().IsMatch(title))
                return TrackMode.Calm;
            if (title.Contains("battle", StringComparison.OrdinalIgnoreCase))
                return TrackMode.Combat;
            if (!compare || pair.Value is null)
                return TrackMode.Calm;

            var v = Vector(pair.Value);
            var logit = Bias + Enumerable.Range(0, Weights.Length).Sum(k => spreads[k] > 0 ? Weights[k] * (v[k] - means[k]) / spreads[k] : 0);
            var p = 1 / (1 + Math.Exp(-logit));
            return p >= 0.6 || (p >= 0.3 && CombatTitle().IsMatch(title)) ? TrackMode.Combat : TrackMode.Calm;
        }, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Listens to every file of the folder, four at a time, then sorts them; <paramref name="progress"/> counts the files done.</summary>
    public static async Task<IReadOnlyDictionary<string, TrackMode>> SortFolderAsync(string folder, IReadOnlyList<string> files, IProgress<int>? progress = null)
    {
        var sounds = new System.Collections.Concurrent.ConcurrentDictionary<string, TrackSound?>(StringComparer.OrdinalIgnoreCase);
        var done = 0;
        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 4 }, (file, _) =>
        {
            sounds[file] = Measure(Path.Combine(folder, file));
            progress?.Report(Interlocked.Increment(ref done));
            return ValueTask.CompletedTask;
        });

        return Sort(files.ToDictionary(f => f, f => sounds[f], StringComparer.OrdinalIgnoreCase));
    }

    private static double[] Vector(TrackSound s) => [s.DbMean, s.DbP90, s.DbStd, s.Centroid, s.High, s.Low];

    // numpy's default: linear between the two closest ranks.
    private static double Percentile(double[] values, double q)
    {
        var sorted = values.Order().ToArray();
        var at = q / 100 * (sorted.Length - 1);
        var below = (int)Math.Floor(at);
        var above = Math.Min(below + 1, sorted.Length - 1);
        return sorted[below] + (sorted[above] - sorted[below]) * (at - below);
    }

    // Radix-2, in place.
    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            for (var i = 0; i < n; i += length)
            {
                for (var k = 0; k < length / 2; k++)
                {
                    double wr = Math.Cos(angle * k), wi = Math.Sin(angle * k);
                    int a = i + k, b = i + k + length / 2;
                    double xr = re[b] * wr - im[b] * wi, xi = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - xr;
                    im[b] = im[a] - xi;
                    re[a] += xr;
                    im[a] += xi;
                }
            }
        }
    }

    /// <summary>Any number of channels to one, by their average (as ffmpeg's -ac 1 does).</summary>
    private sealed class Downmix(ISampleProvider source) : ISampleProvider
    {
        private readonly int _channels = source.WaveFormat.Channels;
        private float[] _buffer = [];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(Span<float> buffer)
        {
            var count = buffer.Length;
            if (_buffer.Length < count * _channels)
                _buffer = new float[count * _channels];

            var frames = source.Read(_buffer.AsSpan(0, count * _channels)) / _channels;
            for (var i = 0; i < frames; i++)
            {
                float sum = 0;
                for (var c = 0; c < _channels; c++)
                    sum += _buffer[i * _channels + c];
                buffer[i] = sum / _channels;
            }

            return frames;
        }
    }
}
