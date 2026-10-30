namespace EDC.Companion;

/// <summary>
/// What Vista Genomics pays for each species, without the first-footfall bonus, and how far apart the
/// samples of each genus must be. From EDMC-BioScan and EDMC-ExploData (github.com/Silarn).
/// </summary>
public static class Organics
{
    public static long? Value(string? species) => species is not null && Values.TryGetValue(species, out var value) ? value : null;

    /// <summary>
    /// What a genus pays, from its cheapest species to its dearest: the surface scanner names the genera,
    /// the species only show on sampling. Brain trees are « Brancae » as a genus, « Seed » as species.
    /// </summary>
    public static (long Min, long Max)? GenusValues(string? genus)
    {
        if (genus is null)
            return null;
        var prefix = genus.Equals("$Codex_Ent_Brancae_Name;", StringComparison.OrdinalIgnoreCase) ? "$Codex_Ent_Seed"
            : genus.EndsWith("_Genus_Name;", StringComparison.OrdinalIgnoreCase) ? genus[..^"Genus_Name;".Length]
            : genus.EndsWith("_Name;", StringComparison.OrdinalIgnoreCase) ? genus[..^"_Name;".Length]
            : genus;
        var values = Values.Where(v => v.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(v => v.Value).ToList();
        return values.Count > 0 ? (values.Min(), values.Max()) : null;
    }

    /// <summary>The colony range, in metres: the next sample must be taken further than that from the others.</summary>
    public static int? Range(string? genus) => genus is not null && Ranges.TryGetValue(genus, out var range) ? range : null;

    private static readonly Dictionary<string, int> Ranges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["$Codex_Ent_Aleoids_Genus_Name;"] = 150,
        ["$Codex_Ent_Bacterial_Genus_Name;"] = 500,
        ["$Codex_Ent_Cactoid_Genus_Name;"] = 300,
        ["$Codex_Ent_Clypeus_Genus_Name;"] = 150,
        ["$Codex_Ent_Conchas_Genus_Name;"] = 150,
        ["$Codex_Ent_Cone_Name;"] = 100,
        ["$Codex_Ent_Electricae_Genus_Name;"] = 1000,
        ["$Codex_Ent_Fonticulus_Genus_Name;"] = 500,
        ["$Codex_Ent_Fumerolas_Genus_Name;"] = 100,
        ["$Codex_Ent_Fungoids_Genus_Name;"] = 300,
        ["$Codex_Ent_Ground_Struct_Ice_Name;"] = 100,
        ["$Codex_Ent_Osseus_Genus_Name;"] = 800,
        ["$Codex_Ent_Recepta_Genus_Name;"] = 150,
        ["$Codex_Ent_Brancae_Name;"] = 100,
        ["$Codex_Ent_Shrubs_Genus_Name;"] = 150,
        ["$Codex_Ent_Sphere_Name;"] = 100,
        ["$Codex_Ent_Stratum_Genus_Name;"] = 500,
        ["$Codex_Ent_Tube_Name;"] = 100,
        ["$Codex_Ent_Tubus_Genus_Name;"] = 800,
        ["$Codex_Ent_Tussocks_Genus_Name;"] = 200,
        ["$Codex_Ent_Vents_Name;"] = 100,
        ["$Codex_Ent_Ingensradices_Genus_Name;"] = 15,
    };

    private static readonly Dictionary<string, long> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        ["$Codex_Ent_Aleoids_01_Name;"] = 7_252_500, // Aleoida Arcus
        ["$Codex_Ent_Aleoids_02_Name;"] = 6_284_600, // Aleoida Coronamus
        ["$Codex_Ent_Aleoids_05_Name;"] = 12_934_900, // Aleoida Gravis
        ["$Codex_Ent_Aleoids_04_Name;"] = 3_385_200, // Aleoida Laminiae
        ["$Codex_Ent_Aleoids_03_Name;"] = 3_385_200, // Aleoida Spica
        ["$Codex_Ent_Vents_Name;"] = 1_628_800, // Amphora Plant
        ["$Codex_Ent_SeedEFGH_01_Name;"] = 1_593_700, // Aureum Brain Tree
        ["$Codex_Ent_Bacterial_04_Name;"] = 1_000_000, // Bacterium Acies
        ["$Codex_Ent_Bacterial_06_Name;"] = 1_658_500, // Bacterium Alcyoneum
        ["$Codex_Ent_Bacterial_01_Name;"] = 1_000_000, // Bacterium Aurasus
        ["$Codex_Ent_Bacterial_10_Name;"] = 1_152_500, // Bacterium Bullaris
        ["$Codex_Ent_Bacterial_12_Name;"] = 1_689_800, // Bacterium Cerbrus
        ["$Codex_Ent_Bacterial_08_Name;"] = 8_418_000, // Bacterium Informem
        ["$Codex_Ent_Bacterial_02_Name;"] = 5_289_900, // Bacterium Nebulus
        ["$Codex_Ent_Bacterial_11_Name;"] = 4_638_900, // Bacterium Omentum
        ["$Codex_Ent_Bacterial_03_Name;"] = 4_934_500, // Bacterium Scopulum
        ["$Codex_Ent_Bacterial_07_Name;"] = 1_949_000, // Bacterium Tela
        ["$Codex_Ent_Bacterial_13_Name;"] = 3_897_000, // Bacterium Verrata
        ["$Codex_Ent_Bacterial_05_Name;"] = 1_000_000, // Bacterium Vesicula
        ["$Codex_Ent_Bacterial_09_Name;"] = 7_774_700, // Bacterium Volu
        ["$Codex_Ent_Cone_Name;"] = 1_471_900, // Bark Mound
        ["$Codex_Ent_SphereEFGH_Name;"] = 1_499_900, // Blatteum Bioluminescent Anemone
        ["$Codex_Ent_TubeEFGH_Name;"] = 1_514_500, // Blatteum Sinuous Tubers
        ["$Codex_Ent_Cactoid_01_Name;"] = 3_667_600, // Cactoida Cortexum
        ["$Codex_Ent_Cactoid_02_Name;"] = 2_483_600, // Cactoida Lapis
        ["$Codex_Ent_Cactoid_05_Name;"] = 2_483_600, // Cactoida Peperatis
        ["$Codex_Ent_Cactoid_04_Name;"] = 3_667_600, // Cactoida Pullulanta
        ["$Codex_Ent_Cactoid_03_Name;"] = 16_202_800, // Cactoida Vermis
        ["$Codex_Ent_TubeABCD_03_Name;"] = 1_514_500, // Caeruleum Sinuous Tubers
        ["$Codex_Ent_Clypeus_01_Name;"] = 8_418_000, // Clypeus Lacrimam
        ["$Codex_Ent_Clypeus_02_Name;"] = 11_873_200, // Clypeus Margaritus
        ["$Codex_Ent_Clypeus_03_Name;"] = 16_202_800, // Clypeus Speculumi
        ["$Codex_Ent_Conchas_02_Name;"] = 7_774_700, // Concha Aureolas
        ["$Codex_Ent_Conchas_04_Name;"] = 16_777_215, // Concha Biconcavis
        ["$Codex_Ent_Conchas_03_Name;"] = 2_352_400, // Concha Labiata
        ["$Codex_Ent_Conchas_01_Name;"] = 4_572_400, // Concha Renibus
        ["$Codex_Ent_SphereABCD_01_Name;"] = 1_499_900, // Croceum Anemone
        ["$Codex_Ent_Ground_Struct_Ice_Name;"] = 1_628_800, // Crystalline Shards
        ["$Codex_Ent_Electricae_01_Name;"] = 6_284_600, // Electricae Pluma
        ["$Codex_Ent_Electricae_02_Name;"] = 6_284_600, // Electricae Radialem
        ["$Codex_Ent_Fonticulus_02_Name;"] = 1_000_000, // Fonticulua Campestris
        ["$Codex_Ent_Fonticulus_06_Name;"] = 1_804_100, // Fonticulua Digitos
        ["$Codex_Ent_Fonticulus_05_Name;"] = 20_000_000, // Fonticulua Fluctus
        ["$Codex_Ent_Fonticulus_04_Name;"] = 3_111_000, // Fonticulua Lapida
        ["$Codex_Ent_Fonticulus_01_Name;"] = 19_010_800, // Fonticulua Segmentatus
        ["$Codex_Ent_Fonticulus_03_Name;"] = 5_727_600, // Fonticulua Upupam
        ["$Codex_Ent_Shrubs_02_Name;"] = 7_774_700, // Frutexa Acus
        ["$Codex_Ent_Shrubs_07_Name;"] = 1_639_800, // Frutexa Collum
        ["$Codex_Ent_Shrubs_05_Name;"] = 1_632_500, // Frutexa Fera
        ["$Codex_Ent_Shrubs_01_Name;"] = 1_808_900, // Frutexa Flabellum
        ["$Codex_Ent_Shrubs_04_Name;"] = 10_326_000, // Frutexa Flammasis
        ["$Codex_Ent_Shrubs_03_Name;"] = 1_632_500, // Frutexa Metallicum
        ["$Codex_Ent_Shrubs_06_Name;"] = 5_988_000, // Frutexa Sponsae
        ["$Codex_Ent_Fumerolas_04_Name;"] = 6_284_600, // Fumerola Aquatis
        ["$Codex_Ent_Fumerolas_01_Name;"] = 6_284_600, // Fumerola Carbosis
        ["$Codex_Ent_Fumerolas_02_Name;"] = 16_202_800, // Fumerola Extremus
        ["$Codex_Ent_Fumerolas_03_Name;"] = 7_500_900, // Fumerola Nitris
        ["$Codex_Ent_Fungoids_03_Name;"] = 3_703_200, // Fungoida Bullarum
        ["$Codex_Ent_Fungoids_04_Name;"] = 3_330_300, // Fungoida Gelata
        ["$Codex_Ent_Fungoids_01_Name;"] = 1_670_100, // Fungoida Setisis
        ["$Codex_Ent_Fungoids_02_Name;"] = 2_680_300, // Fungoida Stabitis
        ["$Codex_Ent_SeedABCD_01_Name;"] = 1_593_700, // Gypseeum Brain Tree
        ["$Codex_Ent_SeedEFGH_03_Name;"] = 1_593_700, // Lindigoticum Brain Tree
        ["$Codex_Ent_TubeEFGH_01_Name;"] = 1_514_500, // Lindigoticum Sinuous Tubers
        ["$Codex_Ent_SeedEFGH_Name;"] = 1_593_700, // Lividum Brain Tree
        ["$Codex_Ent_Sphere_Name;"] = 1_499_900, // Luteolum Anemone
        ["$Codex_Ent_Osseus_05_Name;"] = 1_483_000, // Osseus Cornibus
        ["$Codex_Ent_Osseus_02_Name;"] = 12_934_900, // Osseus Discus
        ["$Codex_Ent_Osseus_01_Name;"] = 4_027_800, // Osseus Fractus
        ["$Codex_Ent_Osseus_06_Name;"] = 9_739_000, // Osseus Pellebantus
        ["$Codex_Ent_Osseus_04_Name;"] = 3_156_300, // Osseus Pumice
        ["$Codex_Ent_Osseus_03_Name;"] = 2_404_700, // Osseus Spiralis
        ["$Codex_Ent_SeedABCD_02_Name;"] = 1_593_700, // Ostrinum Brain Tree
        ["$Codex_Ent_SphereEFGH_02_Name;"] = 1_499_900, // Prasinum Bioluminescent Anemone
        ["$Codex_Ent_TubeABCD_01_Name;"] = 1_514_500, // Prasinum Sinuous Tubers
        ["$Codex_Ent_SphereABCD_02_Name;"] = 1_499_900, // Puniceum Anemone
        ["$Codex_Ent_SeedEFGH_02_Name;"] = 1_593_700, // Puniceum Brain Tree
        ["$Codex_Ent_Ingensradices_Unicus_Name;"] = 119_037, // Radicoida Unicus
        ["$Codex_Ent_Recepta_03_Name;"] = 14_313_700, // Recepta Conditivus
        ["$Codex_Ent_Recepta_02_Name;"] = 16_202_800, // Recepta Deltahedronix
        ["$Codex_Ent_Recepta_01_Name;"] = 12_934_900, // Recepta Umbrux
        ["$Codex_Ent_SphereABCD_03_Name;"] = 1_499_900, // Roseum Anemone
        ["$Codex_Ent_SphereEFGH_03_Name;"] = 1_499_900, // Roseum Bioluminescent Anemone
        ["$Codex_Ent_Seed_Name;"] = 1_593_700, // Roseum Brain Tree
        ["$Codex_Ent_Tube_Name;"] = 1_514_500, // Roseum Sinuous Tubers
        ["$Codex_Ent_SphereEFGH_01_Name;"] = 1_499_900, // Rubeum Bioluminescent Anemone
        ["$Codex_Ent_Stratum_04_Name;"] = 2_448_900, // Stratum Araneamus
        ["$Codex_Ent_Stratum_06_Name;"] = 16_202_800, // Stratum Cucumisis
        ["$Codex_Ent_Stratum_01_Name;"] = 2_448_900, // Stratum Excutitus
        ["$Codex_Ent_Stratum_08_Name;"] = 2_637_500, // Stratum Frigus
        ["$Codex_Ent_Stratum_03_Name;"] = 2_788_300, // Stratum Laminamus
        ["$Codex_Ent_Stratum_05_Name;"] = 1_362_000, // Stratum Limaxus
        ["$Codex_Ent_Stratum_02_Name;"] = 1_362_000, // Stratum Paleas
        ["$Codex_Ent_Stratum_07_Name;"] = 19_010_800, // Stratum Tectonicas
        ["$Codex_Ent_Tubus_03_Name;"] = 11_873_200, // Tubus Cavas
        ["$Codex_Ent_Tubus_05_Name;"] = 7_774_700, // Tubus Compagibus
        ["$Codex_Ent_Tubus_01_Name;"] = 2_415_500, // Tubus Conifer
        ["$Codex_Ent_Tubus_04_Name;"] = 2_637_500, // Tubus Rosarium
        ["$Codex_Ent_Tubus_02_Name;"] = 5_727_600, // Tubus Sororibus
        ["$Codex_Ent_Tussocks_08_Name;"] = 3_252_500, // Tussock Albata
        ["$Codex_Ent_Tussocks_15_Name;"] = 7_025_800, // Tussock Capillum
        ["$Codex_Ent_Tussocks_11_Name;"] = 3_472_400, // Tussock Caputus
        ["$Codex_Ent_Tussocks_05_Name;"] = 1_766_600, // Tussock Catena
        ["$Codex_Ent_Tussocks_04_Name;"] = 1_766_600, // Tussock Cultro
        ["$Codex_Ent_Tussocks_10_Name;"] = 1_766_600, // Tussock Divisa
        ["$Codex_Ent_Tussocks_03_Name;"] = 1_849_000, // Tussock Ignis
        ["$Codex_Ent_Tussocks_01_Name;"] = 5_853_800, // Tussock Pennata
        ["$Codex_Ent_Tussocks_06_Name;"] = 1_000_000, // Tussock Pennatis
        ["$Codex_Ent_Tussocks_09_Name;"] = 1_000_000, // Tussock Propagito
        ["$Codex_Ent_Tussocks_07_Name;"] = 4_447_100, // Tussock Serrati
        ["$Codex_Ent_Tussocks_13_Name;"] = 19_010_800, // Tussock Stigmasis
        ["$Codex_Ent_Tussocks_12_Name;"] = 7_774_700, // Tussock Triticum
        ["$Codex_Ent_Tussocks_02_Name;"] = 3_227_700, // Tussock Ventusa
        ["$Codex_Ent_Tussocks_14_Name;"] = 14_313_700, // Tussock Virgam
        ["$Codex_Ent_TubeEFGH_02_Name;"] = 1_514_500, // Violaceum Sinuous Tubers
        ["$Codex_Ent_SeedABCD_03_Name;"] = 1_593_700, // Viride Brain Tree
        ["$Codex_Ent_TubeEFGH_03_Name;"] = 1_514_500, // Viride Sinuous Tubers
    };
}
