using System.Globalization;
using EDC.Resources;

namespace EDC.Core;

/// <summary>
/// The language of the player: the one of Windows, French or English, unless one is chosen in the
/// settings. Applied once at start; the texts come from Resources/Strings.resx and its translations.
/// </summary>
public static class Loc
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");

    /// <summary>The setting's values: "auto" follows Windows.</summary>
    public static readonly string[] Languages = ["auto", "en", "fr"];

    /// <summary>For the texts, and for the numbers: « 1 234,5 » in French, "1,234.5" in English.</summary>
    public static CultureInfo Culture { get; private set; } = English;

    public static CultureInfo Resolve(string? setting, CultureInfo windows) => setting switch
    {
        "en" => English,
        "fr" => French,
        _ => windows.TwoLetterISOLanguageName == "fr" ? French : English,
    };

    /// <summary>
    /// "{0} track" or "{0} tracks": English puts the plural from 0 on, French from 2 on.
    /// <paramref name="one"/> and <paramref name="many"/> are formats taking the count.
    /// </summary>
    public static string Plural(int count, string one, string many, CultureInfo? culture = null)
    {
        culture ??= Culture;
        var plural = culture.TwoLetterISOLanguageName == "fr" ? count >= 2 : count != 1;
        return string.Format(culture, plural ? many : one, count);
    }

    public static void Apply(string? setting)
    {
        Culture = Resolve(setting, CultureInfo.CurrentUICulture);
        Strings.Culture = Culture;
    }
}
