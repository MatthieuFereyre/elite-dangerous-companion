namespace EDC.Companion;

/// <summary>The grade of each material, from EDCD/FDevIDs material.csv: the cap of the inventory follows it.</summary>
public static class Materials
{
    private static readonly Dictionary<string, int> Grades = new[]
    {
        "basicconductors bulkscandata carbon chemicalstorageunits compactcomposites crystalshards disruptedwakeechoes encryptedfiles gridresistors guardian_powercell guardian_sentinel_wreckagecomponents heatconductionwiring iron lead legacyfirmware mechanicalscrap nickel phosphorus rhenium salvagedalloys scrambledemissiondata shieldcyclerecordings sulphur temperedalloys wornshieldemitters",
        "archivedemissiondata arsenic chemicalprocessors chromium conductivecomponents consumerfirmware encryptioncodes filamentcomposites fsdtelemetry galvanisingalloys germanium guardian_powerconduit heatdispersionplate heatresistantceramics hybridcapacitors manganese mechanicalequipment scanarchives shieldemitters shieldsoakanalysis tg_structuraldata uncutfocuscrystals unknowncarapace vanadium zinc zirconium",
        "boron cadmium chemicaldistillery conductiveceramics electrochemicalarrays emissiondata focuscrystals guardian_sentinel_weaponparts guardian_techcomponent heatexchangers highdensitycomposites industrialfirmware mechanicalcomponents mercury molybdenum niobium phasealloys precipitatedalloys scandatabanks shielddensityreports shieldingsensors symmetrickeys tg_biomechanicalconduits tg_compositiondata tg_shipflightdata tg_wreckagecomponents tin tungsten unknownenergycell unknownshipsignature wakesolutions",
        "ancientbiologicaldata ancientculturaldata ancienthistoricaldata ancientlanguagedata ancienttechnologicaldata antimony chemicalmanipulators compoundshielding conductivepolymers configurablecomponents decodedemissiondata encodedscandata encryptionarchives fedproprietarycomposites guardian_moduleblueprint guardian_weaponblueprint heatvanes hyperspacetrajectories polonium polymercapacitors protolightalloys refinedfocuscrystals ruthenium securityfirmware selenium shieldpatternanalysis technetium tellurium tg_residuedata tg_shipsystemsdata tg_weaponparts thermicalloys unknowntechnologycomponents unknownwakedata yttrium",
        "adaptiveencryptors biotechconductors classifiedscandata compactemissionsdata dataminedwake embeddedfirmware exquisitefocuscrystals fedcorecomposites guardian_vesselblueprint imperialshielding improvisedcomponents militarygradealloys militarysupercapacitors pharmaceuticalisolators protoheatradiators protoradiolicalloys shieldfrequencydata tg_propulsionelement unknownenergysource unknownorganiccircuitry",
    }
    .SelectMany((symbols, index) => symbols.Split(' ').Select(symbol => (symbol, grade: index + 1)))
    .ToDictionary(m => m.symbol, m => m.grade, StringComparer.OrdinalIgnoreCase);

    public static int? Grade(string symbol) => Grades.TryGetValue(symbol, out var grade) ? grade : null;

    /// <summary>300 at grade 1, 50 fewer each grade up: 100 at grade 5.</summary>
    public static int? Cap(string symbol) => Grade(symbol) is { } grade ? 350 - 50 * grade : null;
}
