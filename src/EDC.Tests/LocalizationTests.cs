using System.Globalization;
using System.IO;
using System.Resources;
using System.Text.RegularExpressions;
using EDC.Core;
using EDC.Resources;
using Xunit;

namespace EDC.Tests;

public sealed partial class LocalizationTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");

    private static ResourceManager Manager => Strings.ResourceManager;

    private static IEnumerable<string> Keys() =>
        Manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!
            .Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key);

    [Fact]
    public void Every_text_has_its_french_translation_with_the_same_placeholders()
    {
        var french = Manager.GetResourceSet(French, createIfNotExists: true, tryParents: false)!;
        Assert.NotEmpty(Keys());
        foreach (var key in Keys())
        {
            var translated = french.GetString(key);
            Assert.False(string.IsNullOrWhiteSpace(translated), $"{key} has no French translation");
            Assert.Equal(Placeholders(Manager.GetString(key, English)!), Placeholders(translated!));
        }
    }

    [Fact]
    public void Every_text_of_the_resources_has_its_property_and_no_more()
    {
        var properties = typeof(Strings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string)).Select(p => p.Name).Order();

        Assert.Equal(Keys().Order(), properties);
    }

    private static string[] Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Value).Distinct().Order().ToArray();

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex Placeholder();

    [Theory]
    [InlineData("auto", "fr-FR", "fr")]
    [InlineData("auto", "fr-CA", "fr")]
    [InlineData("auto", "de-DE", "en")]
    [InlineData("auto", "en-US", "en")]
    [InlineData("en", "fr-FR", "en")]
    [InlineData("fr", "en-US", "fr")]
    [InlineData(null, "fr-FR", "fr")]
    public void The_language_follows_windows_unless_one_is_chosen(string? setting, string windows, string expected)
    {
        Assert.Equal(expected, Loc.Resolve(setting, CultureInfo.GetCultureInfo(windows)).TwoLetterISOLanguageName);
    }

    [Theory]
    [InlineData("fr", 0, "0 morceau")]
    [InlineData("fr", 1, "1 morceau")]
    [InlineData("fr", 2, "2 morceaux")]
    [InlineData("en", 0, "0 tracks")]
    [InlineData("en", 1, "1 track")]
    [InlineData("en", 2, "2 tracks")]
    public void A_count_takes_the_plural_of_its_language(string language, int count, string expected)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        Assert.Equal(expected, Loc.Plural(count, Manager.GetString(nameof(Strings.TracksOne), culture)!, Manager.GetString(nameof(Strings.TracksMany), culture)!, culture));
    }

    [Fact]
    public void The_same_text_reads_in_both_languages()
    {
        Assert.Equal("Next", Manager.GetString(nameof(Strings.Next), English));
        Assert.Equal("Suivant", Manager.GetString(nameof(Strings.Next), French));
    }

    /// <summary>A text typed in a window instead of taken from the resources would stay French in English.</summary>
    [Fact]
    public void The_windows_hold_no_text_of_their_own()
    {
        var views = Path.Combine(SourceFolder(), "Views");
        var literal = new Regex(@"\b(Text|Content|ToolTip|Header|PlaceholderText|Title)=""(?!\{)[^""]*\p{L}[^""]*""");
        var found = Directory.GetFiles(views, "*.xaml")
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => literal.IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();

        Assert.Empty(found);
    }

    /// <summary>An icon misspelt would only fail when the window opens.</summary>
    [Fact]
    public void Every_icon_of_the_windows_exists()
    {
        var views = Path.Combine(SourceFolder(), "Views");
        var symbol = new Regex(@"\bSymbol=""(?<name>[A-Za-z0-9]+)""");
        var unknown = Directory.GetFiles(views, "*.xaml")
            .SelectMany(f => symbol.Matches(File.ReadAllText(f)).Select(m => m.Groups["name"].Value))
            .Distinct()
            .Where(name => !Enum.IsDefined(typeof(Wpf.Ui.Controls.SymbolRegular), name))
            .ToList();

        Assert.Empty(unknown);
    }

    private static string SourceFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EDC.sln")))
                return Path.Combine(dir.FullName, "EDC");
        throw new DirectoryNotFoundException("EDC.sln");
    }
}
