using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using EDC.Resources;
using EDC.ViewModels;
using Wpf.Ui.Controls;

namespace EDC.Views;

public partial class PlayerWindow : FluentWindow
{
    public PlayerWindow(PlayerViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        viewModel.CurrentTrackShown += _ => BringCurrentTrackIntoView();
        viewModel.Companion.Neutron.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NeutronViewModel.Next))
                BringNextStepIntoView();
        };
        BringNextStepIntoView();
    }

    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const double Edge = 8;

    // The tabs are laid out at a fixed width and the height follows the content: the window is not to be
    // resized. FluentWindow handles its edges itself whatever ResizeMode says, so a point near an edge
    // answers "inside" to Windows: no resize cursor, no resizing. Added after FluentWindow's hooks, this
    // one is asked first; everything else goes on to them.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(KeepSize);
    }

    private IntPtr KeepSize(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest || !GetWindowRect(hwnd, out var window))
            return IntPtr.Zero;

        var x = unchecked((short)(long)lParam);
        var y = unchecked((short)((long)lParam >> 16));
        var band = (int)Math.Ceiling(Edge * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var nearEdge = x < window.Left + band || x >= window.Right - band || y >= window.Bottom - band || y < window.Top + band / 2;
        if (!nearEdge)
            return IntPtr.Zero;

        handled = true;
        return HtClient;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);

    private void OnNeutronRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NeutronRow row })
            ((PlayerViewModel)DataContext).Companion.Neutron.CopyStepCommand.Execute(row);
    }

    // The next step near the top, the one just reached above it.
    private void BringNextStepIntoView() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
    {
        var next = ((PlayerViewModel)DataContext).Companion.Neutron.Next;
        if (next is null || NeutronItems.ItemContainerGenerator.ContainerFromItem(next) is not FrameworkElement row)
            return;

        var top = row.TranslatePoint(new Point(0, 0), NeutronItems).Y;
        NeutronScroll.ScrollToVerticalOffset(Math.Max(0, top - row.ActualHeight));
    });

    /// <summary>Set when the app really quits; otherwise closing only hides the window to the tray.</summary>
    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void OnSeekBarClick(object sender, MouseButtonEventArgs e)
    {
        var bar = (FrameworkElement)sender;
        if (bar.ActualWidth > 0)
            ((PlayerViewModel)DataContext).Seek(e.GetPosition(bar).X / bar.ActualWidth);
    }

    private void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Strings.AddFolderDialog };
        if (dialog.ShowDialog(this) == true)
            ((PlayerViewModel)DataContext).AddFolder(dialog.FolderName);
    }

    private void OnRemoveFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderRow row })
            return;

        var answer = System.Windows.MessageBox.Show(this,
            string.Format(Strings.RemoveFolderAsk, row.Name),
            Strings.RemoveFolderTitle, System.Windows.MessageBoxButton.OKCancel, MessageBoxImage.Question, System.Windows.MessageBoxResult.Cancel);
        if (answer == System.Windows.MessageBoxResult.OK)
            ((PlayerViewModel)DataContext).RemoveFolder(row.Name);
    }

    private void OnPlaylistRowClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: PlaylistItem item })
            ((PlayerViewModel)DataContext).PlayNow(item.File);
    }

    // The current track near the top, the one before just above it: the one likely to be liked late.
    // After the layout, once the rows of the new list exist; a hidden window is done on showing.
    private void BringCurrentTrackIntoView() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
    {
        var current = ((PlayerViewModel)DataContext).Playlist.FirstOrDefault(i => i.IsCurrent);
        if (current is null || PlaylistItems.ItemContainerGenerator.ContainerFromItem(current) is not FrameworkElement row)
            return;

        var top = row.TranslatePoint(new Point(0, 0), PlaylistItems).Y;
        PlaylistScroll.ScrollToVerticalOffset(Math.Max(0, top - row.ActualHeight));
    });

    /// <summary>Shown over the desktop without taking the focus: the game starting keeps it.</summary>
    public void ShowWithoutFocus()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
            return;

        BringCurrentTrackIntoView();
        ShowActivated = false;
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        ShowActivated = true;
    }

    public void ShowAndActivate()
    {
        BringCurrentTrackIntoView();
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
}
