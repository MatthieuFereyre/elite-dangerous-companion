using System.Windows.Threading;
using EDC.Core;
using NAudio.Wave;

namespace EDC.Audio;

/// <summary>
/// The player of one mode, with its own output and its own playlist: pausing it keeps its place, so the
/// calm track resumes where it stopped once the fight is over. Used from the UI thread only.
/// </summary>
public sealed class Deck : IDisposable
{
    private readonly TrackLibrary _library;
    private readonly Func<int> _likeWeight;
    private readonly Playlist _playlist = new();
    private readonly DeckSampleProvider _provider = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private WaveOut? _output;

    public Deck(TrackMode mode, TrackLibrary library, Func<int> likeWeight)
    {
        Mode = mode;
        _library = library;
        _likeWeight = likeWeight;
    }

    public TrackMode Mode { get; }

    /// <summary>The file loaded in this deck, played or paused; null before the first track.</summary>
    public string? CurrentFile { get; private set; }

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool Ended => _provider.Ended;
    public bool IsSilent => _provider.IsSilent;
    public float FadeTarget => _provider.FadeTarget;
    public TimeSpan Position => _provider.Position;
    public TimeSpan Duration => _provider.Duration;
    public IReadOnlyList<string> Upcoming => _playlist.Upcoming;
    public IReadOnlyList<string> Played => _playlist.Played;

    /// <summary>Raised when the output device fails (unplugged headset…); the deck has rebuilt it.</summary>
    public event Action<Deck, Exception>? OutputFailed;

    public void SetVolume(float volume, bool immediate = false) => _provider.SetVolume(volume, immediate);

    public void FadeTo(float level, double seconds) => _provider.FadeTo(level, TimeSpan.FromSeconds(seconds));

    public void Seek(TimeSpan position) => _provider.Seek(position);

    /// <summary>Loads the next track of the playlist; returns it, or null when the mode has no playable track.</summary>
    public Track? LoadNext()
    {
        // A file that will not decode is skipped; a bounded number of tries so a dead drive cannot spin forever.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var eligible = _library.Playable(Mode).ToList();
            var file = _playlist.Next(eligible, _likeWeight(), CurrentFile);
            if (file is null)
                break;

            var track = _library.Find(file)!;
            CurrentFile = file;
            try
            {
                _provider.Load(_library.FullPath(track));
                return track;
            }
            catch (Exception ex)
            {
                AppLog.Write($"illisible, passé : {track.Title} ({ex.Message})");
            }
        }

        CurrentFile = null;
        _provider.Unload();
        return null;
    }

    /// <summary>Loads <paramref name="file"/> out of its turn; null if it will not decode, the deck then ended.</summary>
    public Track? Load(string file)
    {
        if (_library.Find(file) is not { } track)
            return null;

        _playlist.Take(file);
        CurrentFile = file;
        try
        {
            _provider.Load(_library.FullPath(track));
            return track;
        }
        catch (Exception ex)
        {
            AppLog.Write($"illisible : {track.Title} ({ex.Message})");
            return null;
        }
    }

    public void Play()
    {
        EnsureOutput();
        if (_output!.PlaybackState != PlaybackState.Playing)
            _output.Play();
    }

    public void Pause()
    {
        if (_output?.PlaybackState == PlaybackState.Playing)
            _output.Pause();
    }

    private void EnsureOutput()
    {
        if (_output is not null)
            return;

        // 400 ms ahead: with two buffers, the playback thread had 100 ms to refill one, which the game,
        // hogging the processor, sometimes made it miss — a short gap in the music. Pause stays
        // immediate, and a seek or skip is heard 400 ms later at most.
        _output = new WaveOut { BufferMilliseconds = 100, NumberOfBuffers = 4 };
        _output.Init(_provider);
        _output.PlaybackStopped += OnPlaybackStopped;
    }

    // The provider never runs dry, so the output only stops on its own when the device fails. The
    // event may come from the playback thread: the deck is rebuilt on the UI thread.
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e) => _dispatcher.BeginInvoke(() =>
    {
        if (sender is not WaveOut output || output != _output)
            return;

        output.PlaybackStopped -= OnPlaybackStopped;
        output.Dispose();
        _output = null;

        if (e.Exception is not null)
            OutputFailed?.Invoke(this, e.Exception);
    });

    public void Dispose()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Dispose();
            _output = null;
        }

        _provider.Dispose();
    }
}
