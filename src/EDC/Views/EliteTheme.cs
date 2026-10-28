using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace EDC.Views;

/// <summary>
/// The colours of Elite Dangerous over WPF-UI: the HUD orange as accent, a near-black background, square
/// corners, Bahnschrift. Follows the light or dark mode of Windows apps; the light one is a warm paper
/// with a deeper orange, the HUD orange being unreadable on white.
/// </summary>
internal static class EliteTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static void FollowWindows()
    {
        Apply(WindowsTheme());
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
                Application.Current.Dispatcher.BeginInvoke(() => Apply(WindowsTheme()));
        };
    }

    public static ApplicationTheme WindowsTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is 1 ? ApplicationTheme.Light : ApplicationTheme.Dark;
    }

    public static void Apply(ApplicationTheme theme)
    {
        ApplicationThemeManager.Apply(theme, WindowBackdropType.None, updateAccent: false);

        var dark = theme == ApplicationTheme.Dark;
        if (dark)
            ApplicationAccentColorManager.Apply(Rgb(0xFF7100), Rgb(0xFF8A2E), Rgb(0xFF7100), Rgb(0xFFA060));
        else
            ApplicationAccentColorManager.Apply(Rgb(0xB34D00), Rgb(0xC25400), Rgb(0xB34D00), Rgb(0x9A4200));

        // Set at the root of the application's resources, they win over the theme dictionary just swapped.
        var resources = Application.Current.Resources;
        resources["EliteOrangeBrush"] = Brush(dark ? 0xFF8A1E : 0xB34D00);
        resources["ApplicationBackgroundBrush"] = Brush(dark ? 0x0A0A0B : 0xF3EFE9);
        resources["CardBackgroundFillColorDefaultBrush"] = Brush(dark ? 0x17130F : 0xFFFFFF);
        resources["CardStrokeColorDefaultBrush"] = Brush(dark ? 0x4A2C12 : 0xE2C9B0);
        resources["ContentControlThemeFontFamily"] = new FontFamily("Bahnschrift");
        resources["ControlCornerRadius"] = new CornerRadius(2);
    }

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static SolidColorBrush Brush(int rgb)
    {
        var brush = new SolidColorBrush(Rgb(rgb));
        brush.Freeze();
        return brush;
    }
}
