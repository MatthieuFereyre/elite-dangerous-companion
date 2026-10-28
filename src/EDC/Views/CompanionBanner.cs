using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EDC.Companion;

namespace EDC.Views;

/// <summary>
/// The companion's band over the game: top centre of its screen, above the comms panel on the left and
/// the info panel on the right, over the black of space the cockpit leaves there. One line, the most
/// important first; what does not fit is left out. Click-through and never activated, like
/// <see cref="TrackToast"/>, and like it only seen in borderless window mode.
/// </summary>
public sealed class CompanionBanner : Window
{
    /// <summary>Of the screen's width: above the two top panels of the HUD, which start lower, at 16:9.</summary>
    private const double WidthShare = 0.6;

    internal static readonly Color Orange = Color.FromRgb(0xFF, 0x8A, 0x1E);
    internal static readonly Color Ink = Color.FromRgb(0x0A, 0x0A, 0x0B);

    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private List<Chip> _shown = [];
    private double _fontSize = 15;
    private IntPtr _monitor;

    public CompanionBanner()
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
            Child = _row,
            Padding = new Thickness(6, 3, 6, 4),
            Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x0A, 0x0A, 0x0B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xA0, Orange.R, Orange.G, Orange.B)),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    public static Brush ToneBrush(Tone tone) => tone switch
    {
        Tone.Good => new SolidColorBrush(Color.FromRgb(0x5E, 0xC8, 0xFF)),
        Tone.Warn => new SolidColorBrush(Color.FromRgb(0xFF, 0xC2, 0x33)),
        Tone.Alert => new SolidColorBrush(Color.FromRgb(0xFF, 0x4B, 0x3E)),
        Tone.Muted => new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
        _ => new SolidColorBrush(Orange),
    };

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        GameOverlay.MakeClickThrough(this);
    }

    /// <summary>Shows <paramref name="chips"/> over the game, or hides the band when there is nothing to show.</summary>
    public void Update(IReadOnlyList<Chip> chips, bool visible)
    {
        var screen = visible && chips.Count > 0 ? GameOverlay.ForegroundGame(this) : null;
        if (screen is not { } game)
        {
            if (IsVisible)
                Hide();
            return;
        }

        if (!IsVisible)
        {
            Show();
            _shown = [];
        }

        if (!chips.SequenceEqual(_shown) || game.Monitor != _monitor)
        {
            _shown = chips.ToList();
            _monitor = game.Monitor;
            _fontSize = Math.Round(game.HeightDip * 0.0145, 1);
            Fill(chips, game.WidthDip * WidthShare);
        }

        UpdateLayout();
        var width = (int)Math.Ceiling(ActualWidth * game.Scale);
        GameOverlay.MoveTo(this, game.Left + (game.Width - width) / 2, game.Top + (int)(game.Height * 0.006));
    }

    /// <summary>Chips in order, separated by a thin bar, as many as fit in <paramref name="maxWidth"/>.</summary>
    internal void Fill(IReadOnlyList<Chip> chips, double maxWidth)
    {
        _row.Children.Clear();
        var used = 12.0;
        foreach (var chip in chips)
        {
            var separator = _row.Children.Count > 0 ? Separator() : null;
            var text = new TextBlock
            {
                Text = chip.Text,
                FontSize = _fontSize,
                FontWeight = chip.Tone == Tone.Alert || chip.Loud ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = chip.Loud ? new SolidColorBrush(Ink) : ToneBrush(chip.Tone),
                VerticalAlignment = VerticalAlignment.Center,
            };

            // A loud chip is the tone's colour filled in, dark text on it.
            FrameworkElement element = chip.Loud
                ? new Border { Child = text, Background = ToneBrush(chip.Tone), CornerRadius = new CornerRadius(2), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(2, 0, 2, 0) }
                : text;
            if (!chip.Loud)
                text.Margin = new Thickness(6, 0, 6, 0);

            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var width = element.DesiredSize.Width + (separator is null ? 0 : 1 + 2);
            if (used + width > maxWidth)
                break;

            used += width;
            if (separator is not null)
                _row.Children.Add(separator);
            _row.Children.Add(element);
        }
    }

    internal void SetFontSize(double size) => _fontSize = size;

    private UIElement Separator() => new Border
    {
        Width = 1,
        Margin = new Thickness(1, 3, 1, 3),
        Background = new SolidColorBrush(Color.FromArgb(0x70, Orange.R, Orange.G, Orange.B)),
    };
}
