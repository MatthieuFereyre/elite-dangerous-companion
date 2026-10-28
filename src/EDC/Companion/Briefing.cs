using System.Globalization;
using EDC.Core;
using EDC.Resources;

namespace EDC.Companion;

public enum Tone { Info, Good, Warn, Alert, Muted }

/// <summary>
/// One piece of the banner: a short label, and a longer line for the window. <paramref name="Loud"/>
/// puts it on a filled band, for what must not be missed.
/// </summary>
public sealed record Chip(string Text, Tone Tone, string? Detail = null, bool Loud = false);

/// <summary>
/// The warning in the middle of the screen, for what calls for a reaction now. <paramref name="Big"/>
/// while it is new; then smaller, if it lasts. <paramref name="SeeThrough"/> in a fight, where the enemy
/// must stay visible behind it.
/// </summary>
public sealed record Warning(string Title, string Detail, bool Big = true, Tone Tone = Tone.Alert, bool SeeThrough = false);

/// <summary>
/// A line of the panel on the left: a system's name as <paramref name="Header"/>, or one of its planets.
/// <paramref name="Loud"/> when it holds a bonus to take, a first discovery or a first mapping. <paramref name="Targeted"/>
/// for the planet the ship targets, in the game's orange.
/// </summary>
public sealed record PanelLine(string Text, Tone Tone, bool Header = false, bool Loud = false, bool Targeted = false);

/// <summary>
/// What the companion says, from what it knows: the banner over the game and the reminders at the
/// station. Pure, so the tests can build any situation.
/// </summary>
public static class Briefing
{

    /// <summary>Below this, the rate is too noisy to mean anything.</summary>
    public static readonly TimeSpan RateMinimum = TimeSpan.FromMinutes(10);

    /// <summary>Whether the banner shows at all: in the ship, the SRV or on foot, outside the maps and full-screen panels.</summary>
    public static bool BannerVisible(GameStatus status) =>
        !status.InMenu && (status.InMainShip || status.InSrv || status.OnFoot) && !status.FullScreenView;

    /// <summary>Whether the panel on the left shows: where the banner does, and on the system and galaxy maps.</summary>
    public static bool PanelVisible(GameStatus status) =>
        !status.InMenu && (status.InMainShip || status.InSrv || status.OnFoot) && (!status.FullScreenView || status.SystemMap || status.GalaxyMap);

    /// <summary>The banner, most important first: what does not fit on the right is left out.</summary>
    public static List<Chip> Banner(GameState state, GameStatus status, PadBattery pad, DateTime nowUtc)
    {
        var chips = new List<Chip>();
        chips.AddRange(Alerts(state, status));

        if (status.OnFoot)
        {
            // In a station the ship stays docked: its reminders wait for the return aboard.
            chips.AddRange(OnFoot(state, status));
            if (Organic(state) is { } organic)
                chips.Add(organic);
            chips.AddRange(Earned(state));
        }
        else if (status.Docked)
        {
            var reminders = Reminders(state, status);
            chips.AddRange(reminders);
            if (reminders.Count == 0)
                chips.Add(new Chip(Strings.BReady, Tone.Good));
        }
        else
        {
            chips.AddRange(Flight(state, status));
            if (Pickup(state, nowUtc) is { } pickup)
                chips.Add(pickup);
            if (Target(state) is { } target)
                chips.Add(target);
            if (Organic(state) is { } organic)
                chips.Add(organic);
            chips.AddRange(Earned(state));
            if (HighGrade(state, nowUtc) is { } signals)
                chips.Add(signals);
        }

        if (Rate(state, nowUtc) is { } rate)
            chips.Add(new Chip(string.Format(Strings.BRate, Credits(rate)), Tone.Info));
        if (ExplorationRate(state, nowUtc) is { } exploring)
            chips.Add(new Chip(string.Format(Strings.BExploRate, Credits(exploring)), Tone.Info, Strings.BExploRateTip));
        if (pad.Link == PadLink.Wireless)
            chips.Add(new Chip(string.Format(Strings.BPad, string.Format(Strings.Percent, pad.Percent)), pad.Percent <= BatteryAlerts.Low ? Tone.Alert : Tone.Muted));

        return chips;
    }

    /// <summary>What is earned and not yet cashed in, and lost on dying: bounties, combat bonds, species analysed.</summary>
    private static List<Chip> Earned(GameState state)
    {
        var chips = new List<Chip>();
        if (state.BountiesPending > 0)
            chips.Add(state.Rebuy > 0 && state.BountiesPending > state.Rebuy
                ? new Chip(string.Format(Strings.BBounties, Credits(state.BountiesPending)), Tone.Alert, string.Format(Strings.BBountiesOverRebuy, Credits(state.Rebuy)))
                : new Chip(string.Format(Strings.BBounties, Credits(state.BountiesPending)), Tone.Good));
        if (state.BondsPendingCount > 0)
            chips.Add(new Chip(string.Format(Strings.BBonds, Credits(state.BondsPending)), Tone.Good, string.Format(Strings.BBondsTip, Bonds(state), string.Join(", ", state.BondFactions))));
        if (OrganicUnsold(state) is { } organic)
            chips.Add(organic);
        if (ExplorationUnsold(state) is { } exploration)
            chips.Add(exploration);
        return chips;
    }

    private static string Bonds(GameState state) =>
        Loc.Plural(state.BondsPendingCount, Strings.BBondsOne, Strings.BBondsMany);

    /// <summary>Whatever the situation: what could cost the ship.</summary>
    public static List<Chip> Alerts(GameState state, GameStatus status)
    {
        var chips = new List<Chip>();
        if (status.BeingInterdicted)
            chips.Add(new Chip(Strings.BInterdiction, Tone.Alert, Strings.BInterdictionTip, Loud: true));
        if (status.Overheating)
            chips.Add(new Chip(Strings.BOverheat, Tone.Alert, Strings.BOverheatTip, Loud: true));
        if (ShieldsDown(state, status))
            chips.Add(new Chip(Strings.BShieldsDown, Tone.Alert, string.Format(Strings.BHull, Percent(state.HullHealth)), Loud: true));

        var credits = status.Balance ?? state.Credits;
        if (state.Rebuy > 0 && credits < state.Rebuy)
            chips.Add(new Chip(Strings.BRebuyUncovered, Tone.Alert, string.Format(Strings.BRebuyTip, Credits(state.Rebuy), Credits(credits))));

        var legal = status.LegalState switch
        {
            "Wanted" => Strings.BWanted,
            "Warrant" => Strings.BWarrant,
            "Hostile" => Strings.BHostile,
            "IllegalCargo" => Strings.BIllegalCargo,
            "Speeding" => Strings.BSpeeding,
            "PassengerWanted" => Strings.BPassengerWanted,
            _ => null,
        };
        if (legal is not null)
            chips.Add(new Chip(legal, Tone.Alert));

        return chips;
    }

    /// <summary>
    /// At the station, before undocking: what not to forget. Without <paramref name="sales"/>, what there
    /// is to cash in or to sell is left to the tabs that show it (<see cref="CombatSales"/>, <see cref="ExplorationSales"/>).
    /// </summary>
    public static List<Chip> Reminders(GameState state, GameStatus status, bool sales = true)
    {
        var chips = new List<Chip>();

        if (state.HasLimpetController)
        {
            var usual = state.LimpetsUsual;
            if (state.Limpets == 0)
                chips.Add(new Chip(Strings.BNoLimpets, Tone.Warn, usual > 0 ? string.Format(Strings.BUsualLimpets, usual) : Strings.BNoLimpetsTip));
            else if (usual > 0 && state.Limpets < usual)
                chips.Add(new Chip(string.Format(Strings.BLimpets, state.Limpets, usual), Tone.Warn, string.Format(Strings.BLimpetsTip, usual - state.Limpets)));
        }

        // Out of the ship (SRV, on foot), Status.json gives another tank.
        if (status.InMainShip && status.FuelMain is { } fuel && state.FuelCapacity > 0 && !Full(fuel, state.FuelCapacity))
            chips.Add(new Chip(string.Format(Strings.BRefuel, Percent(fuel / state.FuelCapacity)), Tone.Warn, Strings.BRefuelTip));

        if (state.HullHealth < 0.995)
            chips.Add(new Chip(string.Format(Strings.BHull, Percent(state.HullHealth)), Tone.Warn, Strings.BRepairTip));

        if (sales)
        {
            chips.AddRange(CombatSales(state));
            chips.AddRange(ExplorationSales(state));
        }

        if (state.AmmoUsed && state.AmmoKinds.Count > 0)
        {
            var kinds = string.Join(", ", state.AmmoKinds);
            chips.Add(new Chip(Strings.BRearm, Tone.Warn, string.Format(Strings.BRearmTip, $"{char.ToUpper(kinds[0])}{kinds[1..]}")));
        }

        // Out there, the bag is all the suit has: the locker restocks it, the station's shop the locker.
        if (state.OnFootMissions > 0 && Missing(c => state.Packed(c) + state.Stored(c)) is { Count: > 0 } missing)
            chips.Add(new Chip(string.Format(Strings.BOnFootMission, string.Join($" {Strings.BNor} ", missing.Select(m => m.Name))), Tone.Warn, Strings.BPioneerAtStation));

        return chips;
    }

    // ------------------------------------------------------------------------------------- on foot

    // A property, not a field: the texts are read in the language applied at start.
    private static (string Symbol, string Name, string Use)[] Consumables =>
    [
        ("healthpack", Strings.BMedkit, Strings.BHeal),
        ("energycell", Strings.BEnergyCell, Strings.BRecharge),
    ];

    /// <summary>The consumables of which <paramref name="count"/> finds none.</summary>
    private static List<(string Symbol, string Name, string Use)> Missing(Func<string, int> count) =>
        Consumables.Where(c => count(c.Symbol) <= 0).ToList();

    /// <summary>Health or oxygen below this: the warning, small; below <see cref="Critical"/>, big.</summary>
    private const double Hurt = 0.5;
    private const double Critical = 0.25;

    /// <summary>On foot: what hurts the suit, then what the bag holds to mend it.</summary>
    public static List<Chip> OnFoot(GameState state, GameStatus status)
    {
        var chips = new List<Chip>();
        if (status.VeryCold)
            chips.Add(new Chip(Strings.BVeryCold, Tone.Alert, Strings.BSuitDamage));
        if (status.VeryHot)
            chips.Add(new Chip(Strings.BVeryHot, Tone.Alert, Strings.BSuitDamage));
        if (status.Health is < Hurt and var health)
            chips.Add(new Chip(string.Format(Strings.BHealth, Percent(health)), Tone.Alert, Medkits(state)));
        if (status.Oxygen is < Hurt and var oxygen)
            chips.Add(new Chip(string.Format(Strings.BOxygen, Percent(oxygen)), Tone.Alert, Cells(state)));

        int medkits = state.Packed("healthpack"), cells = state.Packed("energycell");
        chips.Add(medkits > 0 && cells > 0
            ? new Chip(string.Format(Strings.BKit, medkits, cells), Tone.Good)
            : new Chip(string.Format(Strings.BKit, medkits, cells), Tone.Warn, Strings.BKitTip));
        return chips;
    }

    private static string Medkits(GameState state) => state.Packed("healthpack") is > 0 and var n
        ? Loc.Plural(n, Strings.BMedkitsOne, Strings.BMedkitsMany)
        : Strings.BNoMedkit;

    /// <summary>The air runs low once the battery is flat: life support stops, the emergency reserve takes over.</summary>
    private static string Cells(GameState state) => state.Packed("energycell") is > 0 and var n
        ? Loc.Plural(n, Strings.BCellsOne, Strings.BCellsMany)
        : Strings.BNoCell;

    /// <summary>
    /// On foot: health, then air, big when critical; and for a moment after leaving the ship on a planet,
    /// a bag with nothing to heal or recharge. See-through: it may be a fight.
    /// </summary>
    private static Warning? OnFootWarning(GameState state, GameStatus status, DateTime nowUtc)
    {
        var health = status.Health is < Hurt and var h ? new Warning(string.Format(Strings.BHealth, Percent(h)), Medkits(state), h < Critical, SeeThrough: true) : null;
        if (health is { Big: true })
            return health;

        var air = status.Oxygen is < Hurt and var o ? new Warning(string.Format(Strings.BOxygen, Percent(o)), Cells(state), o < Critical, SeeThrough: true) : null;
        if (air is { Big: true })
            return air;

        if (state.DisembarkedOnPlanet && state.DisembarkedUtc is { } left && nowUtc - left < WarningBig
            && Missing(state.Packed) is { Count: > 0 } missing)
            return new Warning(string.Format(Strings.BBagWithout, string.Join($" {Strings.BNor} ", missing.Select(m => m.Name))),
                string.Format(Strings.BNothingFor, string.Join($" {Strings.BNorFor} ", missing.Select(m => m.Use))), Tone: Tone.Warn, SeeThrough: true);

        return health ?? air;
    }

    // ------------------------------------------------------------------------------------- exobiology

    /// <summary>The species in the sampler: how far along, what it pays, how far the next sample must be.</summary>
    public static Chip? Organic(GameState state)
    {
        if (state.Organic is not { } scan)
            return null;

        var text = $"{scan.Name} {scan.Samples}/3" + (scan.Value is { } value ? $" · {Credits(value)}" : "");
        return new Chip(text, Tone.Info, scan.Range is { } range ? string.Format(Strings.BNextSample, range) : null);
    }

    private static Chip? ExplorationUnsold(GameState state)
    {
        if (state.ExplorationUnsold == 0)
            return null;

        return new Chip(string.Format(Strings.BExploration, Credits(state.ExplorationUnsold)), Tone.Good,
            Loc.Plural(state.ExplorationUnsoldCount, Strings.BExplorationTipOne, Strings.BExplorationTipMany));
    }

    private static Chip? OrganicUnsold(GameState state)
    {
        var count = state.OrganicUnsoldCount;
        if (count == 0)
            return null;

        return new Chip(state.OrganicUnsold > 0 ? string.Format(Strings.BExobio, Credits(state.OrganicUnsold)) : Strings.BExobioUnknown, Tone.Good,
            Loc.Plural(count, Strings.BExobioTipOne, Strings.BExobioTipMany));
    }

    /// <summary>
    /// The route: the FSD supercharged or not, where it goes, how many jumps, how long the next one is
    /// and whether its star refuels. The loud chips first.
    /// </summary>
    public static List<Chip> Flight(GameState state, GameStatus status)
    {
        var chips = new List<Chip>();
        if (state.Supercharge is { } boost)
            chips.Add(new Chip(string.Format(Strings.BSupercharged, boost.ToString("0.#", Loc.Culture)), Tone.Good,
                string.Format(Strings.BSuperchargedTip, boost.ToString("0.#", Loc.Culture)), Loud: true));

        var next = state.JumpsLeft > 0 ? state.NextHop : null;
        var jump = next is null ? null : Jump(state, next);
        if (jump is { Loud: true })
            chips.Add(jump);
        if (next is { StarClass: "N" })
            chips.Add(Neutron(state, status, next));

        if (state.Destination is { } destination)
        {
            var jumps = state.JumpsLeft;
            chips.Add(new Chip(string.Format(Strings.RouteTo, destination.System, Loc.Plural(jumps, Strings.JumpsOne, Strings.JumpsMany)), Tone.Info));
        }

        if (next is not null)
        {
            // A neutron star is already said, loud.
            if (next.StarClass != "N")
                chips.Add(next.Scoopable ? new Chip(string.Format(Strings.BNextScoopable, Stars.Label(next.StarClass)), Tone.Good, next.System)
                    : Stars.Hazard(next.StarClass) is { } hazard ? new Chip(string.Format(Strings.BNextHazard, hazard), Tone.Warn, string.Format(Strings.BNotScoopableDetail, next.System))
                    : new Chip(string.Format(Strings.BNextNotScoopable, Stars.Label(next.StarClass)), Tone.Warn, next.System));

            if (jump is { Loud: false })
                chips.Add(jump);

            // In the SRV, Status.json gives the SRV's tank: no word on the ship's.
            if (status.InMainShip && FuelShortfall(state, status) is { } fuel)
                chips.Add(fuel);
        }
        else if (status.InMainShip && status.LowFuel)
        {
            chips.Add(new Chip(Strings.BFuelLow, Tone.Alert));
        }

        return chips;
    }

    /// <summary>
    /// The next jump's length against the FSD's range. The range is the Loadout's, the most the FSD
    /// can do (empty hold, fuel for one jump): a jump just under it may still fail with a full tank.
    /// </summary>
    public static Chip? Jump(GameState state, RouteHop next)
    {
        if (state.Position is not { } here || next.Position is not { } there || state.MaxJumpRange <= 0)
            return null;

        var distance = here.DistanceTo(there);
        var range = state.MaxJumpRange * (state.Supercharge ?? 1);
        var text = string.Format(Strings.BJump, Ly(distance), Ly(range));
        if (distance <= range)
            return new Chip(text, Tone.Muted, string.Format(Strings.BRangeTip, Ly(range)));

        // Out of the plain range, within a neutron star's push.
        if (state.Supercharge is null && state.CurrentHop is { StarClass: "N" } && distance <= state.MaxJumpRange * 4)
            return new Chip(string.Format(Strings.BBoostNeeded, text), Tone.Alert, Strings.BBoostNeededTip, Loud: true);

        return new Chip(string.Format(Strings.BOutOfRange, text), Tone.Alert, string.Format(Strings.BOutOfRangeTip, next.System), Loud: true);
    }

    /// <summary>
    /// A neutron star next: no fuel there, and often more neutron stars after it. First of the route
    /// and loud, so the tank gets filled while the star here still can.
    /// </summary>
    public static Chip Neutron(GameState state, GameStatus status, RouteHop next)
    {
        var text = Strings.BNeutronNext;
        var detail = string.Format(Strings.BNotScoopableDetail, next.System);

        // In the SRV, Status.json gives the SRV's tank.
        if (!status.InMainShip || status.FuelMain is not { } fuel || state.FuelCapacity <= 0)
            return new Chip(text, Tone.Warn, detail, Loud: true);

        if (Full(fuel, state.FuelCapacity))
            return new Chip(string.Format(Strings.BTankFilled, text), Tone.Warn, detail, Loud: true);

        var tank = Percent(fuel / state.FuelCapacity);
        return state.CurrentHop is { Scoopable: true }
            ? new Chip(string.Format(Strings.BRefuelHere, text), Tone.Alert, string.Format(Strings.BRefuelHereTip, detail, tank), Loud: true)
            : new Chip(string.Format(Strings.BTank, text, tank), Tone.Alert, detail, Loud: true);
    }

    /// <summary>
    /// The fuel left counted in jumps like the last one, against the jumps until a star that refuels:
    /// a warning only when it may not be enough.
    /// </summary>
    public static Chip? FuelShortfall(GameState state, GameStatus status)
    {
        if (status.FuelMain is null || state.LastJumpFuel is not > 0)
            return status.LowFuel ? new Chip(Strings.BFuelLow, Tone.Alert) : null;

        if (DryStretch(state, status) is not var (reach, needed))
            return status.LowFuel ? new Chip(Strings.BFuelLowScoop, Tone.Warn) : null;
        if (reach >= needed)
            return null;

        return new Chip(string.Format(Strings.BFuelJumps, Loc.Plural(reach, Strings.JumpsOne, Strings.JumpsMany)), Tone.Alert,
            string.Format(Strings.BFuelJumpsTip, needed));
    }

    public static Chip? Target(GameState state)
    {
        if (state.Target is not { } target)
            return null;

        var chip = target.ScanStage < 3 ? new Chip(string.Format(Strings.BTargetScanning, target.Ship), Tone.Muted)
            : target.Wanted ? new Chip(string.Format(Strings.BTarget, target.Ship, Credits(target.Bounty)), Tone.Good, string.Format(Strings.BTargetWantedTip, target.Pilot, Rank(target.Rank), target.Faction))
            : new Chip(string.Format(Strings.BTargetClean, target.Ship), Tone.Muted, Strings.BTargetCleanTip);

        // The rank comes at scan stage 1, before the bounty.
        var ratio = Threat.Ratio(state, target);
        var against = string.Format(Strings.BAgainst, target.Ship, Rank(target.Rank), state.ShipType, ratio?.ToString("0.0", Loc.Culture));
        return Threat.Of(ratio) switch
        {
            Danger.Deadly => new Chip(string.Format(Strings.BDanger, chip.Text), Tone.Alert, string.Format(Strings.BAvoid, against), Loud: true),
            Danger.Tough => new Chip(string.Format(Strings.BTough, chip.Text), Tone.Warn, string.Format(Strings.BCareful, against)),
            _ => chip,
        };
    }

    /// <summary>
    /// Through the stars that do not refuel and on to the one that does, or to the end of the route: the
    /// jumps it takes, and those the tank holds, counted like the longest of the last ones.
    /// </summary>
    private static (int Reach, int Needed)? DryStretch(GameState state, GameStatus status)
    {
        if (status.FuelMain is not { } fuel || state.LastJumpFuel is not { } perJump || perJump <= 0)
            return null;

        var ahead = state.HopsAhead;
        var dry = ahead.TakeWhile(h => !h.Scoopable).Count();
        if (dry == 0)
            return null;

        return ((int)Math.Floor(fuel / perJump), dry < ahead.Count ? dry + 1 : dry);
    }

    /// <summary>
    /// The star here refuels, the next does not, and the tank keeps less than a jump to spare until a
    /// star that does: the time to fill it is now, before leaving. Not once filled here, nor with the
    /// jump under way.
    /// </summary>
    public static Warning? FillHere(GameState state, GameStatus status, DateTime nowUtc)
    {
        if (state.FilledHere || status.FsdCharging || status.FsdJump || state.CurrentHop is not { Scoopable: true } || status.FuelMain is not { } fuel || state.FuelCapacity <= 0
            || Full(fuel, state.FuelCapacity) || DryStretch(state, status) is not var (reach, needed) || reach > needed)
            return null;

        var big = !status.ScoopingFuel && state.ArrivedUtc is { } arrived && nowUtc - arrived < WarningBig;
        return new Warning(Strings.BFillHere,
            string.Format(Strings.BFillHereTip, Percent(fuel / state.FuelCapacity), Loc.Plural(reach, Strings.JumpsOne, Strings.JumpsMany), needed),
            big, Tone.Warn);
    }

    /// <summary>The systems of the route the panel names at most: it must not run down the screen.</summary>
    private const int PanelSystems = 8;

    /// <summary>
    /// The panel on the left, in flight: the sources of materials here, every notable planet here, then a line for
    /// each next system of the route Spansh knows a notable planet in. Empty when there is none; the planets
    /// left out in a ship without a surface scanner.
    /// </summary>
    public static List<PanelLine> Panel(GameState state, GameStatus status, DateTime nowUtc)
    {
        var lines = new List<PanelLine>();
        if (status.GalaxyMap)
            return GalaxyTarget(state);
        if (status.OnFoot || status.Docked)
            return lines;

        // The sources of materials, as soon as the journal tells them; the high grade emissions stand out.
        var signals = state.MaterialSignals(nowUtc);
        if (signals.Count > 0)
        {
            lines.Add(new PanelLine(string.Format(Strings.BMaterials, signals.Count), Tone.Info, Header: true));
            lines.AddRange(signals.Select(s =>
                new PanelLine(s.Name + (s.HighGrade ? " · grade 5" : "") + (s.ExpiresUtc is { } end ? $" · {(int)(end - nowUtc).TotalMinutes} min" : ""),
                    Tone.Good, Loud: s.HighGrade)));
        }

        if (!state.HasSurfaceScanner)
            return lines;

        static string Bonus(bool firstDiscovery, bool firstMapping) => (firstDiscovery, firstMapping) switch
        {
            (true, true) => Strings.BFirstBoth,
            (true, false) => Strings.BFirstDiscovery,
            (false, true) => Strings.BFirstMapping,
            _ => "",
        };

        // Here, the planets worth the trip, as the system map lists them; the others are not told, bonus or
        // not. A body with a bonus to take stands out: filled, like the band's loud chips.
        // In the order that takes the least supercruise from where the ship is; without its time, which the
        // FSD's engineering, or an overcharged one, puts beyond any estimate.
        var here = state.VisitPlan(nowUtc)
            .Select(p => (p.Body.Name, p.Body.Label, p.Body.Distance, p.Body.Value,
                Bonus: Bonus(p.Body.FirstDiscovery, state.ScannedHere(p.Body.Name) is { FirstMapping: true })))
            .ToList();
        if (here.Count > 0)
        {
            lines.Add(new PanelLine(string.Format(Strings.BHere, here.Count), Tone.Info, Header: true));
            lines.AddRange(here.Select(b =>
            {
                var targeted = string.Equals(b.Name, status.Destination, StringComparison.OrdinalIgnoreCase);
                return new PanelLine($"{(targeted ? "› " : "")}{b.Label} · {ShortName(b.Name, state.System)} · {b.Distance.ToString("N0", Loc.Culture)} ls · {Worth(b.Value)}{b.Bonus}",
                    targeted ? Tone.Info : Tone.Good, Loud: b.Bonus != "", Targeted: targeted);
            }));
        }

        // The route ahead, a line a system with how many planets to scan there, filled when one is a first discovery. The system map shows this system: the route would only hide it.
        var route = state.RouteNotable.Take(status.SystemMap ? 0 : PanelSystems).ToList();
        if (route.Count > 0)
            lines.Add(new PanelLine(Strings.BOnRoute, Tone.Info, Header: true));
        foreach (var (jumps, hop, bodies) in route)
        {
            lines.Add(new PanelLine($"{Loc.Plural(jumps, Strings.JumpsOne, Strings.JumpsMany)} · {hop.System} ({bodies.Count}) · {Worth(bodies)}", Tone.Good, Loud: bodies.Any(b => b.FirstDiscovery)));
        }

        return lines;
    }

    /// <summary>On the galaxy map, what EDSM knows of the system targeted, and its planets worth the trip.</summary>
    private static List<PanelLine> GalaxyTarget(GameState state)
    {
        if (state.TargetSystem is not { } target || state.TargetSurvey is not { } survey)
            return [];

        var lines = new List<PanelLine>
        {
            new(string.Format(Strings.BTargetSystem, target.System), Tone.Info, Header: true),
            survey.Kind switch
            {
                SurveyKind.Undiscovered => new PanelLine(Strings.BUndiscovered, Tone.Good, Loud: true),
                SurveyKind.Complete => new PanelLine(string.Format(Strings.BFullyScanned, survey.Known), Tone.Info),
                _ when survey.Count is { } count => new PanelLine(string.Format(Strings.BKnownInPart, survey.Known, count), Tone.Good),
                _ => new PanelLine(string.Format(Strings.BKnownInPartNoCount, survey.Known), Tone.Good),
            },
        };
        var planets = state.WorthTelling(survey.Planets);
        if (planets.Count > 0)
            lines.Add(new PanelLine($"{Loc.Plural(planets.Count, Strings.PlanetsOne, Strings.PlanetsMany)} · {Worth(planets)}", Tone.Good));
        return lines;
    }

    /// <summary>What mapping a planet pays, or that nobody knows.</summary>
    private static string Worth(long? value) => value is { } v ? Credits(v) : Strings.BValueUnknown;

    /// <summary>What mapping the planets of a system pays, « + » when some are not known.</summary>
    private static string Worth(IReadOnlyList<NotableBody> bodies)
    {
        var known = bodies.Where(b => b.Value is not null).Sum(b => b.Value!.Value);
        return bodies.All(b => b.Value is null) ? Strings.BValueUnknown
            : bodies.Any(b => b.Value is null) ? Credits(known) + " +" : Credits(known);
    }

    /// <summary>« A 5 » for « LHS 3447 A 5 », as the system map writes it.</summary>
    private static string ShortName(string body, string? system) =>
        system is not null && body.StartsWith(system + " ", StringComparison.OrdinalIgnoreCase) ? body[(system.Length + 1)..] : body;

    /// <summary>How long a warning stays big: the moment it starts, or the target is locked.</summary>
    public static readonly TimeSpan WarningBig = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The warning in the middle of the screen: shields fallen, big at first and again at each hit, smaller
    /// while they stay down; the hull hit; a newly locked target far too strong for the ship; the tank to
    /// fill before a stretch of stars that do not refuel; last, the discovery scanner to sound. In flight only.
    /// </summary>
    public static Warning? Warning(GameState state, GameStatus status, DateTime nowUtc, bool honk = true)
    {
        if (status.OnFoot)
            return OnFootWarning(state, status, nowUtc);
        if (!status.InMainShip || status.Docked || status.Landed)
            return null;

        var hull = string.Format(Strings.BHull, Percent(state.HullHealth));
        bool Recent(DateTime? at) => at is { } time && nowUtc - time < WarningBig;

        var shields = ShieldsDown(state, status) && state.ShieldsDownAtUtc is { } down
            ? new Warning(Strings.BShieldsDown, string.Format(Strings.BSince, hull, (int)(nowUtc - down).TotalSeconds),
                Recent(down) || Recent(state.LastHullHitUtc) || Recent(state.LastAttackUtc), SeeThrough: true)
            : null;
        if (shields is { Big: true })
            return shields;

        if (shields is null && Recent(state.LastHullHitUtc) && state.HullHealth < 0.6)
            return new Warning(hull, Strings.BHullHit, SeeThrough: true);

        if (state.Target is { } target && target.LegalStatus != "Clean" && Recent(state.TargetSinceUtc)
            && Threat.Ratio(state, target) is { } ratio && Threat.Of(ratio) == Danger.Deadly)
            return new Warning(Strings.BTargetTooDangerous,
                string.Format(Strings.BThreatFor, target.Ship, Rank(target.Rank), ratio.ToString("0.0", Loc.Culture), state.ShipType), SeeThrough: true);

        var fill = FillHere(state, status, nowUtc);
        if (fill is { Big: true })
            return fill;
        return shields ?? fill ?? LeavingValuable(state, status) ?? (honk ? Honk(state, status, nowUtc) : null);
    }

    /// <summary>
    /// The jump charging while a valuable planet here is still to map: the time to cancel it. Not in the jump, too
    /// late, and where Status.json still says it for a moment after arriving, with the planets of the new system.
    /// </summary>
    private static Warning? LeavingValuable(GameState state, GameStatus status)
    {
        if (!status.HyperdriveCharging || status.FsdJump || state.NotableBodies is not { Count: > 0 } left)
            return null;

        var names = string.Join(", ", left.Select(b => ShortName(b.Name, state.System)));
        return new Warning(Strings.BLeavingValuable, string.Format(Strings.BLeavingValuableTip, names, Credits(left.Sum(b => b.Value ?? 0))), Big: true, Tone.Warn);
    }

    /// <summary>
    /// A system the discovery scanner has not sounded in, or whose data went down with the ship: still
    /// while the FSD charges, the last moment to think of it; not in the jump.
    /// </summary>
    private static Warning? Honk(GameState state, GameStatus status, DateTime nowUtc)
    {
        if (state.Honked || state.System is null || status.FsdJump)
            return null;

        var big = state.ArrivedUtc is { } arrived && nowUtc - arrived < WarningBig;
        return new Warning(Strings.BHonk, Strings.BHonkTip, big, Tone.Info);
    }

    /// <summary>At the station the game drops and raises the shields as modules change: in flight only.</summary>
    private static bool ShieldsDown(GameState state, GameStatus status) =>
        !state.ShieldsUp && status.InMainShip && !status.Docked && !status.Landed;

    /// <summary>How long a picked up material stays on the banner.</summary>
    public static readonly TimeSpan PickupShown = TimeSpan.FromSeconds(30);

    /// <summary>The material just picked up and where its count stands against the cap: at the cap, more is lost.</summary>
    public static Chip? Pickup(GameState state, DateTime nowUtc)
    {
        if (state.LastPickup is not { } pickup || nowUtc - pickup.AtUtc > PickupShown)
            return null;

        if (pickup.Cap is not { } cap)
            return new Chip($"{pickup.Name} {pickup.Count}", Tone.Good);

        var grade = string.Format(Strings.BGrade, Materials.Grade(pickup.Symbol));
        return pickup.Full
            ? new Chip(string.Format(Strings.BPickupFull, pickup.Name, pickup.Count, cap), Tone.Alert, string.Format(Strings.BPickupFullTip, grade))
            : new Chip($"{pickup.Name} {pickup.Count}/{cap}", Tone.Good, grade);
    }

    public static Chip? HighGrade(GameState state, DateTime nowUtc)
    {
        var signals = state.HighGradeSignals(nowUtc);
        if (signals.Count == 0)
            return null;

        var left = signals.Max(s => s.ExpiresUtc!.Value) - nowUtc;
        return new Chip(signals.Count > 1 ? string.Format(Strings.BHgeMany, signals.Count) : Strings.BHge, Tone.Good,
            string.Format(Strings.BHgeTip, (int)left.TotalMinutes));
    }

    /// <summary>Credits earned per hour this session; null until it means something.</summary>
    /// <summary>The bounties and the combat bonds not cashed in yet, lost on dying.</summary>
    public static List<Chip> CombatSales(GameState state)
    {
        var chips = new List<Chip>();
        if (state.BountiesPending > 0)
            chips.Add(new Chip(string.Format(Strings.BCashIn, Credits(state.BountiesPending)), Tone.Good,
                string.Format(Strings.BCashInTip, state.BountiesPendingCount)));
        if (state.BondsPendingCount > 0)
            chips.Add(new Chip(string.Format(Strings.BCashInBonds, Credits(state.BondsPending)), Tone.Good, string.Format(Strings.BLostIfDestroyed, Bonds(state))));
        return chips;
    }

    /// <summary>What fighting pays by the hour this session: the bounties and the combat bonds earned since the game was loaded.</summary>
    public static long? CombatRate(GameState state, DateTime nowUtc)
    {
        var earned = state.SessionBounties + state.SessionBonds;
        if (state.SessionStartUtc is not { } start || earned <= 0)
            return null;

        var elapsed = nowUtc - start;
        return elapsed < RateMinimum ? null : (long)(earned / elapsed.TotalHours);
    }

    /// <summary>The exploration data and the species not sold yet, lost on dying.</summary>
    public static List<Chip> ExplorationSales(GameState state)
    {
        var chips = new List<Chip>();
        if (OrganicUnsold(state) is { } organic)
            chips.Add(organic);
        if (ExplorationUnsold(state) is { } exploration)
            chips.Add(exploration);
        return chips;
    }

    /// <summary>What exploring pays by the hour this session: the bodies scanned since the game was loaded, sold or not.</summary>
    public static long? ExplorationRate(GameState state, DateTime nowUtc)
    {
        if (state.SessionStartUtc is not { } start || state.ExplorationSession <= 0)
            return null;

        var elapsed = nowUtc - start;
        return elapsed < RateMinimum ? null : (long)(state.ExplorationSession / elapsed.TotalHours);
    }

    public static long? Rate(GameState state, DateTime nowUtc)
    {
        if (state.SessionStartUtc is not { } start || state.SessionEarnings <= 0)
            return null;

        var elapsed = nowUtc - start;
        return elapsed < RateMinimum ? null : (long)(state.SessionEarnings / elapsed.TotalHours);
    }

    public static string Credits(long amount) => amount switch
    {
        >= 1_000_000_000 => string.Format(Strings.CreditsBillion, (amount / 1e9).ToString("0.0", Loc.Culture)),
        >= 1_000_000 => string.Format(Strings.CreditsMillion, (amount / 1e6).ToString("0.0", Loc.Culture)),
        >= 10_000 => string.Format(Strings.CreditsThousand, (amount / 1e3).ToString("0", Loc.Culture)),
        _ => string.Format(Strings.CreditsUnit, amount.ToString("N0", Loc.Culture)),
    };

    // 0.29 × 100 gives 28.999…: a hair more, so that it reads 29.
    private static string Percent(double fraction) => string.Format(Strings.Percent, (int)Math.Floor(fraction * 100 + 1e-9));

    private static bool Full(double fuel, double capacity) => fuel >= capacity * 0.99;

    private static string Ly(double lightYears) => lightYears.ToString("0.#", Loc.Culture);

    private static string Rank(string? rank) => rank switch
    {
        "Harmless" => Strings.RankHarmless,
        "Mostly Harmless" => Strings.RankMostlyHarmless,
        "Novice" => Strings.RankNovice,
        "Competent" => Strings.RankCompetent,
        "Expert" => Strings.RankExpert,
        "Master" => Strings.RankMaster,
        "Dangerous" => Strings.RankDangerous,
        "Deadly" => Strings.RankDeadly,
        "Elite" => Strings.RankElite,
        null => Strings.RankUnknown,
        _ => rank,
    };
}
