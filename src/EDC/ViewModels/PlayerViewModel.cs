using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDC.Audio;
using EDC.Core;
using EDC.Resources;
using EDC.Services;
using Wpf.Ui.Controls;

namespace EDC.ViewModels;

/// <summary>A line of the playlist: the track heard before, the current one, or one to come.</summary>
public sealed record PlaylistItem(string File, string Title, bool IsLiked, bool IsCurrent, bool IsPlayed, string Duration = "");

/// <summary>A music folder in the window: played or not, and how its tracks are sorted.</summary>
public sealed record FolderRow(string Name, string Path, bool Enabled, string Summary)
{
    public static FolderRow From(MusicFolder folder, IEnumerable<Track> tracks)
    {
        var own = tracks.Where(t => t.Folder.Equals(folder.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        var count = Loc.Plural(own.Count, Strings.TracksOne, Strings.TracksMany);
        var summary = !Directory.Exists(folder.Path)
            ? string.Format(Strings.FolderMissing, count)
            : string.Join(" · ", new[]
            {
                count,
                Loc.Plural(own.Count(t => t.Mode == TrackMode.Calm), Strings.CalmOne, Strings.CalmMany),
                string.Format(Strings.CombatCount, own.Count(t => t.Mode == TrackMode.Combat)),
                Loc.Plural(own.Count(t => t.Mode == TrackMode.Off), Strings.ExcludedOne, Strings.ExcludedMany),
                Loc.Plural(own.Count(t => t.Like), Strings.LikedOne, Strings.LikedMany),
            });
        return new FolderRow(folder.Name, folder.Path, folder.Enabled, summary);
    }
}

/// <summary>What the window shows, refreshed from the controller each time it reports a change.</summary>
public sealed partial class PlayerViewModel : ObservableObject
{
    private readonly MusicController? _controller;
    private readonly ControllerMonitor? _monitor;
    private readonly PlayerSettings? _settings;
    private readonly Action? _saveSettings;
    private readonly TrackDurations _durations = new();
    private bool _refreshing;
    private string? _playlistKey;

    public PlayerViewModel(MusicController controller, ControllerMonitor pad, CompanionViewModel companion, PlayerSettings settings, Action saveSettings)
    {
        _controller = controller;
        _monitor = pad;
        _settings = settings;
        _saveSettings = saveSettings;
        Companion = companion;
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _durations.Changed += () => dispatcher.BeginInvoke(Refresh);
        controller.StateChanged += Refresh;
        Refresh();
    }

    /// <summary>For an off-screen render: the values are then set by hand.</summary>
    internal PlayerViewModel()
    {
    }

    [ObservableProperty] private bool _inGame;
    [ObservableProperty] private bool _isCombat;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string _reason = "";
    [ObservableProperty] private string _title = Strings.NoTrack;
    [ObservableProperty] private string _section = "";
    [ObservableProperty] private bool _hasTrack;
    [ObservableProperty] private bool _isLiked;
    [ObservableProperty] private bool _isCombatTrack;
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _libraryText = "";
    [ObservableProperty] private string? _notice;
    [ObservableProperty] private int _volume;
    [ObservableProperty] private ForceMode _force;
    [ObservableProperty] private string _playlistText = "";
    [ObservableProperty] private PadBattery _pad = PadBattery.None;

    public enum Tab { Music, Commander, Combat, Exploration, Neutron, Settings }

    /// <summary>The tab shown: the music at each start.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMusic))]
    [NotifyPropertyChangedFor(nameof(ShowCombat))]
    [NotifyPropertyChangedFor(nameof(ShowExploration))]
    [NotifyPropertyChangedFor(nameof(ShowNeutron))]
    [NotifyPropertyChangedFor(nameof(ShowCommander))]
    [NotifyPropertyChangedFor(nameof(ShowSettings))]
    private Tab _shown;

    /// <summary>Right of the player: what comes next, or the music folders.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaylistPane))]
    [NotifyPropertyChangedFor(nameof(ShowFoldersPane))]
    private bool _foldersPane;

    public bool ShowPlaylistPane
    {
        get => !FoldersPane;
        set { if (value) FoldersPane = false; }
    }

    public bool ShowFoldersPane
    {
        get => FoldersPane;
        set { if (value) FoldersPane = true; }
    }

    // Each radio button only ever sets its own tab: the group unchecking the others must not write back.
    public bool ShowMusic
    {
        get => Shown == Tab.Music;
        set { if (value) Shown = Tab.Music; }
    }

    public bool ShowCombat
    {
        get => Shown == Tab.Combat;
        set { if (value) Shown = Tab.Combat; }
    }

    public bool ShowExploration
    {
        get => Shown == Tab.Exploration;
        set { if (value) Shown = Tab.Exploration; }
    }

    public bool ShowNeutron
    {
        get => Shown == Tab.Neutron;
        set { if (value) Shown = Tab.Neutron; }
    }

    public bool ShowCommander
    {
        get => Shown == Tab.Commander;
        set { if (value) Shown = Tab.Commander; }
    }

    public bool ShowSettings
    {
        get => Shown == Tab.Settings;
        set { if (value) Shown = Tab.Settings; }
    }

    public CompanionViewModel Companion { get; set; } = new();

    /// <summary>Rebuilt only when it changes, not at each refresh, so the list keeps its scroll.</summary>
    public ObservableCollection<PlaylistItem> Playlist { get; } = [];

    /// <summary>The music folders, rebuilt only when one of them changes.</summary>
    public ObservableCollection<FolderRow> Folders { get; } = [];

    /// <summary>Raised after the playlist is rebuilt on a new current track, for the view to bring it into sight.</summary>
    public event Action<PlaylistItem>? CurrentTrackShown;

    /// <summary>"v1.0.0": the version of the release, from its tag; "v0.0.0" for a build from the sources.</summary>
    public string AppVersion { get; } = "v" + (typeof(PlayerViewModel).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?");

    public string InGameText => InGame ? Strings.InGame : Strings.OutOfGame;
    public string ModeText => IsCombat ? Strings.ModeCombat : Strings.ModeCalm;
    public string PlayPauseText => IsPlaying ? Strings.Pause : Strings.Play;
    public string LikeText => IsLiked ? Strings.Liked : Strings.Like;

    public string PadText => Pad.Text.ToUpperInvariant();
    public bool PadLow => Pad.Link == PadLink.Wireless && Pad.Percent <= BatteryAlerts.Low;

    /// <summary>The controller, crossed out when absent: the word "manette" would not fit on the line.</summary>
    public SymbolRegular PadSymbol => Pad.Link == PadLink.None ? SymbolRegular.XboxControllerError24 : SymbolRegular.XboxController24;

    public bool IsForceAuto
    {
        get => Force == ForceMode.Auto;
        set { if (value) SetForce(ForceMode.Auto); }
    }

    public bool IsForceCalm
    {
        get => Force == ForceMode.Calm;
        set { if (value) SetForce(ForceMode.Calm); }
    }

    public bool IsForceCombat
    {
        get => Force == ForceMode.Combat;
        set { if (value) SetForce(ForceMode.Combat); }
    }

    partial void OnInGameChanged(bool value) => OnPropertyChanged(nameof(InGameText));

    partial void OnIsCombatChanged(bool value) => OnPropertyChanged(nameof(ModeText));

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayPauseText));
    partial void OnIsLikedChanged(bool value) => OnPropertyChanged(nameof(LikeText));

    partial void OnPadChanged(PadBattery value)
    {
        OnPropertyChanged(nameof(PadText));
        OnPropertyChanged(nameof(PadLow));
        OnPropertyChanged(nameof(PadSymbol));
    }

    partial void OnForceChanged(ForceMode value)
    {
        OnPropertyChanged(nameof(IsForceAuto));
        OnPropertyChanged(nameof(IsForceCalm));
        OnPropertyChanged(nameof(IsForceCombat));
    }

    partial void OnVolumeChanged(int value)
    {
        if (!_refreshing)
            _controller?.SetVolume(value);
    }

    partial void OnIsCombatTrackChanged(bool value)
    {
        if (!_refreshing)
            _controller?.SetCategory(value ? TrackMode.Combat : TrackMode.Calm);
    }

    private void SetForce(ForceMode force)
    {
        Force = force;
        _controller?.SetForce(force);
    }

    public void Seek(double fraction) => _controller?.Seek(fraction);
    public void PlayNow(string file) => _controller?.PlayNow(file);

    [RelayCommand] private void ToggleLikeOf(string file) => _controller?.ToggleLike(file);
    [RelayCommand] private void ToggleFolder(FolderRow row) => _controller?.SetFolderEnabled(row.Name, !row.Enabled);

    public void AddFolder(string path) => _controller?.AddFolder(path);
    public void RemoveFolder(string name) => _controller?.RemoveFolder(name);

    [RelayCommand] private void TogglePlay() => _controller?.TogglePlay();
    [RelayCommand] private void Next() => _controller?.Next();
    [RelayCommand] private void ToggleLike() => _controller?.ToggleLike();
    [RelayCommand] private void Dislike() => _controller?.Dislike();

    public void Refresh()
    {
        if (_controller is not { } c)
            return;

        _refreshing = true;
        try
        {
            InGame = c.InGame;
            IsCombat = c.ActiveMode == TrackMode.Combat;
            IsPlaying = c.IsPlaying;
            Reason = string.Format(Loc.Culture, Strings.SwitchAt, c.SwitchReason, c.SwitchTime);
            Force = c.Force;
            Volume = c.Volume;
            Notice = c.Notice;
            Pad = _monitor?.Current ?? PadBattery.None;

            var track = c.CurrentTrack;
            HasTrack = track is not null;
            Title = track?.Title ?? Strings.NoTrack;
            Section = track?.Section ?? "";
            IsLiked = track?.Like ?? false;
            IsCombatTrack = track?.Mode == TrackMode.Combat;

            var position = c.Position;
            var duration = c.Duration;
            PositionText = Format(position);
            DurationText = Format(duration);
            Progress = duration > TimeSpan.Zero ? Math.Clamp(position / duration, 0, 1) : 0;

            var (played, upcoming) = c.Round();
            var shown = played.Concat(track is null ? [] : [track]).Concat(upcoming).ToList();
            _durations.Request(shown.Select(c.Library.FullPath));
            string Length(Track t) => _durations.Get(c.Library.FullPath(t)) is { } d ? Format(d) : "";
            SetPlaylist(
                played.Select(t => new PlaylistItem(t.Key, t.Title, t.Like, IsCurrent: false, IsPlayed: true, Length(t)))
                    .Concat(track is null ? [] : [new PlaylistItem(track.Key, track.Title, track.Like, IsCurrent: true, IsPlayed: false, Length(track))])
                    .Concat(upcoming.Select(t => new PlaylistItem(t.Key, t.Title, t.Like, IsCurrent: false, IsPlayed: false, Length(t))))
                    .ToList());
            PlaylistText = string.Format(Strings.UpNext, ModeText, upcoming.Count);

            var tracks = c.Library.Tracks;
            LibraryText = string.Format(Strings.LibrarySummary, tracks.Count(t => t.Mode == TrackMode.Calm),
                tracks.Count(t => t.Mode == TrackMode.Combat), tracks.Count(t => t.Like), tracks.Count(t => t.Mode == TrackMode.Off));
            SetFolders(c.Library.Folders.Select(f => FolderRow.From(f, tracks)).ToList());
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void SetFolders(IReadOnlyList<FolderRow> rows)
    {
        if (rows.SequenceEqual(Folders))
            return;

        Folders.Clear();
        foreach (var row in rows)
            Folders.Add(row);
    }

    internal void SetPlaylist(IReadOnlyList<PlaylistItem> items)
    {
        var key = string.Join("|", items.Select(i => $"{i.File}{i.IsLiked}{i.IsCurrent}{i.Duration}"));
        if (key == _playlistKey)
            return;

        var previousCurrent = Playlist.FirstOrDefault(i => i.IsCurrent)?.File;
        _playlistKey = key;
        Playlist.Clear();
        foreach (var item in items)
            Playlist.Add(item);

        if (items.FirstOrDefault(i => i.IsCurrent) is { } current && current.File != previousCurrent)
            CurrentTrackShown?.Invoke(current);
    }

    internal static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
}
