using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using EDC.Core;
using EDC.Resources;

namespace EDC.Audio;

public enum ForceMode { Auto, Calm, Combat }

/// <summary>
/// Decides what plays: starts and stops with the game, follows its journal for combat, crossfades
/// between the calm and the combat deck, and carries out the buttons. Lives on the UI thread.
/// </summary>
public sealed class MusicController : IDisposable
{
    public const string GameProcess = "EliteDangerous64";

    private const double ResumeFadeSeconds = 0.4;
    private const double GameStartFadeSeconds = 2;

    private readonly TrackLibrary _library;
    private readonly PlayerSettings _settings;
    private readonly string _settingsPath;
    private readonly Func<bool> _isGameRunning;
    private readonly JournalTail _journal;
    private readonly CombatClock _clock;
    private readonly Dictionary<TrackMode, Deck> _decks;
    private readonly HashSet<TrackMode> _emptyModes = [];
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _saveSettings;
    private readonly DispatcherTimer _clearNotice;

    private int _ticksToProcessCheck;
    private DateTime _nextOutputRetry;
    private bool _skipping;
    private bool _sorting;

    public MusicController(TrackLibrary library, PlayerSettings settings, string settingsPath,
        JournalTail journal, Func<bool>? isGameRunning = null)
    {
        _library = library;
        _settings = settings;
        _settingsPath = settingsPath;
        _journal = journal;
        _isGameRunning = isGameRunning ?? IsGameProcessRunning;
        _clock = new CombatClock(TimeSpan.FromSeconds(settings.CalmDelaySeconds));
        Force = settings.Force;

        _decks = new()
        {
            [TrackMode.Calm] = new Deck(TrackMode.Calm, library, () => settings.LikeWeight),
            [TrackMode.Combat] = new Deck(TrackMode.Combat, library, () => settings.LikeWeight),
        };
        foreach (var deck in _decks.Values)
        {
            deck.SetVolume(VolumeGain(settings.Volume), immediate: true);
            deck.OutputFailed += (d, ex) => AppLog.Write($"sortie audio perdue ({d.Mode}) : {ex.Message}");
        }

        var dispatcher = Dispatcher.CurrentDispatcher;
        _library.Changed += () => dispatcher.BeginInvoke(() =>
        {
            _emptyModes.Clear();
            StateChanged?.Invoke();
        });

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();

        _saveSettings = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveSettings.Tick += (_, _) =>
        {
            _saveSettings.Stop();
            _settings.Save(_settingsPath);
        };

        _clearNotice = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _clearNotice.Tick += (_, _) =>
        {
            _clearNotice.Stop();
            Notice = null;
            StateChanged?.Invoke();
        };
    }

    /// <summary>Raised on the UI thread whenever something shown may have changed (several times a second).</summary>
    public event Action? StateChanged;

    /// <summary>Raised on the UI thread when Elite Dangerous starts.</summary>
    public event Action? GameStarted;

    public bool InGame { get; private set; }

    /// <summary>The music is on: started with the game or by Lecture, until the game closes or Pause.</summary>
    public bool IsPlaying { get; private set; }

    public ForceMode Force { get; private set; }
    public TrackMode ActiveMode { get; private set; } = TrackMode.Calm;
    public string SwitchReason { get; private set; } = Strings.SwitchStart;
    public DateTime SwitchTime { get; private set; } = DateTime.Now;
    public int Volume => _settings.Volume;
    public TimeSpan CalmDelay => _clock.CalmDelay;

    /// <summary>A short message for the window, such as a failed write to the library.</summary>
    public string? Notice { get; private set; }

    public TrackLibrary Library => _library;

    private Deck Active => _decks[ActiveMode];

    public Track? CurrentTrack => Active.CurrentFile is { } file ? _library.Find(file) : null;
    public TimeSpan Position => Active.Position;
    public TimeSpan Duration => Active.Duration;

    /// <summary>
    /// The active mode's round for the window: the tracks heard, oldest first, the one ending with the
    /// track before the current one; then those to come, in order. Tracks gone off or to the other mode
    /// are left out.
    /// </summary>
    public (IReadOnlyList<Track> Played, IReadOnlyList<Track> Upcoming) Round()
    {
        var deck = Active;
        var eligible = _library.Playable(ActiveMode).ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
        var upcoming = deck.Upcoming.Where(eligible.ContainsKey).Select(f => eligible[f]).ToList();

        // A liked track heard and still to come shows once, where it comes next.
        var shown = new HashSet<string>(deck.Upcoming, StringComparer.OrdinalIgnoreCase);
        if (deck.CurrentFile is { } current)
            shown.Add(current);
        var played = new List<Track>();
        foreach (var file in deck.Played.Reverse())
        {
            if (shown.Add(file) && eligible.TryGetValue(file, out var track))
                played.Add(track);
        }

        played.Reverse();
        return (played, upcoming);
    }

    public void Start()
    {
        AppLog.Write($"lecteur démarré, {_library.Playable(TrackMode.Calm).Count()} morceaux calmes, " +
                     $"{_library.Playable(TrackMode.Combat).Count()} de combat");
        Tick();
        _timer.Start();
    }

    // ------------------------------------------------------------------------------------- the loop

    private void Tick()
    {
        if (--_ticksToProcessCheck <= 0)
        {
            _ticksToProcessCheck = 8;
            var running = _isGameRunning();
            if (running != InGame)
                OnGameChanged(running);
        }

        if (InGame)
            ReadJournal();

        var (mode, reason) = Desired();
        if (mode != ActiveMode)
            SwitchTo(mode, reason);

        if (IsPlaying)
            KeepActiveDeckGoing();

        // A deck that has faded out is paused, which keeps its place in the track.
        foreach (var deck in _decks.Values)
        {
            if ((deck != Active || !IsPlaying) && deck.IsPlaying && deck.IsSilent)
                deck.Pause();
        }

        StateChanged?.Invoke();
    }

    private void ReadJournal()
    {
        try
        {
            foreach (var line in _journal.ReadNewLines())
            {
                // The time of reading, not the journal's: what matters is how long ago we learnt of it.
                if (CombatSignals.Parse(line) is { } signal)
                    _clock.Record(DateTime.UtcNow, signal.Why);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The game holds the journal open; a missed pass is caught up on the next one.
        }
    }

    private void KeepActiveDeckGoing()
    {
        var deck = Active;
        if (!_skipping && deck.Ended && !_emptyModes.Contains(deck.Mode))
            LoadNext(deck);

        if (!deck.IsPlaying && DateTime.UtcNow >= _nextOutputRetry)
        {
            _nextOutputRetry = DateTime.UtcNow.AddSeconds(5);
            TryPlay(deck);
        }
    }

    private void OnGameChanged(bool running)
    {
        InGame = running;
        _clock.Reset();

        if (running)
        {
            AppLog.Write("jeu lancé");
            _journal.Start();
            var (mode, _) = Desired();
            if (mode != ActiveMode)
            {
                Active.FadeTo(0, 0);
                ActiveMode = mode;
            }

            StartPlayback(GameStartFadeSeconds);
            GameStarted?.Invoke();
        }
        else
        {
            AppLog.Write("jeu fermé, lecture arrêtée");
            StopPlayback(_settings.FadeSeconds);
        }
    }

    private (TrackMode Mode, string Reason) Desired() => Force switch
    {
        ForceMode.Calm => (TrackMode.Calm, Strings.SwitchForcedCalm),
        ForceMode.Combat => (TrackMode.Combat, Strings.SwitchForcedCombat),
        _ when InGame && _clock.IsCombat(DateTime.UtcNow) => (TrackMode.Combat, _clock.LastWhy ?? "combat"),
        _ => (TrackMode.Calm, InGame ? string.Format(Strings.SwitchNoSignal, _settings.CalmDelaySeconds) : Strings.SwitchOutOfGame),
    };

    private void SwitchTo(TrackMode mode, string reason)
    {
        var from = Active;
        var to = _decks[mode];
        ActiveMode = mode;
        SwitchReason = reason;
        SwitchTime = DateTime.Now;
        AppLog.Write($"-> {TrackLibrary.ModeName(mode)} ({reason})");

        if (!IsPlaying)
            return;

        if (to.CurrentFile is null || to.Ended)
            LoadNext(to);

        TryPlay(to);
        to.FadeTo(1, _settings.FadeSeconds);
        from.FadeTo(0, _settings.FadeSeconds);
    }

    private void StartPlayback(double fadeSeconds)
    {
        IsPlaying = true;
        var deck = Active;
        if (deck.CurrentFile is null || deck.Ended)
            LoadNext(deck);

        TryPlay(deck);
        deck.FadeTo(1, fadeSeconds);
    }

    private void StopPlayback(double fadeSeconds)
    {
        IsPlaying = false;
        foreach (var deck in _decks.Values)
            deck.FadeTo(0, fadeSeconds);
    }

    private void TryPlay(Deck deck)
    {
        try
        {
            deck.Play();
        }
        catch (Exception ex)
        {
            AppLog.Write($"pas de sortie audio : {ex.Message}");
        }
    }

    private void LoadNext(Deck deck)
    {
        if (deck.LoadNext() is { } track)
        {
            AppLog.Write($"{TrackLibrary.ModeName(deck.Mode)} : {track.Title}");
        }
        else
        {
            _emptyModes.Add(deck.Mode);
            AppLog.Write($"aucun morceau {TrackLibrary.ModeName(deck.Mode)} jouable dans les dossiers joués");
        }
    }

    // ------------------------------------------------------------------------------------- the buttons

    public void TogglePlay()
    {
        if (IsPlaying)
        {
            AppLog.Write("pause");
            StopPlayback(ResumeFadeSeconds);
        }
        else
        {
            AppLog.Write("lecture");
            StartPlayback(ResumeFadeSeconds);
        }

        StateChanged?.Invoke();
    }

    public void Next() => SkipTo(LoadNext);

    /// <summary>Plays <paramref name="file"/>, of the active mode, right now; starts playback if paused.</summary>
    public void PlayNow(string file)
    {
        if (!IsPlaying)
        {
            if (Active.Load(file) is { } track)
                AppLog.Write($"{TrackLibrary.ModeName(ActiveMode)} : {track.Title} (choisi)");
            StartPlayback(ResumeFadeSeconds);
            StateChanged?.Invoke();
            return;
        }

        SkipTo(deck =>
        {
            if (deck.Load(file) is { } track)
                AppLog.Write($"{TrackLibrary.ModeName(deck.Mode)} : {track.Title} (choisi)");
        });
    }

    private async void SkipTo(Action<Deck> load)
    {
        if (_skipping)
            return;

        var deck = Active;
        if (!IsPlaying || !deck.IsPlaying)
        {
            load(deck);
            StateChanged?.Invoke();
            return;
        }

        // A short fade rather than a cut.
        _skipping = true;
        deck.FadeTo(0, 0.25);
        await Task.Delay(300);
        _skipping = false;

        load(deck);
        if (deck == Active && IsPlaying)
            deck.FadeTo(1, 0.15);

        StateChanged?.Invoke();
    }

    /// <summary>Jumps within the current track, <paramref name="fraction"/> 0 to 1: to hear more of it before sorting it.</summary>
    public void Seek(double fraction)
    {
        var deck = Active;
        if (_skipping || deck.CurrentFile is null)
            return;

        deck.Seek(deck.Duration * Math.Clamp(fraction, 0, 1));
        StateChanged?.Invoke();
    }

    public void ToggleLike()
    {
        if (CurrentTrack is { } track)
            ToggleLike(track.Key);
    }

    /// <summary>Any track of the library, such as the one before, heard in the playlist.</summary>
    public void ToggleLike(string file)
    {
        if (_library.Find(file) is not { } track)
            return;

        if (Change(() => _library.SetLike(track.Key, !track.Like)))
            AppLog.Write($"{(track.Like ? "n'est plus aimé" : "aimé")} : {track.Title}");

        StateChanged?.Invoke();
    }

    /// <summary>Never plays the track again (mode "off"), then moves on.</summary>
    public void Dislike()
    {
        if (CurrentTrack is not { } track)
            return;

        if (Change(() => _library.SetMode(track.Key, TrackMode.Off)))
        {
            AppLog.Write($"écarté : {track.Title}");
            Next();
        }
    }

    /// <summary>Files the track as calm or combat. It plays on; only the next picks follow the new category.</summary>
    public void SetCategory(TrackMode mode)
    {
        if (CurrentTrack is not { } track || track.Mode == mode)
            return;

        if (Change(() => _library.SetMode(track.Key, mode)))
            AppLog.Write($"reclassé en {TrackLibrary.ModeName(mode)} : {track.Title}");

        StateChanged?.Invoke();
    }

    /// <summary>Adds a folder of MP3s, or brings in the new files of one already there; they come in as calm.</summary>
    /// <summary>
    /// Adds a folder, or brings in the new files of one already there, each filed calm or combat by
    /// ear (<see cref="MusicSorter"/>). The listening runs in the background, its count in the notice.
    /// </summary>
    public async void AddFolder(string path)
    {
        if (_sorting)
            return;

        _sorting = true;
        try
        {
            var name = _library.Folders.FirstOrDefault(f => f.Path.Equals(Path.TrimEndingDirectorySeparator(path), StringComparison.OrdinalIgnoreCase))?.Name
                       ?? TrackLibrary.FolderName(path);
            var files = await Task.Run(() => TrackLibrary.MusicFiles(path));
            Notify(string.Format(Strings.Listening, name, Loc.Plural(files.Count, Strings.TracksOne, Strings.TracksMany)));
            var progress = new Progress<int>(done => Notify(string.Format(Strings.ListeningProgress, name, done, files.Count)));
            var modes = await Task.Run(() => MusicSorter.SortFolderAsync(path, files, progress));

            var added = (Folder: (MusicFolder?)null, Count: 0);
            if (!Change(() => added = _library.AddFolder(path, modes: modes)))
                return;

            var combat = _library.Tracks.Count(t => t.Folder == added.Folder!.Name && t.Mode == TrackMode.Combat);
            AppLog.Write($"dossier {added.Folder!.Name} : {added.Count} morceaux ajoutés, sous {added.Folder.Path}, {combat} en combat");
            Notify(added.Count == 0 ? string.Format(Strings.NoNewTrack, added.Folder.Name)
                : string.Format(Strings.TracksAdded, added.Folder.Name, Loc.Plural(added.Count, Strings.TracksOne, Strings.TracksMany), combat));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify(string.Format(Strings.FolderUnreadable, ex.Message));
        }
        finally
        {
            _sorting = false;
        }
    }

    public void RemoveFolder(string name)
    {
        if (Change(() => _library.RemoveFolder(name)))
            AppLog.Write($"dossier retiré : {name}");
        StateChanged?.Invoke();
    }

    public void SetFolderEnabled(string name, bool enabled)
    {
        if (Change(() => _library.SetFolderEnabled(name, enabled)))
            AppLog.Write(enabled ? $"dossier joué : {name}" : $"dossier plus joué : {name}");
        StateChanged?.Invoke();
    }

    public void SetForce(ForceMode force)
    {
        if (force == Force)
            return;

        Force = force;
        _settings.Force = force;
        _saveSettings.Stop();
        _saveSettings.Start();
        AppLog.Write(force switch
        {
            ForceMode.Calm => "forçage : calme",
            ForceMode.Combat => "forçage : combat",
            _ => "forçage : automatique",
        });

        var (mode, reason) = Desired();
        if (mode != ActiveMode)
            SwitchTo(mode, force == ForceMode.Auto ? string.Format(Strings.SwitchBackAuto, reason) : reason);

        StateChanged?.Invoke();
    }

    public void SetVolume(int volume)
    {
        volume = Math.Clamp(volume, 0, 100);
        if (volume == _settings.Volume)
            return;

        _settings.Volume = volume;
        foreach (var deck in _decks.Values)
            deck.SetVolume(VolumeGain(volume));

        _saveSettings.Stop();
        _saveSettings.Start();
        StateChanged?.Invoke();
    }

    /// <summary>Squared, closer to how loudness is heard than a straight line.</summary>
    internal static float VolumeGain(int volume) => MathF.Pow(volume / 100f, 2);

    private bool Change(Action write)
    {
        try
        {
            write();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            AppLog.Write($"bibliothèque non modifiée : {ex.Message}");
            Notify(string.Format(Strings.LibraryNotChanged, ex.Message));
            return false;
        }
    }

    private void Notify(string notice)
    {
        Notice = notice;
        _clearNotice.Stop();
        _clearNotice.Start();
        StateChanged?.Invoke();
    }

    private static bool IsGameProcessRunning()
    {
        var processes = Process.GetProcessesByName(GameProcess);
        foreach (var process in processes)
            process.Dispose();
        return processes.Length > 0;
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_saveSettings.IsEnabled)
            _settings.Save(_settingsPath);

        foreach (var deck in _decks.Values)
            deck.Dispose();
    }
}
