using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using EDC.Audio;
using EDC.Core;
using EDC.Resources;
using EDC.Services;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace EDC.Views;

/// <summary>
/// The notification-area icon: its colour says out of game / calm / combat, a left click opens the
/// player, and its menu carries the essentials.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly MusicController _controller;
    private readonly TaskbarIcon _icon;
    private readonly MenuItem _title = new() { IsEnabled = false, FontWeight = FontWeights.SemiBold };
    private readonly MenuItem _like = new() { Header = Strings.Like, IsCheckable = true };
    private readonly MenuItem _playPause = new();
    private readonly MenuItem _autostart = new() { Header = Strings.StartWithWindows, IsCheckable = true };
    private readonly MenuItem _trackToast = new() { Header = Strings.TrackToastSetting, IsCheckable = true };
    private readonly Dictionary<ForceMode, MenuItem> _force = [];

    private readonly ControllerMonitor _pad;

    private (IconState, bool, int?)? _appliedIcon;
    private string _appliedTooltip = "";

    public TrayIconHost(MusicController controller, ControllerMonitor pad)
    {
        _controller = controller;
        _pad = pad;

        var menu = BuildMenu();
        menu.Opened += (_, _) => RefreshMenu();

        _icon = new TaskbarIcon
        {
            ToolTipText = "Elite Dangerous Companion",
            MenuActivation = PopupActivationMode.RightClick,
            NoLeftClickDelay = true,
            ContextMenu = menu,
        };
        _icon.TrayLeftMouseUp += (_, _) => ShowRequested?.Invoke();
        _icon.ForceCreate();

        controller.StateChanged += Update;
        Update();
    }

    public event Action? ShowRequested;
    public event Action? ExitRequested;
    public event Action? ReplayRequested;
    public event Action? UpdateRequested;

    /// <summary>The "track over the game" setting, read each time the menu opens.</summary>
    public Func<bool>? TrackToastEnabled { get; set; }

    public event Action<bool>? TrackToastToggled;

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(_title);
        menu.Items.Add(new Separator());

        _like.Click += (_, _) => _controller.ToggleLike();
        menu.Items.Add(_like);
        menu.Items.Add(Item(Strings.Exclude, _controller.Dislike));
        menu.Items.Add(Item(Strings.Next, _controller.Next));
        _playPause.Click += (_, _) => _controller.TogglePlay();
        menu.Items.Add(_playPause);

        var force = new MenuItem { Header = Strings.MenuMode };
        foreach (var (mode, label) in new[] { (ForceMode.Auto, Strings.ForceAuto), (ForceMode.Calm, Strings.ForceCalm), (ForceMode.Combat, Strings.ForceCombat) })
        {
            var item = new MenuItem { Header = label, IsCheckable = true };
            item.Click += (_, _) => _controller.SetForce(mode);
            _force[mode] = item;
            force.Items.Add(item);
        }

        menu.Items.Add(force);
        menu.Items.Add(new Separator());

        var open = Item(Strings.OpenPlayer, () => ShowRequested?.Invoke());
        open.FontWeight = FontWeights.SemiBold;
        menu.Items.Add(open);

        _trackToast.Click += (_, _) => TrackToastToggled?.Invoke(_trackToast.IsChecked);
        menu.Items.Add(_trackToast);
        menu.Items.Add(Item(Strings.ReplayJournal, () => ReplayRequested?.Invoke()));
        menu.Items.Add(Item(Strings.CheckForUpdate, () => UpdateRequested?.Invoke()));
        _autostart.Click += (_, _) => StartupManager.SetEnabled(_autostart.IsChecked);
        menu.Items.Add(_autostart);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.Quit, () => ExitRequested?.Invoke()));
        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void RefreshMenu()
    {
        var track = _controller.CurrentTrack;
        _title.Header = track is null ? Strings.NoTrack : track.Title;
        _like.IsChecked = track?.Like ?? false;
        _like.IsEnabled = track is not null;
        _playPause.Header = _controller.IsPlaying ? Strings.Pause : Strings.Play;
        foreach (var (mode, item) in _force)
            item.IsChecked = mode == _controller.Force;
        _autostart.IsChecked = StartupManager.IsEnabled();
        _trackToast.IsChecked = TrackToastEnabled?.Invoke() ?? false;
    }

    private void Update()
    {
        var state = !_controller.InGame && !_controller.IsPlaying ? IconState.OutOfGame
            : _controller.ActiveMode == TrackMode.Combat ? IconState.Combat
            : IconState.Calm;
        var paused = !_controller.IsPlaying;
        var pad = _pad.Current;
        int? gauge = pad.Link == PadLink.Wireless ? pad.Percent : null;

        if (_appliedIcon != (state, paused, gauge))
        {
            _appliedIcon = (state, paused, gauge);
            try
            {
                // The setter disposes the icon it replaces: always a fresh one.
                _icon.Icon = IconRenderer.CreateTrayIcon(state, paused, gauge);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or ExternalException)
            {
                AppLog.Write($"icône non mise à jour : {ex.Message}");
            }
        }

        var where = _controller.InGame ? Strings.TrayInGame : Strings.TrayOutOfGame;
        var mode = _controller.ActiveMode == TrackMode.Combat ? Strings.TrayCombat : Strings.TrayCalm;
        var what = _controller.IsPlaying ? _controller.CurrentTrack?.Title ?? Strings.TrayNoTrack : Strings.TrayPaused;
        var manette = pad.Link == PadLink.None ? "" : "\n" + string.Format(Strings.TrayController, pad.Text);
        var tooltip = $"{Strings.AppName} — {where}, {mode}{manette}\n{what}";

        // The Win32 tooltip is capped at 127 characters: the track title, last, is what gets cut.
        if (tooltip.Length > 127)
            tooltip = tooltip[..127];
        if (tooltip != _appliedTooltip)
        {
            _appliedTooltip = tooltip;
            _icon.ToolTipText = tooltip;
        }

        if (_icon.ContextMenu?.IsOpen == true)
            RefreshMenu();
    }

    /// <summary>A Windows notification; held back in game by Windows, hence the chime that goes with it.</summary>
    public void ShowBatteryWarning(PadBattery pad, int threshold) =>
        _icon.ShowNotification(
            string.Format(Strings.TrayController, pad.Text),
            threshold <= BatteryAlerts.Critical ? Strings.BatteryEmpty : Strings.BatteryLow,
            NotificationIcon.Warning);

    public void Dispose()
    {
        _controller.StateChanged -= Update;
        _icon.Dispose();
    }
}
