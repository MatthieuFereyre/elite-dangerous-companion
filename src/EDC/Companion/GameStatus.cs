using System.Text.Json;

namespace EDC.Companion;

/// <summary>Status.json, which the game rewrites several times a second: where the commander is, right now.</summary>
/// <remarks>
/// On foot, <paramref name="Health"/> and <paramref name="Oxygen"/> run from 0 to 1, <paramref name="Temperature"/> is in kelvins.
/// <paramref name="Destination"/> is the name of what is targeted: a body, a station, a system.
/// </remarks>
public sealed record GameStatus(long Flags, long Flags2, int GuiFocus, double? FuelMain, long? Balance, string? LegalState,
    double? Health = null, double? Oxygen = null, double? Temperature = null, string? Destination = null)
{
    public static readonly GameStatus None = new(0, 0, 0, null, null, null);

    public bool Docked => Has(Flags, 0);
    public bool Landed => Has(Flags, 1);
    public bool Supercruise => Has(Flags, 4);
    public bool ScoopingFuel => Has(Flags, 11);
    public bool FsdCharging => Has(Flags, 17);
    public bool LowFuel => Has(Flags, 19);
    public bool Overheating => Has(Flags, 20);
    public bool BeingInterdicted => Has(Flags, 23);
    public bool InMainShip => Has(Flags, 24);
    public bool InFighter => Has(Flags, 25);
    public bool InSrv => Has(Flags, 26);
    public bool FsdJump => Has(Flags, 30);
    public bool OnFoot => Has(Flags2, 0);
    public bool OnFootInStation => Has(Flags2, 3);
    public bool OnFootOnPlanet => Has(Flags2, 4);
    public bool VeryCold => Has(Flags2, 10);
    public bool VeryHot => Has(Flags2, 11);

    /// <summary>The hyperdrive charging: a jump to another system is under way, still to be cancelled.</summary>
    public bool HyperdriveCharging => Has(Flags2, 19);

    /// <summary>Flags at zero: the main menu, or the game not yet in a ship.</summary>
    public bool InMenu => Flags == 0 && Flags2 == 0;

    /// <summary>A full-screen view where the banner would be in the way: maps, FSS, surface scanner, codex.</summary>
    public bool FullScreenView => GuiFocus is >= 6 and <= 11;

    /// <summary>The map of a system, where the panel stays to tell which planets to map.</summary>
    public bool SystemMap => GuiFocus == 7;
    public bool GalaxyMap => GuiFocus == 6;

    private static bool Has(long flags, int bit) => (flags & (1L << bit)) != 0;

    public static GameStatus? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var e = document.RootElement;
            if (e.ValueKind != JsonValueKind.Object)
                return null;

            double? fuel = null;
            if (e.TryGetProperty("Fuel", out var f) && f.ValueKind == JsonValueKind.Object)
                fuel = GameState.Dbl(f, "FuelMain");

            return new GameStatus(
                GameState.Long(e, "Flags") ?? 0,
                GameState.Long(e, "Flags2") ?? 0,
                (int)(GameState.Long(e, "GuiFocus") ?? 0),
                fuel,
                GameState.Long(e, "Balance"),
                GameState.Str(e, "LegalState"),
                GameState.Dbl(e, "Health"),
                GameState.Dbl(e, "Oxygen"),
                GameState.Dbl(e, "Temperature"),
                e.TryGetProperty("Destination", out var destination) && destination.ValueKind == JsonValueKind.Object
                    ? GameState.Str(destination, "Name")
                    : null);
        }
        catch (JsonException)
        {
            // Caught mid-write: the next read gets it whole.
            return null;
        }
    }
}
