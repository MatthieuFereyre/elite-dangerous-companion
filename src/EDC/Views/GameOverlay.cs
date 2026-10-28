using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using EDC.Audio;

namespace EDC.Views;

/// <summary>The screen of the game, in device pixels, and the scale of the WPF units on it.</summary>
internal readonly record struct GameScreen(IntPtr Monitor, int Left, int Top, int Width, int Height, double Scale)
{
    public double WidthDip => Width / Scale;
    public double HeightDip => Height / Scale;
}

/// <summary>What the windows over the game share: click-through, never activated, on the game's screen.</summary>
internal static class GameOverlay
{
    public static void MakeClickThrough(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate);
    }

    /// <summary>The screen of the game when it is the window in front; null otherwise (alt-tab, another window).</summary>
    public static GameScreen? ForegroundGame(Visual visual)
    {
        var game = ForegroundGameWindow();
        if (game == IntPtr.Zero)
            return null;

        var monitor = MonitorFromWindow(game, MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return null;

        var scale = PresentationSource.FromVisual(visual)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var area = info.Monitor;
        return new GameScreen(monitor, area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top, scale);
    }

    /// <summary>Places the window, in device pixels, above everything and without taking the focus.</summary>
    public static void MoveTo(Window window, int x, int y) =>
        SetWindowPos(new WindowInteropHelper(window).Handle, HwndTopmost, x, y, 0, 0, SwpNoSize | SwpNoActivate);

    private static IntPtr ForegroundGameWindow()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
            return IntPtr.Zero;

        GetWindowThreadProcessId(window, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName.Equals(MusicController.GameProcess, StringComparison.OrdinalIgnoreCase) ? window : IntPtr.Zero;
        }
        catch (ArgumentException)
        {
            return IntPtr.Zero;
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
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
