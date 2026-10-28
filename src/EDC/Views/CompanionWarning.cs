using System.Globalization;
using EDC.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using EDC.Companion;

namespace EDC.Views;

/// <summary>
/// The companion's warning in the middle of the game's screen, above the crosshair so as not to hide
/// the target: big and pulsing while it is new, smaller if it lasts. Click-through, like the band.
/// </summary>
public sealed class CompanionWarning : Window
{
    /// <summary>Of the screen's height, from the top: between the band and the crosshair.</summary>
    private const double TopShare = 0.24;


    private static readonly Brush Backdrop = new SolidColorBrush(Color.FromArgb(0xC8, 0x0A, 0x0A, 0x0B));
    /// <summary>In a fight: a veil only, the enemy behind must stay visible.</summary>
    private static readonly Brush Veil = new SolidColorBrush(Color.FromArgb(0x30, 0x0A, 0x0A, 0x0B));

    private readonly Border _frame;
    private readonly TextBlock _title = new() { FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _detail = new() { HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)) };
    private Warning? _shown;
    private IntPtr _monitor;

    public CompanionWarning()
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

        _frame = new Border
        {
            Child = new StackPanel { Children = { _title, _detail } },
            BorderThickness = new Thickness(0, 2, 0, 2),
        };
        Content = _frame;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        GameOverlay.MakeClickThrough(this);
    }

    /// <summary>Shows <paramref name="warning"/> over the game, or hides the window when there is none.</summary>
    public void Update(Warning? warning, bool visible)
    {
        var screen = visible && warning is not null ? GameOverlay.ForegroundGame(this) : null;
        if (screen is not { } game || warning is null)
        {
            if (IsVisible)
                Hide();
            _shown = null;
            return;
        }

        if (!IsVisible)
            Show();

        if (warning != _shown || game.Monitor != _monitor)
        {
            var restyle = _shown is null || _shown.Big != warning.Big || _shown.Tone != warning.Tone || _shown.SeeThrough != warning.SeeThrough
                || game.Monitor != _monitor;
            _shown = warning;
            _monitor = game.Monitor;
            Fill(warning, game.HeightDip, restyle);
        }

        UpdateLayout();
        var width = (int)Math.Ceiling(ActualWidth * game.Scale);
        GameOverlay.MoveTo(this, game.Left + (game.Width - width) / 2, game.Top + (int)(game.Height * TopShare));
    }

    /// <summary>The text, and the size and pulse when they change: a pulse restarted each second would stutter.</summary>
    internal void Fill(Warning warning, double screenHeight, bool restyle = true)
    {
        _title.Text = warning.Title.ToUpper(Loc.Culture);
        _detail.Text = warning.Detail;
        if (!restyle)
            return;

        var tone = CompanionBanner.ToneBrush(warning.Tone);
        _title.Foreground = tone;
        _frame.BorderBrush = tone;
        _title.FontSize = Math.Round(screenHeight * (warning.Big ? 0.042 : 0.022), 1);
        _detail.FontSize = Math.Round(screenHeight * (warning.Big ? 0.019 : 0.014), 1);
        _frame.Padding = warning.Big ? new Thickness(28, 6, 28, 10) : new Thickness(16, 3, 16, 5);
        _frame.Background = warning.SeeThrough ? Veil : Backdrop;

        // See-through, the letters themselves fade to half: the enemy shows through them too.
        var (high, low) = warning.SeeThrough ? (0.8, 0.4) : (1.0, 0.55);
        _frame.Opacity = high;
        _frame.BeginAnimation(OpacityProperty, warning.Big
            ? new DoubleAnimation(high, low, TimeSpan.FromSeconds(0.45)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
            : null);
    }
}
