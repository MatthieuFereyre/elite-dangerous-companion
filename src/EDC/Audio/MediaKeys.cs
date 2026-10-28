using System.Windows;
using Windows.Media;
using Windows.Media.Playback;

namespace EDC.Audio;

/// <summary>
/// The keyboard's media keys, through the media controls of Windows — the ones of the volume flyout,
/// which also show the track. Play/pause, next, and previous to restart the track: there is no history
/// to go back to. Windows sends the keys to the session that played last, so a player started since
/// takes them until this one plays again.
/// </summary>
public sealed class MediaKeys : IDisposable
{
    private readonly MusicController _controller;
    // Never plays: it only lends its media controls to an app without a UWP window.
    private readonly MediaPlayer _anchor = new();
    private readonly SystemMediaTransportControls _controls;
    private string? _shownTitle;
    private bool? _shownPlaying;

    public MediaKeys(MusicController controller)
    {
        _controller = controller;
        _anchor.CommandManager.IsEnabled = false;
        _controls = _anchor.SystemMediaTransportControls;
        _controls.IsEnabled = true;
        _controls.IsPlayEnabled = true;
        _controls.IsPauseEnabled = true;
        _controls.IsStopEnabled = true;
        _controls.IsNextEnabled = true;
        _controls.IsPreviousEnabled = true;
        _controls.ButtonPressed += OnButtonPressed;

        controller.StateChanged += Update;
        Update();
    }

    // Raised on a thread-pool thread; the controller lives on the UI one.
    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs e) =>
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            switch (e.Button)
            {
                case SystemMediaTransportControlsButton.Play when !_controller.IsPlaying:
                case SystemMediaTransportControlsButton.Pause when _controller.IsPlaying:
                case SystemMediaTransportControlsButton.Stop when _controller.IsPlaying:
                    _controller.TogglePlay();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    _controller.Next();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    _controller.Seek(0);
                    break;
            }
        });

    // StateChanged comes four times a second: Windows is only told what changed.
    private void Update()
    {
        if (_controller.IsPlaying != _shownPlaying)
        {
            _shownPlaying = _controller.IsPlaying;
            _controls.PlaybackStatus = _controller.IsPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
        }

        var track = _controller.CurrentTrack;
        if (track?.Title != _shownTitle)
        {
            _shownTitle = track?.Title;
            var display = _controls.DisplayUpdater;
            display.Type = MediaPlaybackType.Music;
            display.MusicProperties.Title = track?.Title ?? "";
            display.MusicProperties.Artist = track?.Section ?? "";
            display.Update();
        }
    }

    public void Dispose()
    {
        _controller.StateChanged -= Update;
        _controls.ButtonPressed -= OnButtonPressed;
        _controls.IsEnabled = false;
        _anchor.Dispose();
    }
}
