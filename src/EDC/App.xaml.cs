using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using EDC.Audio;
using EDC.Companion;
using EDC.Core;
using EDC.Resources;
using EDC.Services;
using EDC.ViewModels;
using EDC.Views;

namespace EDC;

/// <summary>
///   EDC.exe                           lives in the tray, plays while Elite Dangerous runs
///   EDC.exe --show                    same, with the window open
///   EDC.exe --data-dir &lt;folder&gt;     another folder for the settings, the library and the log
///   EDC.exe --simulate &lt;journal&gt;      replays a game journal and lists the switches, no sound
/// </summary>
public partial class App : Application
{
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private TrackLibrary? _library;
    private MusicController? _controller;
    private TrayIconHost? _tray;
    private ControllerMonitor? _pad;
    private MediaKeys? _mediaKeys;
    private CompanionService? _companion;
    private CompanionBanner? _banner;
    private CompanionWarning? _warning;
    private CompanionPanel? _panel;
    private Updater? _updater;
    private PlayerWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var args = e.Args;
        var paths = AppPaths.Resolve(Option(args, "--data-dir"));

        if (Option(args, "--simulate") is { } journal)
        {
            Simulate(journal, paths);
            return;
        }

        // One player per data folder: a second launch only brings the window of the first one up.
        var key = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(paths.Folder.ToLowerInvariant())))[..16];
        _instanceMutex = new Mutex(initiallyOwned: true, $@"Local\EDC.{key}", out var first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting($@"Local\EDC.Show.{key}").Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        AppLog.FilePath = paths.LogFile;
        paths.Prepare();
        DispatcherUnhandledException += OnUnhandledException;
        EliteTheme.FollowWindows();

        var settings = PlayerSettings.Load(paths.SettingsFile);
        Loc.Apply(settings.Language);
        _library = new TrackLibrary(paths.LibraryFile);
        _library.WatchFile();
        _controller = new MusicController(_library, settings, paths.SettingsFile, new JournalTail(JournalTail.DefaultDirectory));

        _pad = new ControllerMonitor();
        _companion = new CompanionService(JournalTail.DefaultDirectory);
        _updater = new Updater(settings);
        var companion = new CompanionViewModel(_companion, () => _pad.Current, settings, () => settings.Save(paths.SettingsFile), _updater);
        _window = new PlayerWindow(new PlayerViewModel(_controller, _pad, companion, settings, () => settings.Save(paths.SettingsFile)));
        RestoreWindowPosition(settings);
        // Closing to the tray only hides the window (PlayerWindow.OnClosing raises no Closing then).
        _window.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
                SaveWindowPosition(settings, paths.SettingsFile);
        };
        _window.Closing += (_, _) => SaveWindowPosition(settings, paths.SettingsFile);
        _tray = new TrayIconHost(_controller, _pad);
        var alerts = new BatteryAlerts();
        _pad.Changed += () =>
        {
            AppLog.Write($"manette : {_pad.Current.Text}");
            if (alerts.Check(_pad.Current) is { } threshold)
            {
                Chime.Play(threshold <= BatteryAlerts.Critical ? 2 : 1);
                _tray.ShowBatteryWarning(_pad.Current, threshold);
            }
        };
        _pad.Start();

        // The track that starts, over the game.
        var toast = new TrackToast();
        var announcer = new TrackAnnouncer();
        _tray.TrackToastEnabled = () => settings.TrackToast;
        _tray.TrackToastToggled += enabled =>
        {
            settings.TrackToast = enabled;
            settings.Save(paths.SettingsFile);
            AppLog.Write(enabled ? "morceau affiché en jeu" : "morceau plus affiché en jeu");
        };
        _controller.StateChanged += () =>
        {
            if (!_controller.InGame)
                announcer.Reset();
            var track = _controller.CurrentTrack;
            if (announcer.Check(track?.Key, _controller.IsPlaying, _controller.InGame) && settings.TrackToast)
                toast.Announce(track!.Title, track.Like, _controller.ActiveMode, settings.TrackToastSeconds);
        };
        _tray.ShowRequested += () => _window.ShowAndActivate();
        _controller.GameStarted += () =>
        {
            _window.ShowWithoutFocus();
            EdmcLauncher.LaunchWithGame(settings.LaunchEdmc);
        };
        _tray.ExitRequested += Quit;
        _tray.ReplayRequested += () =>
        {
            if (ReplayWindow.PickJournal() is { } picked)
                new ReplayWindow(picked, _controller.CalmDelay).Show();
        };

        // A convenience: without it the player still plays, so a failure is only logged.
        try
        {
            _mediaKeys = new MediaKeys(_controller);
        }
        catch (Exception ex)
        {
            AppLog.Write($"touches multimédia indisponibles : {ex.Message}");
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\EDC.Show.{key}");
        new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => _window?.ShowAndActivate());
        }) { IsBackground = true, Name = "EDC.ShowListener" }.Start();

        // The companion's band and warning over the game, refreshed with each read of the game's files.
        _banner = new CompanionBanner();
        _warning = new CompanionWarning();
        _panel = new CompanionPanel();
        _companion.Changed += () =>
        {
            var now = DateTime.UtcNow;
            _companion.State.MinNotableValue = settings.PanelMinValue;
            var visible = settings.CompanionBanner && _controller.InGame && Briefing.BannerVisible(_companion.Status);
            _banner.Update(Briefing.Banner(_companion.State, _companion.Status, settings.ControllerInBanner ? _pad.Current : PadBattery.None, now), visible);
            _warning.Update(Briefing.Warning(_companion.State, _companion.Status, now, settings.HonkReminder), visible);
            _panel.Update(Briefing.Panel(_companion.State, _companion.Status, now), settings.ExplorationPanel && _controller.InGame && Briefing.PanelVisible(_companion.Status),
                compact: _companion.Status.SystemMap);
        };
        _companion.Start();

        _tray.UpdateRequested += async () => await _updater.CheckNowAsync();
        _updater.Start();

        _controller.Start();

        if (args.Contains("--show", StringComparer.OrdinalIgnoreCase))
            _window.ShowAndActivate();
    }

    // Where it was last, if that still lands on a screen: a monitor unplugged since sends it back to the centre.
    private void RestoreWindowPosition(PlayerSettings settings)
    {
        if (_window is null || settings.WindowLeft is not { } left || settings.WindowTop is not { } top)
            return;

        var onScreen = left + 40 >= SystemParameters.VirtualScreenLeft
                       && left + 40 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                       && top >= SystemParameters.VirtualScreenTop
                       && top + 20 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        if (!onScreen)
            return;

        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        _window.Left = left;
        _window.Top = top;
    }

    // On hiding to the tray and on quitting. Minimized, the place to remember is the one it will come back to.
    private void SaveWindowPosition(PlayerSettings settings, string settingsFile)
    {
        if (_window is null)
            return;

        var bounds = _window.WindowState == WindowState.Normal ? new Rect(_window.Left, _window.Top, 0, 0) : _window.RestoreBounds;
        if (double.IsNaN(bounds.Left) || bounds.IsEmpty || (settings.WindowLeft == bounds.Left && settings.WindowTop == bounds.Top))
            return;

        settings.WindowLeft = bounds.Left;
        settings.WindowTop = bounds.Top;
        settings.Save(settingsFile);
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    // ------------------------------------------------------------------------------------- replay

    /// <summary>
    /// The old -Simulate: replays a journal and lists the switches, without a sound. Printed when the
    /// output is piped (<c>| Out-String</c>); otherwise shown in a window, since PowerShell does not wait
    /// for a windowed program and its output would land after the prompt.
    /// </summary>
    private void Simulate(string journal, AppPaths paths)
    {
        var calmDelay = TimeSpan.FromSeconds(PlayerSettings.Load(paths.SettingsFile).CalmDelaySeconds);

        if (Console.IsOutputRedirected)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (!File.Exists(journal))
            {
                Console.Error.WriteLine(string.Format(Strings.JournalNotFound, journal));
                Shutdown(1);
                return;
            }

            foreach (var line in JournalReplay.Run(File.ReadLines(journal), calmDelay))
                Console.WriteLine(line);
            Shutdown();
            return;
        }

        if (!File.Exists(journal))
        {
            MessageBox.Show(string.Format(Strings.JournalNotFound, journal), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        EliteTheme.FollowWindows();
        var window = new ReplayWindow(journal, calmDelay);
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }

    // ------------------------------------------------------------------------------------- shutdown

    private void Quit()
    {
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_controller is not null)
            AppLog.Write("lecteur arrêté");

        _mediaKeys?.Dispose();
        _updater?.Dispose();
        _companion?.Dispose();
        _banner?.Close();
        _warning?.Close();
        _panel?.Close();
        _pad?.Dispose();
        _tray?.Dispose();
        _controller?.Dispose();
        _library?.Dispose();
        _showEvent?.Dispose();
        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    // No dialog: it would steal the focus from the game. The log says what happened.
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write($"erreur : {e.Exception}");
        e.Handled = true;
    }
}
