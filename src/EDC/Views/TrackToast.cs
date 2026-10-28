using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using EDC.Audio;
using EDC.Core;
using EDC.Resources;
using Wpf.Ui.Controls;

namespace EDC.Views;

/// <summary>
/// The track that starts, shown a few seconds over the game: top right of the screen the game is on, with
/// a heart when it is liked. Click-through and never activated, so the game keeps the focus. Works over
/// the game in borderless window mode, not in exclusive fullscreen.
/// </summary>
public sealed class TrackToast : Window
{
    private static readonly Color Orange = Color.FromRgb(0xFF, 0x8A, 0x1E);

    private readonly SymbolIcon _heart = new() { Symbol = SymbolRegular.Heart24, FontSize = 28, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly System.Windows.Controls.TextBlock _mode = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Orange) };
    private readonly System.Windows.Controls.TextBlock _title = new() { FontSize = 21, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 540 };
    private readonly DispatcherTimer _hold = new();

    public TrackToast()
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
        Opacity = 0;

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_mode);
        texts.Children.Add(_title);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_heart);
        row.Children.Add(texts);
        Content = new Border
        {
            Child = row,
            Padding = new Thickness(20, 12, 26, 14),
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x0A, 0x0A, 0x0B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, Orange.R, Orange.G, Orange.B)),
            BorderThickness = new Thickness(1),
        };

        _hold.Tick += (_, _) =>
        {
            _hold.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.4));
            fade.Completed += (_, _) => { if (Opacity == 0) Hide(); };
            BeginAnimation(OpacityProperty, fade);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate);
    }

    /// <summary>Shows <paramref name="title"/> for <paramref name="seconds"/>; a new one replaces the one on screen.</summary>
    public void Announce(string title, bool liked, TrackMode mode, double seconds)
    {
        _title.Text = title;
        _mode.Text = mode == TrackMode.Combat ? Strings.ModeCombat : Strings.ModeCalm;
        _heart.Filled = liked;
        _heart.Foreground = liked ? new SolidColorBrush(Orange) : new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));

        if (!IsVisible)
            Show();
        UpdateLayout();
        PlaceOnGameScreen();

        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.25)));
        _hold.Stop();
        _hold.Interval = TimeSpan.FromSeconds(Math.Max(1, seconds));
        _hold.Start();
    }

    // Top right of the monitor the game is on, in device pixels: the window may sit on another monitor
    // with another scale than the player's.
    private void PlaceOnGameScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(GameWindow(), MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;

        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var area = info.Monitor;
        var margin = (int)((area.Bottom - area.Top) * 0.03);
        SetWindowPos(handle, HwndTopmost, area.Right - width - margin, area.Top + margin, 0, 0, SwpNoSize | SwpNoActivate);
    }

    private static IntPtr GameWindow()
    {
        var processes = Process.GetProcessesByName(MusicController.GameProcess);
        try
        {
            return processes.Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExLayered = 0x80000;
    private const int WsExNoActivate = 0x8000000;
    private const uint MonitorDefaultToPrimary = 1;
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoActivate = 0x10;
    private static readonly IntPtr HwndTopmost = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
