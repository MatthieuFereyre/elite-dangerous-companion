using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using EDC.Companion;
using EDC.ViewModels;

namespace EDC.Views;

/// <summary>
/// The companion's list on the left edge of the game's screen, under the band: the planets worth mapping
/// here and on the route ahead. Over the cockpit's frame, which hides nothing an explorer needs.
/// Click-through, like the band.
/// </summary>
public sealed class CompanionPanel : Window
{
    /// <summary>Of the screen's height, from the top: under the band.</summary>
    private const double TopShare = 0.09;

    /// <summary>Of the screen's width, from the left.</summary>
    private const double LeftShare = 0.008;

    /// <summary>On the system map, smaller and higher, over the Universal Cartographics logo: its menu stays clear.</summary>
    private const double CompactTopShare = 0.018;
    private const double CompactLeftShare = 0.04;

    private readonly StackPanel _lines = new();
    private List<PanelLine> _shown = [];
    private IntPtr _monitor;
    private bool _compact;

    public CompanionPanel()
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

        var orange = CompanionBanner.Orange;
        Content = new Border
        {
            Child = _lines,
            Padding = new Thickness(8, 4, 10, 6),
            Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x0A, 0x0A, 0x0B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xA0, orange.R, orange.G, orange.B)),
            BorderThickness = new Thickness(1, 0, 0, 0),
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        GameOverlay.MakeClickThrough(this);
    }

    /// <summary>Shows <paramref name="lines"/> over the game, or hides the panel when there is nothing to show.</summary>
    public void Update(IReadOnlyList<PanelLine> lines, bool visible, bool compact = false)
    {
        var screen = visible && lines.Count > 0 ? GameOverlay.ForegroundGame(this) : null;
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

        if (!lines.SequenceEqual(_shown) || game.Monitor != _monitor || compact != _compact)
        {
            _shown = lines.ToList();
            _monitor = game.Monitor;
            _compact = compact;
            Fill(lines, Math.Round(game.HeightDip * (compact ? 0.0145 : 0.0135), 1));
        }

        UpdateLayout();
        GameOverlay.MoveTo(this, game.Left + (int)(game.Width * (compact ? CompactLeftShare : LeftShare)),
            game.Top + (int)(game.Height * (compact ? CompactTopShare : TopShare)));
    }

    /// <summary>
    /// A system's name in the band's orange, its planets under it, indented; a planet with a bonus filled
    /// in its tone, dark text on it, like the band's loud chips.
    /// </summary>
    internal void Fill(IReadOnlyList<PanelLine> lines, double fontSize)
    {
        _lines.Children.Clear();
        foreach (var line in lines)
        {
            var text = new TextBlock
            {
                FontSize = fontSize,
                FontWeight = line.Header || line.Loud || line.Targeted ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = line.Loud ? new SolidColorBrush(CompanionBanner.Ink) : CompanionBanner.ToneBrush(line.Tone),
            };
            text.Inlines.Add(new Run(line.Text));
            // Where a ship can land: a small grey SRV after the value.
            if (line.Landable)
                text.Inlines.Add(new InlineUIContainer(SrvIcon.Make(
                    line.Loud ? new SolidColorBrush(CompanionBanner.Ink) : CompanionBanner.ToneBrush(Tone.Muted), fontSize * 0.7))
                {
                    BaselineAlignment = BaselineAlignment.Center,
                });
            var margin = line.Header
                ? new Thickness(0, _lines.Children.Count > 0 ? fontSize * 0.5 : 0, 0, 0)
                : new Thickness(fontSize * 0.8, 0, 0, 0);
            _lines.Children.Add(line.Loud
                ? (FrameworkElement)new Border
                {
                    Child = text,
                    Background = CompanionBanner.ToneBrush(line.Tone),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(5, 0, 6, 1),
                    Margin = new Thickness(margin.Left - 5, 1, 0, 1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                }
                : text);
            if (!line.Loud)
                text.Margin = margin;
        }
    }
}
