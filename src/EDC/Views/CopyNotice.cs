using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using EDC.Companion;
using EDC.Resources;

namespace EDC.Views;

/// <summary>
/// A discreet line over the game when the route's next step is copied on its own: what to paste in the
/// galaxy map. Bottom right, like a notification: away from the honk reminder that comes with each
/// arrival, from the ring round the star that scooping must not cross, and from the HUD in the middle.
/// Click-through; gone after a few seconds.
/// </summary>
public sealed class CopyNotice : Window
{
    private static readonly TimeSpan Shown = TimeSpan.FromSeconds(6);

    /// <summary>Off the screen's edges, of its height.</summary>
    private const double MarginShare = 0.03;

    private readonly TextBlock _label = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8)), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _system = new() { FontWeight = FontWeights.SemiBold, Foreground = CompanionBanner.ToneBrush(Tone.Info), VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _hold = new();
    private bool _visible;

    public CopyNotice()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Bahnschrift");

        Content = new Border
        {
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _label, _system } },
            Padding = new Thickness(12, 3, 12, 4),
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x0A, 0x0A, 0x0B)),
        };

        _hold.Tick += (_, _) =>
        {
            _hold.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.4));
            fade.Completed += (_, _) => { if (!_hold.IsEnabled) Hide(); };
            BeginAnimation(OpacityProperty, fade);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        GameOverlay.MakeClickThrough(this);
    }

    /// <summary>Shows <paramref name="system"/> as copied, a new one replacing the one on screen.</summary>
    public void Announce(string system)
    {
        _label.Text = Strings.NextCopied + "  ";
        _system.Text = system;
        _hold.Stop();
        _hold.Interval = Shown;
        _hold.Start();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Place();
    }

    /// <summary>With each read of the game's files: whether the companion shows over the game.</summary>
    public void Update(bool visible)
    {
        _visible = visible;
        if (_hold.IsEnabled)
            Place();
    }

    private void Place()
    {
        if ((_visible ? GameOverlay.ForegroundGame(this) : null) is not { } game)
        {
            if (IsVisible)
                Hide();
            return;
        }

        if (!IsVisible)
            Show();
        _label.FontSize = Math.Round(game.HeightDip * 0.014, 1);
        _system.FontSize = Math.Round(game.HeightDip * 0.017, 1);
        UpdateLayout();
        var (x, y) = PlaceOn(game, (int)Math.Ceiling(ActualWidth * game.Scale), (int)Math.Ceiling(ActualHeight * game.Scale));
        GameOverlay.MoveTo(this, x, y);
    }

    /// <summary>Where a notice of this size goes on the game's screen, in device pixels.</summary>
    internal static (int X, int Y) PlaceOn(GameScreen game, int width, int height)
    {
        var margin = (int)(game.Height * MarginShare);
        return (game.Left + game.Width - width - margin, game.Top + game.Height - height - margin);
    }
}
