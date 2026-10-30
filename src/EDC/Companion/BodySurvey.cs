using System.Text.Json;
using EDC.Resources;

namespace EDC.Companion;

/// <summary>
/// What a journal scan says of a planet beyond its worth, for the surface scanner's view: its gravity in g,
/// its temperature in kelvins, its air (empty without, « thin … » when thin) and its materials in percent.
/// </summary>
public sealed record BodyDetails(bool Landable, double? Gravity, double? Temperature, string Atmosphere, IReadOnlyList<(string Symbol, double Percent)> Materials)
{
    /// <summary>The journal's surface gravity is in m/s².</summary>
    private const double StandardGravity = 9.80665;

    public static BodyDetails? FromScan(JsonElement e)
    {
        if (GameState.Str(e, "PlanetClass") is null)
            return null;

        var materials = e.TryGetProperty("Materials", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
                .Select(m => (Symbol: GameState.Str(m, "Name") ?? "", Percent: GameState.Dbl(m, "Percent") ?? 0))
                .Where(m => m.Symbol != "")
                .ToList()
            : [];
        return new BodyDetails(GameState.Bool(e, "Landable") == true, GameState.Dbl(e, "SurfaceGravity") / StandardGravity,
            GameState.Dbl(e, "SurfaceTemperature"), GameState.Str(e, "Atmosphere") ?? "", materials);
    }

    /// <summary>The rarer materials, grade 3 and up, the rarest first: those an engineer asks for.</summary>
    public IEnumerable<(string Name, double Percent)> RareMaterials(int count) =>
        Materials
            .Select(m => (m.Percent, Grade: Companion.Materials.Grade(m.Symbol) ?? 0, Name: RawName(m.Symbol)))
            .Where(m => m.Grade >= 3 && m.Name is not null)
            .OrderByDescending(m => m.Grade).ThenByDescending(m => m.Percent)
            .Take(count)
            .Select(m => (m.Name!, m.Percent));

    // The raws of grade 3 and 4 a planet holds, in the window's language: the journal names them in the game's.
    private static string? RawName(string symbol) => symbol.ToLowerInvariant() switch
    {
        "boron" => Strings.RawBoron,
        "cadmium" => Strings.RawCadmium,
        "mercury" => Strings.RawMercury,
        "molybdenum" => Strings.RawMolybdenum,
        "niobium" => Strings.RawNiobium,
        "tin" => Strings.RawTin,
        "tungsten" => Strings.RawTungsten,
        "antimony" => Strings.RawAntimony,
        "polonium" => Strings.RawPolonium,
        "ruthenium" => Strings.RawRuthenium,
        "selenium" => Strings.RawSelenium,
        "technetium" => Strings.RawTechnetium,
        "tellurium" => Strings.RawTellurium,
        "yttrium" => Strings.RawYttrium,
        _ => null,
    };
}

/// <summary>
/// A planet's signals: counted by the FSS (FSSBodySignals), then the genera of its life named by the
/// surface scanner (SAASignalsFound), each by the Codex's id and the game's name for it.
/// </summary>
public sealed record BodySignals(int Biological, int Geological, IReadOnlyList<(string Id, string Name)> Genera)
{
    public static BodySignals FromEvent(JsonElement e)
    {
        int Count(string type) => e.TryGetProperty("Signals", out var signals) && signals.ValueKind == JsonValueKind.Array
            ? signals.EnumerateArray().Where(s => GameState.Str(s, "Type") == type).Sum(s => (int)(GameState.Long(s, "Count") ?? 0))
            : 0;

        var genera = e.TryGetProperty("Genuses", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
                .Select(g => (Id: GameState.Str(g, "Genus") ?? "", Name: GameState.Str(g, "Genus_Localised") ?? GameState.Str(g, "Genus") ?? ""))
                .Where(g => g.Id != "")
                .ToList()
            : [];
        return new BodySignals(Count("$SAA_SignalType_Biological;"), Count("$SAA_SignalType_Geological;"), genera);
    }
}
