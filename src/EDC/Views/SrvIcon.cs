using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EDC.Views;

/// <summary>
/// An SRV seen from the side, drawn for EDC (the game's own icons are Frontier's): low body, domed cab,
/// three wheels a side. It marks a planet a ship can land on.
/// </summary>
public static class SrvIcon
{
    public static readonly Geometry Geometry = Frozen(Geometry.Parse(
        "F0 M1,8.6 L2.2,6 L7,5.4 L9.4,2.4 L15.6,2.4 L18.2,5.2 L22.4,6.2 L23,8.6 Z M10.4,3.4 L15,3.4 L16.8,5.4 L10.4,5.4 Z M2.3,11.2 A2.3,2.3 0 1 1 6.9,11.2 A2.3,2.3 0 1 1 2.3,11.2 Z M3.8,11.2 A0.8,0.8 0 1 1 5.4,11.2 A0.8,0.8 0 1 1 3.8,11.2 Z M9.7,11.2 A2.3,2.3 0 1 1 14.3,11.2 A2.3,2.3 0 1 1 9.7,11.2 Z M11.2,11.2 A0.8,0.8 0 1 1 12.8,11.2 A0.8,0.8 0 1 1 11.2,11.2 Z M17.1,11.2 A2.3,2.3 0 1 1 21.7,11.2 A2.3,2.3 0 1 1 17.1,11.2 Z M18.6,11.2 A0.8,0.8 0 1 1 20.2,11.2 A0.8,0.8 0 1 1 18.6,11.2 Z"));

    private static Geometry Frozen(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }

    /// <summary>The icon at <paramref name="height"/>, a little apart from the text before it.</summary>
    public static Path Make(Brush fill, double height) => new()
    {
        Data = Geometry,
        Fill = fill,
        Stretch = Stretch.Uniform,
        Height = height,
        Margin = new Thickness(height * 0.5, 0, 0, 0),
    };
}
