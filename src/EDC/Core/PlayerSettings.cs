using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using EDC.Audio;

namespace EDC.Core;

/// <summary>
/// Every setting of the player, in settings.json of the user's data folder (<see cref="AppPaths"/>).
/// </summary>
public sealed class PlayerSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>0 to 100.</summary>
    public int Volume { get; set; } = 40;

    /// <summary>Back to calm music this long after the last combat signal.</summary>
    public int CalmDelaySeconds { get; set; } = 120;

    public double FadeSeconds { get; set; } = 3;

    /// <summary>How many times a liked track enters a round, against once for the others.</summary>
    public int LikeWeight { get; set; } = 2;

    /// <summary>The mode forced from the window, kept from one launch to the next.</summary>
    public ForceMode Force { get; set; } = ForceMode.Auto;

    /// <summary>The power the hunt looks for: "auto" the game's, "none", or one of <see cref="Companion.HuntFinder.Powers"/>.</summary>
    public string Power { get; set; } = "auto";

    /// <summary>"auto" (as Windows), "en" or "fr"; applied on the next start.</summary>
    public string Language { get; set; } = "auto";

    /// <summary>The track's title over the game when it starts.</summary>
    public bool TrackToast { get; set; } = true;

    public double TrackToastSeconds { get; set; } = 5;

    /// <summary>The companion's band at the top of the game's screen, and its warnings.</summary>
    public bool CompanionBanner { get; set; } = true;

    /// <summary>The panel over the game listing the valuable planets and the material sources, here and on the route.</summary>
    public bool ExplorationPanel { get; set; } = true;

    /// <summary>The reminder to honk on entering a system not scanned yet.</summary>
    public bool HonkReminder { get; set; } = true;

    /// <summary>The wireless controller's battery in the band over the game.</summary>
    public bool ControllerInBanner { get; set; } = true;

    /// <summary>Below what mapping it well pays, in credits, a notable planet is left out of the panel.</summary>
    public long PanelMinValue { get; set; } = 700_000;

    /// <summary>The planets nobody had found, told whatever they pay.</summary>
    public bool PanelFirstDiscoveries { get; set; }

    /// <summary>The planets nobody had mapped, told whatever they pay.</summary>
    public bool PanelFirstMappings { get; set; }

    /// <summary>EDMarketConnector started with the game, when installed: it sends the journal to EDDN, EDSM and Inara.</summary>
    public bool LaunchEdmc { get; set; }

    /// <summary>Installs the new releases published on GitHub as soon as they are found.</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Where the player window was last, in WPF units; null until it has been closed once.</summary>
    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    /// <summary>The route last plotted or given by a Spansh link, and how far along: a long one spans several sessions.</summary>
    public Companion.NeutronRoute? NeutronRoute { get; set; }

    /// <summary>The next step copied on reaching one, ready to paste into the galaxy map.</summary>
    public bool NeutronAutoCopy { get; set; } = true;

    public static PlayerSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(path), Options) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            AppLog.Write($"réglages illisibles, valeurs par défaut : {ex.Message}");
        }

        return new();
    }

    public void Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"réglages non enregistrés : {ex.Message}");
        }
    }
}
