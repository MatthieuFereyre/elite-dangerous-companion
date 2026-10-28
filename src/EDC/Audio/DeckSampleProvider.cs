using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace EDC.Audio;

/// <summary>
/// One deck's sound: the current file, converted to a fixed format, with two gains ramped sample by
/// sample — the fade level (crossfades, skips) and the volume. It never runs dry: past the end of the
/// track it plays silence and raises <see cref="Ended"/>, so the output device never stops by itself.
/// </summary>
public sealed class DeckSampleProvider : ISampleProvider, IDisposable
{
    public const int SampleRate = 44100;

    private readonly Lock _gate = new();
    private MediaFoundationReader? _reader;
    private ISampleProvider? _source;

    private float _fade, _fadeTarget, _fadeStep;
    private float _volume, _volumeTarget, _volumeStep;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

    public bool Ended { get; private set; }

    public TimeSpan Position
    {
        get { lock (_gate) return _reader?.CurrentTime ?? TimeSpan.Zero; }
    }

    public TimeSpan Duration
    {
        get { lock (_gate) return _reader?.TotalTime ?? TimeSpan.Zero; }
    }

    /// <summary>The fade level the deck is heading to, 0 to 1.</summary>
    public float FadeTarget
    {
        get { lock (_gate) return _fadeTarget; }
    }

    public bool IsSilent
    {
        get { lock (_gate) return _fade == 0 && _fadeTarget == 0; }
    }

    /// <summary>Opens a file; throws if it cannot be decoded, and the previous track is then gone.</summary>
    public void Load(string path)
    {
        MediaFoundationReader? reader = null;
        try
        {
            reader = new MediaFoundationReader(path);
            var source = reader.ToSampleProvider();
            if (source.WaveFormat.Channels == 1)
                source = new MonoToStereoSampleProvider(source);
            else if (source.WaveFormat.Channels != 2)
                throw new NotSupportedException($"{source.WaveFormat.Channels} canaux");
            if (source.WaveFormat.SampleRate != SampleRate)
                source = new WdlResamplingSampleProvider(source, SampleRate);

            lock (_gate)
            {
                _reader?.Dispose();
                _reader = reader;
                _source = source;
                Ended = false;
            }
        }
        catch
        {
            reader?.Dispose();
            Unload();
            throw;
        }
    }

    public void Unload()
    {
        lock (_gate)
        {
            _reader?.Dispose();
            _reader = null;
            _source = null;
            Ended = true;
        }
    }

    /// <summary>Jumps to <paramref name="position"/>, clamped to the track; nothing if no track is loaded.</summary>
    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_reader is null || Ended)
                return;

            _reader.CurrentTime = position < TimeSpan.Zero ? TimeSpan.Zero
                : position > _reader.TotalTime ? _reader.TotalTime
                : position;
        }
    }

    /// <summary>Heads the fade level to <paramref name="level"/> over <paramref name="duration"/>.</summary>
    public void FadeTo(float level, TimeSpan duration)
    {
        lock (_gate)
        {
            _fadeTarget = level;
            _fadeStep = Step(_fade, level, duration);
        }
    }

    /// <summary>Volume 0 to 1, reached in 50 ms so moving the slider does not click.</summary>
    public void SetVolume(float volume, bool immediate = false)
    {
        lock (_gate)
        {
            _volumeTarget = volume;
            _volumeStep = Step(_volume, volume, TimeSpan.FromMilliseconds(50));
            if (immediate)
                _volume = volume;
        }
    }

    private static float Step(float from, float to, TimeSpan duration)
    {
        var frames = Math.Max(1, duration.TotalSeconds * SampleRate);
        return (float)Math.Max(Math.Abs(to - from) / frames, 1e-7);
    }

    public int Read(Span<float> buffer)
    {
        lock (_gate)
        {
            var read = 0;
            if (_source is not null && !Ended)
            {
                try
                {
                    read = _source.Read(buffer);
                }
                catch (Exception)
                {
                    // A file that breaks mid-way ends there; the controller moves on.
                    read = 0;
                }

                if (read == 0)
                    Ended = true;
            }

            buffer[read..].Clear();

            for (var i = 0; i + 1 < buffer.Length; i += 2)
            {
                _fade = Approach(_fade, _fadeTarget, _fadeStep);
                _volume = Approach(_volume, _volumeTarget, _volumeStep);

                // Equal-power curve: a crossfade keeps the loudness steady instead of dipping midway.
                var gain = MathF.Sin(_fade * MathF.PI / 2) * _volume;
                buffer[i] *= gain;
                buffer[i + 1] *= gain;
            }

            return buffer.Length;
        }
    }

    private static float Approach(float value, float target, float step) =>
        value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);

    public void Dispose() => Unload();
}
