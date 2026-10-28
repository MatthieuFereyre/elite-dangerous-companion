using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EDC.Core;
using DrawingIcon = System.Drawing.Icon;
using DrawingBitmap = System.Drawing.Bitmap;

namespace EDC.Views;

public enum IconState { OutOfGame, Calm, Combat }

/// <summary>
/// The HUD's colours: on a near-black tile with a thin rim, a ship's chevron standing on the ellipse of
/// the radar like one of its contacts, in the HUD orange for calm, red for combat, grey out of game; faded when paused. With the
/// controller's level, a gauge along the bottom, the drawing moved up above it.
/// </summary>
public static class IconRenderer
{
    private static readonly Color Tile = Color.FromRgb(0x0E, 0x0C, 0x0A);

    public static Color ColorFor(IconState state) => state switch
    {
        IconState.Calm => Color.FromRgb(0xFF, 0x7A, 0x10),
        IconState.Combat => Color.FromRgb(0xF0, 0x3A, 0x2E),
        _ => Color.FromRgb(0x8B, 0x94, 0x9E),
    };

    /// <summary>Green above half, orange down to 20 %, red below: the colours of a battery.</summary>
    public static Color GaugeColor(int percent) => percent > 50 ? Color.FromRgb(0x3F, 0xB9, 0x50)
        : percent > BatteryAlerts.Low ? Color.FromRgb(0xF0, 0xA0, 0x20)
        : Color.FromRgb(0xFF, 0x4D, 0x4D);

    public static BitmapSource Render(IconState state, bool paused, int size, int? gauge = null)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushOpacity(paused ? 0.55 : 1);
            var accent = new SolidColorBrush(ColorFor(state));

            // The rim stays thin but never under a pixel, or it vanishes at 16 px.
            var rim = Math.Max(1, size / 28.0);
            var tile = new Rect(rim / 2, rim / 2, size - rim, size - rim);
            dc.DrawRoundedRectangle(new SolidColorBrush(Tile), new Pen(accent, rim), tile, size * 0.18, size * 0.18);

            // Drawn in a unit square, then fitted: the whole tile, or the part above the gauge.
            var room = gauge is null ? size : size * 0.72;
            var scale = room * (gauge is null ? 1 : 0.92);
            dc.PushTransform(new TranslateTransform((size - scale) / 2, (room - scale) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));

            // The radar's ellipse, and the stalk that holds a contact over its plane.
            var line = new Pen(accent, Math.Max(0.06, 1.2 / scale));
            dc.DrawEllipse(null, line, new Point(0.5, 0.76), 0.38, 0.12);
            dc.DrawLine(line, new Point(0.5, 0.76), new Point(0.5, 0.5));

            // The contact: a ship's chevron pointing ahead, its notch clear of the radar.
            var ship = new StreamGeometry();
            using (var g = ship.Open())
            {
                g.BeginFigure(new Point(0.5, 0.1), true, true);
                g.LineTo(new Point(0.76, 0.58), true, false);
                g.LineTo(new Point(0.5, 0.46), true, false);
                g.LineTo(new Point(0.24, 0.58), true, false);
            }

            ship.Freeze();
            dc.DrawGeometry(accent, null, ship);
            dc.Pop();
            dc.Pop();
            dc.Pop();

            // Not faded with the pause: the level matters as much with the music off.
            if (gauge is { } percent)
            {
                var frame = new Rect(size * 0.09, size * 0.72, size * 0.82, size * 0.22);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x18)), new Pen(Brushes.White, Math.Max(1, size / 32.0)), frame);
                var inner = Rect.Inflate(frame, -size * 0.06, -size * 0.06);
                inner.Width *= Math.Clamp(percent, 0, 100) / 100.0;
                if (inner.Width > 0)
                    dc.DrawRectangle(new SolidColorBrush(GaugeColor(percent)), null, inner);
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] Png(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// A new icon for the tray. The tray control takes ownership of what it is given and disposes it,
    /// so every call returns a fresh instance.
    /// </summary>
    public static DrawingIcon CreateTrayIcon(IconState state, bool paused, int? gauge)
    {
        using var stream = new MemoryStream(Png(Render(state, paused, 32, gauge)));
        using var bitmap = new DrawingBitmap(stream);
        var handle = bitmap.GetHicon();
        try
        {
            return (DrawingIcon)DrawingIcon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>Writes a multi-size .ico (PNG entries) of the calm icon: the executable's and the window's.</summary>
    public static void WriteIco(string path)
    {
        int[] sizes = [16, 24, 32, 48, 64, 256];
        var images = sizes.Select(s => Png(Render(IconState.Calm, false, s))).ToList();

        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)sizes.Length);

        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }

        foreach (var image in images)
            writer.Write(image);
    }
}
