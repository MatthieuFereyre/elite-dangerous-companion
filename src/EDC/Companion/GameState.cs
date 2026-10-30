using System.Globalization;
using System.Text.Json;


namespace EDC.Companion;

/// <summary>Galactic coordinates, in light years.</summary>
public readonly record struct StarPos(double X, double Y, double Z)
{
    public double DistanceTo(StarPos other) =>
        Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y) + (Z - other.Z) * (Z - other.Z));
}

/// <summary>A system of the plotted route, from NavRoute.json.</summary>
public sealed record RouteHop(string System, long Address, string StarClass, StarPos? Position = null)
{
    public bool Scoopable => Stars.IsScoopable(StarClass);
}

/// <summary>The target as the last ShipTargeted gave it; the bounty is only known once scanned (stage 3).</summary>
public sealed record Target(string Ship, string? Pilot, string? Rank, string? LegalStatus, long Bounty, int ScanStage, string? Faction,
    string? ShipSymbol = null)
{
    public bool Wanted => LegalStatus is "Wanted" or "WantedEnemy";
}

/// <summary>A material picked up, with the count it brought the inventory to.</summary>
public sealed record MaterialPickup(string Symbol, string Name, int Count, DateTime AtUtc)
{
    public int? Cap => Materials.Cap(Symbol);
    public bool Full => Cap is { } cap && Count >= cap;
}

/// <summary>
/// A source of materials seen in the current system, with when it disappears: never for a salvageable
/// wreck, which stays. <paramref name="HighGrade"/> for high grade emissions, the grade 5 ones.
/// </summary>
public sealed record Signal(string Name, DateTime? ExpiresUtc, bool HighGrade = false);

/// <summary>A species in the genetic sampler: three samples, then the analysis.</summary>
public sealed record OrganicScan(string Species, string Genus, string Name, int Samples)
{
    public long? Value => Organics.Value(Species);
    public int? Range => Organics.Range(Genus);
}

/// <summary>
/// What the companion knows of the game, built from the journal lines in order. Pure: no file, no clock;
/// the times are the journal's. A replay of every journal rebuilds it, which is how it starts.
/// </summary>
public sealed class GameState
{
    private readonly Dictionary<string, long> _bountiesByFaction = new(StringComparer.Ordinal);
    private readonly Dictionary<long, int> _limpetsUsual = [];
    private readonly List<Signal> _signals = [];
    private readonly Queue<double> _jumpFuel = new();
    private readonly Dictionary<string, int> _materials = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _materialNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _backpack = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _locker = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _onFootMissions = [];
    private readonly Dictionary<string, (long Total, int Count)> _bondsByFaction = new(StringComparer.Ordinal);
    private readonly List<string> _organicUnsold = [];
    private readonly Dictionary<string, ScanValue> _explorationUnsold = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _mappedEfficient = new(StringComparer.OrdinalIgnoreCase);
    // The bodies scanned since the game was loaded, kept once sold: what this session's exploration is worth.
    private readonly Dictionary<string, ScanValue> _sessionScans = new(StringComparer.OrdinalIgnoreCase);
    // The systems honked and the bodies mapped, each with whether its data is sold: the rest goes down with the ship.
    private readonly Dictionary<string, bool> _honked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string System, bool Sold)> _mappedBodies = new(StringComparer.OrdinalIgnoreCase);
    // The current system's notable planets: the journal's scans, and EDSM's for those not scanned yet.
    private readonly Dictionary<string, NotableBody> _scannedNotable = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<NotableBody> _edsmNotable = [];
    // The current system's stars and planets the journal scanned, whatever their worth.
    private readonly Dictionary<string, ScannedBody> _scannedBodies = new(StringComparer.OrdinalIgnoreCase);
    // The current system's planets as the journal scanned them, and their signals: for the surface scanner's view.
    private readonly Dictionary<string, BodyDetails> _bodyDetails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BodySignals> _bodySignals = new(StringComparer.OrdinalIgnoreCase);
    // The route's systems Spansh knows a notable planet in, by address.
    private readonly Dictionary<long, IReadOnlyList<NotableBody>> _routeNotable = [];

    public string? Commander { get; private set; }
    /// <summary>The power the commander is pledged to, if any.</summary>
    public string? Power { get; private set; }
    public long ShipId { get; private set; } = -1;
    public string? ShipName { get; private set; }
    public string? ShipType { get; private set; }
    /// <summary>The journal's name of the ship ("python_nx"), which <see cref="Threat"/> weighs.</summary>
    public string? ShipSymbol { get; private set; }
    /// <summary>The engineering of the combat modules, each counted by its grade over 5.</summary>
    public double Engineering { get; private set; }
    public bool Armed { get; private set; } = true;

    public string? System { get; private set; }
    public long SystemAddress { get; private set; }
    public string? Station { get; private set; }
    public StarPos? Position { get; private set; }
    /// <summary>The last FSDJump: the moment to fill the tank, before the next.</summary>
    public DateTime? ArrivedUtc { get; private set; }
    /// <summary>The tank scooped full in this system: what supercruise burns after does not undo it.</summary>
    public bool FilledHere { get; private set; }

    public IReadOnlyList<RouteHop> Route { get; private set; } = [];

    /// <summary>The next jump as the game targets it: what is left when the current system is not on the route.</summary>
    public RouteHop? FsdTarget { get; private set; }
    public int? FsdTargetRemaining { get; private set; }
    /// <summary>The most fuel one of the last five jumps took: a short hop must not make the tank look bigger.</summary>
    public double? LastJumpFuel => _jumpFuel.Count > 0 ? _jumpFuel.Max() : null;

    /// <summary>The range factor a jet cone gave the FSD (4 for a neutron star), until the jump uses it.</summary>
    public double? Supercharge { get; private set; }

    public MaterialPickup? LastPickup { get; private set; }
    public int MaterialCount(string symbol) => _materials.GetValueOrDefault(symbol);

    public double HullHealth { get; private set; } = 1;
    /// <summary>The game says when the shields fall and when they are back, never how much is left.</summary>
    public bool ShieldsUp { get; private set; } = true;
    public DateTime? ShieldsDownAtUtc { get; private set; }
    public DateTime? LastHullHitUtc { get; private set; }
    /// <summary>The game writes it once a minute at most, however heavy the fire.</summary>
    public DateTime? LastAttackUtc { get; private set; }
    public int CargoCapacity { get; private set; }
    public double FuelCapacity { get; private set; }
    /// <summary>The Loadout's: empty hold and only the fuel for one jump, so the most the FSD can do.</summary>
    public double MaxJumpRange { get; private set; }
    public long Rebuy { get; private set; }
    public bool HasLimpetController { get; private set; }
    /// <summary>The ship can map a planet: without it, the planets to map are not its business. Assumed before the first Loadout.</summary>
    public bool HasSurfaceScanner { get; private set; } = true;
    public int Limpets { get; private set; }

    /// <summary>The most limpets this ship has carried: how many it usually leaves with.</summary>
    public int LimpetsUsual => _limpetsUsual.GetValueOrDefault(ShipId);

    public long Credits { get; private set; }

    public Target? Target { get; private set; }
    /// <summary>When the current target was locked: a new one, not the same one further scanned.</summary>
    public DateTime? TargetSinceUtc { get; private set; }

    /// <summary>Bounties earned and not yet cashed in; lost on dying.</summary>
    public long BountiesPending => _bountiesByFaction.Values.Sum();
    public int BountiesPendingCount { get; private set; }

    public DateTime? SessionStartUtc { get; private set; }
    public long SessionEarnings { get; private set; }
    public long SessionBounties { get; private set; }
    public long SessionBonds { get; private set; }
    public int SessionKills { get; private set; }

    /// <summary>Combat bonds earned and not yet cashed in; lost on dying.</summary>
    public long BondsPending => _bondsByFaction.Values.Sum(b => b.Total);
    public int BondsPendingCount => _bondsByFaction.Values.Sum(b => b.Count);
    public IReadOnlyCollection<string> BondFactions => _bondsByFaction.Keys;

    // ------------------------------------------------------------------------------------- on foot

    /// <summary>A consumable in the backpack ("healthpack", "energycell"): what the suit has out there.</summary>
    public int Packed(string consumable) => _backpack.GetValueOrDefault(consumable);

    /// <summary>A consumable left in the ship locker.</summary>
    public int Stored(string consumable) => _locker.GetValueOrDefault(consumable);

    /// <summary>The last time the commander left the ship, and whether on a planet: there, the bag is all there is.</summary>
    public DateTime? DisembarkedUtc { get; private set; }
    public bool DisembarkedOnPlanet { get; private set; }

    public int OnFootMissions => _onFootMissions.Count;

    /// <summary>The species in the genetic sampler, until analysed.</summary>
    public OrganicScan? Organic { get; private set; }

    /// <summary>The species analysed and not yet sold to Vista Genomics; lost on dying.</summary>
    public long OrganicUnsold => _organicUnsold.Sum(s => Organics.Value(s) ?? 0);
    public int OrganicUnsoldCount => _organicUnsold.Count;

    /// <summary>What the stars and planets scanned and not yet sold are worth, by the players' formula; lost on dying.</summary>
    public long ExplorationUnsold => _explorationUnsold.Sum(b =>
        ExplorationValues.Value(b.Value, _mappedEfficient.TryGetValue(b.Key, out var efficient), efficient));

    public int ExplorationUnsoldCount => _explorationUnsold.Count;

    /// <summary>What the bodies scanned since the game was loaded are worth, sold or not.</summary>
    public long ExplorationSession => _sessionScans.Sum(b =>
        ExplorationValues.Value(b.Value, _sessionMapped.Contains(b.Key), _sessionEfficient.Contains(b.Key)));

    public int ExplorationSessionCount => _sessionScans.Count;

    private readonly HashSet<string> _sessionMapped = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sessionEfficient = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The kinds of ammunition of the ship (missiles, mines…), as the Loadout lists them.</summary>
    public IReadOnlyList<string> AmmoKinds { get; private set; } = [];

    /// <summary>Some ammunition went since the last restock: a fight or mining since then.</summary>
    public bool AmmoUsed { get; private set; }

    /// <summary>A Backpack or ShipLocker event without its lists, to be read from Backpack.json or ShipLocker.json.</summary>
    public bool BackpackFileWanted { get; set; }
    public bool ShipLockerFileWanted { get; set; }

    /// <summary>The discovery scanner sounded here, and what it found not lost since with the ship.</summary>
    public bool Honked => System is { } system && _honked.ContainsKey(system);

    /// <summary>Raised on entering a new system: the caller asks EDSM and Spansh for its planets.</summary>
    public bool SystemBodiesWanted { get; set; }

    /// <summary>
    /// The planets of the current system not mapped yet that are worth telling: mapping them pays the
    /// threshold or more, or they are a first discovery or a first mapping when the player asked for those.
    /// The most precious first.
    /// </summary>
    public IReadOnlyList<NotableBody> NotableBodies =>
        // What Spansh said of it on the route, for a system EDSM does not know; the journal's scans over both.
        (_routeNotable.GetValueOrDefault(SystemAddress) ?? [])
            .Concat(_edsmNotable)
            .Where(b => !_scannedNotable.ContainsKey(b.Name))
            .DistinctBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .Concat(_scannedNotable.Values)
            .Where(b => !_mappedBodies.ContainsKey(b.Name) && Worth(b))
            .OrderByDescending(b => b.Value ?? -1)
            .ThenBy(b => b.Distance)
            .ToList();

    // A value not known is told: it may well pass the threshold.
    private bool Worth(NotableBody b) =>
        b.Value is null || b.Value >= MinNotableValue || (FirstDiscoveries && b.FirstDiscovery) || (FirstMappings && b.FirstMapping);

    /// <summary>Below what mapping it pays, a planet is not told, here nor on the route; the player's setting.</summary>
    public long MinNotableValue { get; set; }

    /// <summary>A planet nobody had found is told whatever it pays; the player's setting.</summary>
    public bool FirstDiscoveries { get; set; }

    /// <summary>A planet nobody had mapped is told whatever it pays; the player's setting.</summary>
    public bool FirstMappings { get; set; }

    /// <summary>EDSM's answer, asked with the value; one for a system left since is dropped.</summary>
    /// <summary>The bodies of the current system as scanned, placed on their orbits.</summary>
    public SystemOrbits Orbits { get; private set; } = new();

    /// <summary>The star or planet the ship last dropped at, or arrived near; null right after a jump, at the arrival star.</summary>
    public string? NearBody { get; private set; }

    /// <summary>
    /// The notable planets in the order that takes the least supercruise, from where the ship is, each
    /// with the time to reach it from the one before. Those the journal has not placed (known from EDSM
    /// or Spansh only, or of an unknown ancestry) come last, by distance, without a time.
    /// </summary>
    public IReadOnlyList<(NotableBody Body, double? Seconds)> VisitPlan(DateTime nowUtc)
    {
        var bodies = NotableBodies;
        var start = (NearBody ?? Orbits.ArrivalStar) is { } from ? Orbits.Position(from, nowUtc) : null;
        var placed = bodies.Select(b => (Body: b, At: start is null ? null : Orbits.Position(b.Name, nowUtc))).ToList();
        var known = placed.Where(p => p.At is not null).ToList();

        var plan = VisitPlanner.Order(start ?? default, known.Select(p => p.At!.Value).ToList())
            .Select(leg => (known[leg.Index].Body, (double?)leg.Seconds))
            .ToList();
        plan.AddRange(placed.Where(p => p.At is null).OrderBy(p => p.Body.Distance).Select(p => (p.Body, (double?)null)));
        return plan;
    }

    public void SetNotableBodies(long address, IReadOnlyList<NotableBody> bodies)
    {
        if (address == SystemAddress)
            _edsmNotable = bodies;
    }

    /// <summary>A star or planet of the current system as the journal scanned it, if it did.</summary>
    public ScannedBody? ScannedHere(string name) => _scannedBodies.GetValueOrDefault(name);

    /// <summary>A planet of the current system with its worth, whatever it is: the journal's scan, else EDSM's or Spansh's.</summary>
    public NotableBody? PlanetHere(string name) =>
        _scannedNotable.GetValueOrDefault(name)
        ?? _edsmNotable.Concat(_routeNotable.GetValueOrDefault(SystemAddress) ?? [])
            .FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A planet of the current system as the journal's scan describes it: landing, materials.</summary>
    public BodyDetails? DetailsHere(string name) => _bodyDetails.GetValueOrDefault(name);

    /// <summary>A planet's signals, counted by the FSS, its genera named once probed.</summary>
    public BodySignals? SignalsHere(string name) => _bodySignals.GetValueOrDefault(name);

    /// <summary>The planets of the current system mapped by the commander.</summary>
    public int MappedHere => _mappedBodies.Count(b => string.Equals(b.Value.System, System, StringComparison.OrdinalIgnoreCase));

    /// <summary>Raised by a new route: the caller asks Spansh for its systems and calls <see cref="SetRouteNotable"/>.</summary>
    public bool RouteNotableWanted { get; set; }

    /// <summary>Spansh's answer for the route: added to what it said of the routes before.</summary>
    public void SetRouteNotable(IReadOnlyDictionary<long, List<NotableBody>> bodies)
    {
        foreach (var (address, notable) in bodies)
            _routeNotable[address] = notable;
    }

    /// <summary>
    /// The hops ahead with a notable planet not mapped yet, the next first, each with how many jumps away
    /// and its planets, the most precious first.
    /// </summary>
    public IReadOnlyList<(int Jumps, RouteHop Hop, IReadOnlyList<NotableBody> Bodies)> RouteNotable =>
        HopsAhead
            .Select((hop, i) => (Jumps: i + 1, Hop: hop, Bodies: (IReadOnlyList<NotableBody>)(_routeNotable.GetValueOrDefault(hop.Address) ?? [])
                .Where(b => !_mappedBodies.ContainsKey(b.Name) && Worth(b))
                .OrderByDescending(b => b.Value ?? -1)
                .ThenBy(b => b.Distance)
                .ToList()))
            .Where(h => h.Bodies.Count > 0)
            .ToList();

    public IReadOnlyList<Signal> HighGradeSignals(DateTime nowUtc) => MaterialSignals(nowUtc).Where(s => s.HighGrade).ToList();

    /// <summary>The sources of materials of the current system not expired yet, in the order seen.</summary>
    public IReadOnlyList<Signal> MaterialSignals(DateTime nowUtc) => _signals.Where(s => s.ExpiresUtc is null || s.ExpiresUtc > nowUtc).ToList();

    /// <summary>Raised by a NavRoute event without its route: the caller reads NavRoute.json and calls <see cref="SetRoute"/>.</summary>
    public bool RouteFileWanted { get; set; }

    /// <summary>Same for a Cargo event without its inventory, to be read from Cargo.json.</summary>
    public bool CargoFileWanted { get; set; }

    // ------------------------------------------------------------------------------------- route

    /// <summary>Where the current system is in the route; -1 when it is not on it.</summary>
    public int RouteIndex => Route.ToList().FindIndex(h => h.Address == SystemAddress);

    /// <summary>The current system when it is on the route: its star is only known from there.</summary>
    public RouteHop? CurrentHop => RouteIndex is >= 0 and var index ? Route[index] : null;

    public RouteHop? NextHop
    {
        get
        {
            var index = RouteIndex;
            if (index >= 0)
                return index + 1 < Route.Count ? Route[index + 1] : null;
            return FsdTarget;
        }
    }

    /// <summary>Jumps left to the end of the route; 0 when there is none or it is done.</summary>
    public int JumpsLeft
    {
        get
        {
            var index = RouteIndex;
            if (index >= 0)
                return Route.Count - 1 - index;
            return Route.Count > 0 ? FsdTargetRemaining ?? Route.Count : 0;
        }
    }

    public RouteHop? Destination => JumpsLeft > 0 && Route.Count > 0 ? Route[^1] : null;

    private readonly Dictionary<RankKind, int> _ranks = [];
    private readonly Dictionary<RankKind, int> _rankProgress = [];
    private readonly Dictionary<string, double> _reputation = [];

    /// <summary>Each rank as the game gives it at its start, then by its promotions.</summary>
    public IReadOnlyDictionary<RankKind, int> Ranks => _ranks;

    /// <summary>The percent to the next rank.</summary>
    public IReadOnlyDictionary<RankKind, int> RankProgress => _rankProgress;

    /// <summary>The standing with each superpower, -100 to 100, by its journal name.</summary>
    public IReadOnlyDictionary<string, double> Reputation => _reputation;

    /// <summary>The system the galaxy map points at: the end of the route, or the system targeted without one.</summary>
    public RouteHop? TargetSystem => Destination ?? (FsdTarget is { } target && target.Address != SystemAddress ? target : null);

    // What EDSM said of each system targeted, null while it has not answered: asked once.
    private readonly Dictionary<long, SystemSurvey?> _surveys = [];

    /// <summary>The target not asked of EDSM yet: the caller calls <see cref="SurveyAsked"/>, then <see cref="SetSurvey"/>.</summary>
    public long? TargetSurveyWanted => TargetSystem is { Address: not 0 } target && !_surveys.ContainsKey(target.Address) ? target.Address : null;

    public void SurveyAsked(long address) => _surveys.TryAdd(address, null);

    public void SetSurvey(long address, SystemSurvey survey) => _surveys[address] = survey;

    public SystemSurvey? TargetSurvey => TargetSystem is { } target && _surveys.TryGetValue(target.Address, out var survey) ? survey : null;

    /// <summary>Those of <paramref name="planets"/> worth telling, as <see cref="NotableBodies"/> are.</summary>
    public IReadOnlyList<NotableBody> WorthTelling(IEnumerable<NotableBody> planets) => planets.Where(Worth).ToList();

    /// <summary>The hops still to make, the next first.</summary>
    public IReadOnlyList<RouteHop> HopsAhead
    {
        get
        {
            var index = RouteIndex;
            if (index >= 0)
                return Route.Skip(index + 1).ToList();
            return JumpsLeft > 0 ? Route.Skip(Math.Max(0, Route.Count - JumpsLeft)).ToList() : [];
        }
    }

    public void SetRoute(IReadOnlyList<RouteHop> route)
    {
        Route = route;
        RouteNotableWanted = route.Count > 0;
    }

    // ------------------------------------------------------------------------------------- reading

    public void Apply(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            var e = document.RootElement;
            if (e.ValueKind != JsonValueKind.Object || Str(e, "event") is not { } name)
                return;

            var time = Str(e, "timestamp") is { } stamp && DateTime.TryParse(stamp, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.UtcNow;
            Apply(name, e, time);
        }
    }

    private void Apply(string name, JsonElement e, DateTime time)
    {
        switch (name)
        {
            case "LoadGame":
                Commander = Str(e, "Commander");
                ShipId = Long(e, "ShipID") ?? ShipId;
                ShipName = Str(e, "ShipName");
                ShipType = Str(e, "Ship_Localised") ?? ShipTypeName(Str(e, "Ship"));
                ShipSymbol = Str(e, "Ship")?.ToLowerInvariant() ?? ShipSymbol;
                ShieldsUp = true;
                ShieldsDownAtUtc = null;
                Credits = Long(e, "Credits") ?? Credits;
                FuelCapacity = Dbl(e, "FuelCapacity") ?? FuelCapacity;
                SessionStartUtc = time;
                SessionEarnings = 0;
                _sessionScans.Clear();
                _sessionMapped.Clear();
                _sessionEfficient.Clear();
                SessionBounties = 0;
                SessionBonds = 0;
                SessionKills = 0;
                Target = null;
                break;

            case "Location":
            case "FSDJump":
            case "CarrierJump":
                EnterSystem(e);
                // After a jump the ship is at the arrival star; at a load, by the body the game names.
                NearBody = name == "Location" && Str(e, "BodyType") is "Planet" or "Star" ? Str(e, "Body") : null;
                if (name == "FSDJump" && Dbl(e, "FuelUsed") is > 0 and var used)
                {
                    _jumpFuel.Enqueue(used);
                    if (_jumpFuel.Count > 5)
                        _jumpFuel.Dequeue();
                }
                if (name == "Location")
                    Station = Bool(e, "Docked") == true ? Str(e, "StationName") : null;
                if (name == "FSDJump")
                {
                    Supercharge = null;
                    ArrivedUtc = time;
                }
                FilledHere = false;
                break;

            case "Powerplay":
            case "PowerplayJoin":
                Power = Str(e, "Power") ?? Power;
                break;
            case "PowerplayDefect":
                Power = Str(e, "ToPower") ?? Power;
                break;
            case "PowerplayLeave":
                Power = null;
                break;

            case "FuelScoop":
                if (Dbl(e, "Total") is { } tank && FuelCapacity > 0 && tank >= FuelCapacity * 0.99)
                    FilledHere = true;
                break;

            case "JetConeBoost":
                Supercharge = Dbl(e, "BoostValue");
                break;

            case "SupercruiseExit":
            case "SupercruiseEntry":
                if (Long(e, "SystemAddress") is { } address && address != SystemAddress)
                    EnterSystem(e);
                if (name == "SupercruiseExit" && Str(e, "BodyType") is "Planet" or "Star")
                    NearBody = Str(e, "Body");
                break;

            case "ApproachBody":
                NearBody = Str(e, "Body") ?? NearBody;
                break;

            case "ScanBaryCentre":
                if (Long(e, "SystemAddress") == SystemAddress)
                    Orbits.Add(e);
                break;

            case "NavRoute":
                if (e.TryGetProperty("Route", out var route) && route.ValueKind == JsonValueKind.Array)
                    SetRoute(ParseRoute(route));
                else
                    RouteFileWanted = true;
                break;

            case "NavRouteClear":
                // A replay reads NavRoute.json once at the end: a route cleared since must not come back.
                RouteFileWanted = false;
                RouteNotableWanted = false;
                Route = [];
                FsdTarget = null;
                FsdTargetRemaining = null;
                break;

            case "Rank":
            case "Progress":
            case "Promotion":
                foreach (var kind in Enum.GetValues<RankKind>())
                {
                    if (Long(e, kind == RankKind.Cqc ? "CQC" : kind.ToString()) is not { } level)
                        continue;
                    if (name == "Progress")
                        _rankProgress[kind] = (int)level;
                    else
                        _ranks[kind] = (int)level;
                    // A promotion starts the next rank from nothing.
                    if (name == "Promotion")
                        _rankProgress[kind] = 0;
                }
                break;

            case "Reputation":
                foreach (var power in new[] { "Federation", "Empire", "Alliance", "Independent" })
                    if (Dbl(e, power) is { } reputation)
                        _reputation[power] = reputation;
                break;

            case "FSDTarget":
                FsdTarget = new RouteHop(Str(e, "Name") ?? "?", Long(e, "SystemAddress") ?? 0, Str(e, "StarClass") ?? "");
                FsdTargetRemaining = (int?)Long(e, "RemainingJumpsInRoute");
                break;

            case "Docked":
                Station = Str(e, "StationName");
                break;

            case "Undocked":
                Station = null;
                break;

            case "Loadout":
                // Another ship comes out of the shipyard with its racks full.
                if (Long(e, "ShipID") is { } loaded && loaded != ShipId)
                    AmmoUsed = false;
                ShipId = Long(e, "ShipID") ?? ShipId;
                ShipName = Str(e, "ShipName") ?? ShipName;
                ShipType = Str(e, "Ship_Localised") ?? ShipTypeName(Str(e, "Ship")) ?? ShipType;
                HullHealth = Dbl(e, "HullHealth") ?? HullHealth;
                CargoCapacity = (int)(Long(e, "CargoCapacity") ?? 0);
                Rebuy = Long(e, "Rebuy") ?? Rebuy;
                MaxJumpRange = Dbl(e, "MaxJumpRange") ?? MaxJumpRange;
                if (e.TryGetProperty("FuelCapacity", out var fuel) && fuel.ValueKind == JsonValueKind.Object)
                    FuelCapacity = Dbl(fuel, "Main") ?? FuelCapacity;
                ShipSymbol = Str(e, "Ship")?.ToLowerInvariant() ?? ShipSymbol;
                var fitted = e.TryGetProperty("Modules", out var modules) && modules.ValueKind == JsonValueKind.Array
                    ? modules.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.Object).ToList()
                    : [];
                HasLimpetController = fitted.Any(m => Str(m, "Item") is { } item && item.Contains("dronecontrol", StringComparison.OrdinalIgnoreCase));
                HasSurfaceScanner = fitted.Any(m => Str(m, "Item") is { } item && item.Contains("detailedsurfacescanner", StringComparison.OrdinalIgnoreCase));
                AmmoKinds = fitted.Where(m => m.TryGetProperty("AmmoInClip", out _) || m.TryGetProperty("AmmoInHopper", out _))
                    .Select(m => AmmoKind(Str(m, "Item") ?? "")).Distinct().ToList();
                Armed = fitted.Count == 0 || fitted.Any(m => Str(m, "Slot") is { } slot && slot.Contains("Hardpoint") && !slot.StartsWith("Tiny"));
                Engineering = fitted.Where(IsCombatModule)
                    .Sum(m => m.TryGetProperty("Engineering", out var mod) && mod.ValueKind == JsonValueKind.Object ? (Long(mod, "Level") ?? 0) / 5.0 : 0);
                break;

            case "Cargo":
                if (Str(e, "Vessel") is { } vessel && vessel != "Ship")
                    break;
                if (e.TryGetProperty("Inventory", out var inventory) && inventory.ValueKind == JsonValueKind.Array)
                    SetCargo(inventory);
                else
                    CargoFileWanted = true;
                break;

            case "BuyAmmo":
                AmmoUsed = false;
                break;
            case "CapShipBond":
            case "PVPKill":
            case "MiningRefined":
                AmmoUsed = true;
                break;
            case "BuyDrones":
                SetLimpets(Limpets + (int)(Long(e, "Count") ?? 0));
                break;
            case "SellDrones":
                SetLimpets(Math.Max(0, Limpets - (int)(Long(e, "Count") ?? 0)));
                break;
            case "LaunchDrone":
                Limpets = Math.Max(0, Limpets - 1);
                break;

            case "HullDamage":
                if (Bool(e, "PlayerPilot") != false && Bool(e, "Fighter") != true)
                {
                    HullHealth = Dbl(e, "Health") ?? HullHealth;
                    LastHullHitUtc = time;
                }
                break;

            case "ShieldState":
                ShieldsUp = Bool(e, "ShieldsUp") ?? ShieldsUp;
                ShieldsDownAtUtc = ShieldsUp ? null : ShieldsDownAtUtc ?? time;
                break;

            case "UnderAttack":
                if (Str(e, "Target") == "You")
                    LastAttackUtc = time;
                break;

            case "Repair":
                if (Strings(e, "Items").Concat(Str(e, "Item") is { } one ? [one] : []).Any(i => i is "Hull" or "All" or "hull" or "all"))
                    HullHealth = 1;
                break;
            case "RepairAll":
                HullHealth = 1;
                break;

            case "ShipTargeted":
                if (Bool(e, "TargetLocked") != true)
                {
                    Target = null;
                    break;
                }

                var ship = Str(e, "Ship_Localised") ?? ShipTypeName(Str(e, "Ship")) ?? "?";
                var stage = (int)(Long(e, "ScanStage") ?? 0);
                var pilot = Str(e, "PilotName_Localised");
                if (Target is null || Target.Ship != ship || (pilot is not null && Target.Pilot is not null && pilot != Target.Pilot))
                    TargetSinceUtc = time;
                // A new target, or more of the same one as the scan goes on.
                Target = new Target(ship,
                    Str(e, "PilotName_Localised") ?? Target?.Pilot,
                    Str(e, "PilotRank") ?? (Target?.Ship == ship ? Target.Rank : null),
                    Str(e, "LegalStatus") ?? (Target?.Ship == ship ? Target.LegalStatus : null),
                    Long(e, "Bounty") ?? (Target?.Ship == ship ? Target.Bounty : 0),
                    stage,
                    Str(e, "Faction") ?? (Target?.Ship == ship ? Target.Faction : null),
                    Str(e, "Ship")?.ToLowerInvariant());
                break;

            case "Bounty":
                var total = Long(e, "TotalReward") ?? Long(e, "Reward") ?? 0;
                if (e.TryGetProperty("Rewards", out var rewards) && rewards.ValueKind == JsonValueKind.Array)
                {
                    foreach (var reward in rewards.EnumerateArray())
                        AddBounty(Str(reward, "Faction") ?? "", Long(reward, "Reward") ?? 0);
                }
                else
                {
                    AddBounty(Str(e, "Faction") ?? "", total);
                }

                BountiesPendingCount++;
                AmmoUsed = true;
                SessionBounties += total;
                SessionEarnings += total;
                SessionKills++;
                break;

            case "FactionKillBond":
                AmmoUsed = true;
                var bond = Long(e, "Reward") ?? 0;
                var awarding = Str(e, "AwardingFaction") ?? "";
                var held = _bondsByFaction.GetValueOrDefault(awarding);
                _bondsByFaction[awarding] = (held.Total + bond, held.Count + 1);
                SessionEarnings += bond;
                SessionBonds += bond;
                SessionKills++;
                break;

            case "RedeemVoucher" when Str(e, "Type") == "CombatBond":
                if (Str(e, "Faction") is { Length: > 0 } bonded)
                    _bondsByFaction.Remove(bonded);
                else
                    _bondsByFaction.Clear();
                break;

            case "RedeemVoucher" when Str(e, "Type") == "bounty":
                if (e.TryGetProperty("Factions", out var factions) && factions.ValueKind == JsonValueKind.Array)
                {
                    foreach (var faction in factions.EnumerateArray())
                    {
                        if (Str(faction, "Faction") is { Length: > 0 } named)
                            _bountiesByFaction.Remove(named);
                        else if (MatchByAmount(Long(faction, "Amount") ?? 0) is { } matched)
                            _bountiesByFaction.Remove(matched);
                    }
                }
                else if (Str(e, "Faction") is { } single)
                {
                    _bountiesByFaction.Remove(single);
                }
                else
                {
                    _bountiesByFaction.Clear();
                }

                if (_bountiesByFaction.Count == 0)
                    BountiesPendingCount = 0;
                break;

            case "Died":
                // The exploration data not sold goes with the ship: those systems are to scan again.
                foreach (var system in _honked.Where(h => !h.Value).Select(h => h.Key).ToList())
                    _honked.Remove(system);
                foreach (var body in _mappedBodies.Where(b => !b.Value.Sold).Select(b => b.Key).ToList())
                    _mappedBodies.Remove(body);
                _bountiesByFaction.Clear();
                BountiesPendingCount = 0;
                _bondsByFaction.Clear();
                _organicUnsold.Clear();
                _explorationUnsold.Clear();
                _mappedEfficient.Clear();
                _sessionScans.Clear();
                _sessionMapped.Clear();
                _sessionEfficient.Clear();
                AmmoUsed = false;
                Organic = null;
                Target = null;
                Supercharge = null;
                ShieldsUp = true;
                ShieldsDownAtUtc = null;
                break;

            case "MissionCompleted":
            case "SearchAndRescue":
            case "CommunityGoalReward":
            case "DatalinkVoucher":
                SessionEarnings += Long(e, "Reward") ?? 0;
                if (name == "MissionCompleted")
                {
                    AddMaterials(e, "MaterialsReward", "Name", "Count", +1);
                    _onFootMissions.Remove(Long(e, "MissionID") ?? -1);
                }
                break;

            // ------------------------------------------------------------------------- on foot

            case "MissionAccepted":
                if (IsOnFoot(Str(e, "Name")) && Long(e, "MissionID") is { } accepted)
                    _onFootMissions.Add(accepted);
                break;
            case "MissionFailed":
            case "MissionAbandoned":
                _onFootMissions.Remove(Long(e, "MissionID") ?? -1);
                break;
            case "Missions":
                // At each load: the missions still running.
                _onFootMissions.Clear();
                if (e.TryGetProperty("Active", out var active) && active.ValueKind == JsonValueKind.Array)
                    foreach (var mission in active.EnumerateArray())
                        if (IsOnFoot(Str(mission, "Name")) && Long(mission, "MissionID") is { } running)
                            _onFootMissions.Add(running);
                break;

            case "Backpack":
                if (!SetConsumables(_backpack, e))
                    BackpackFileWanted = true;
                break;
            case "ShipLocker":
                if (!SetConsumables(_locker, e))
                    ShipLockerFileWanted = true;
                break;
            case "BackpackChange":
                foreach (var (side, sign) in new[] { ("Added", +1), ("Removed", -1) })
                    if (e.TryGetProperty(side, out var moved) && moved.ValueKind == JsonValueKind.Array)
                        foreach (var item in moved.EnumerateArray())
                            if (Str(item, "Type") == "Consumable" && Str(item, "Name") is { } consumable)
                                _backpack[consumable] = Math.Max(0, Packed(consumable) + sign * (int)(Long(item, "Count") ?? 0));
                break;

            case "Disembark":
                DisembarkedUtc = time;
                DisembarkedOnPlanet = Bool(e, "OnPlanet") == true;
                break;

            case "ScanOrganic":
                if (Str(e, "Species") is not { } species)
                    break;
                var scan = Organic?.Species == species ? Organic : null;
                switch (Str(e, "ScanType"))
                {
                    case "Analyse":
                        _organicUnsold.Add(species);
                        Organic = null;
                        break;
                    case "Log":
                        // The sampler holds one species: logging another drops the one half done.
                        Organic = new OrganicScan(species, Str(e, "Genus") ?? "", Str(e, "Species_Localised") ?? species, 1);
                        break;
                    default:
                        Organic = scan is null
                            ? new OrganicScan(species, Str(e, "Genus") ?? "", Str(e, "Species_Localised") ?? species, 2)
                            : scan with { Samples = Math.Min(3, scan.Samples + 1) };
                        break;
                }
                break;

            // ------------------------------------------------------------------------- materials
            // The Materials event at each load gives the inventory; the others move it.

            case "Materials":
                _materials.Clear();
                foreach (var category in new[] { "Raw", "Manufactured", "Encoded" })
                    AddMaterials(e, category, "Name", "Count", +1);
                break;

            case "MaterialCollected":
                if (Str(e, "Name") is { } collected)
                {
                    LearnName(e, collected, "Name_Localised");
                    var count = AddMaterial(collected, (int)(Long(e, "Count") ?? 0));
                    LastPickup = new MaterialPickup(collected, MaterialName(collected), count, time);
                }
                break;

            case "MaterialDiscarded":
                if (Str(e, "Name") is { } discarded)
                    AddMaterial(discarded, -(int)(Long(e, "Count") ?? 0));
                break;

            case "MaterialTrade":
                foreach (var (side, sign) in new[] { ("Paid", -1), ("Received", +1) })
                    if (e.TryGetProperty(side, out var trade) && trade.ValueKind == JsonValueKind.Object && Str(trade, "Material") is { } traded)
                    {
                        LearnName(trade, traded, "Material_Localised");
                        AddMaterial(traded, sign * (int)(Long(trade, "Quantity") ?? 0));
                    }
                break;

            case "EngineerCraft":
                AddMaterials(e, "Ingredients", "Name", "Count", -1);
                break;

            case "Synthesis":
            case "TechnologyBroker":
                AddMaterials(e, "Materials", "Name", "Count", -1);
                break;

            case "ScientificResearch":
                if (Str(e, "Name") is { } researched)
                    AddMaterial(researched, -(int)(Long(e, "Count") ?? 0));
                break;

            case "EngineerContribution":
                if (Str(e, "Type") == "Materials" && Str(e, "Material") is { } contributed)
                    AddMaterial(contributed, -(int)(Long(e, "Quantity") ?? 0));
                break;

            case "SellExplorationData":
            case "MultiSellExplorationData":
                SessionEarnings += Long(e, "TotalEarnings") ?? 0;
                if (e.TryGetProperty("Discovered", out var discovered) && discovered.ValueKind == JsonValueKind.Array)
                    foreach (var sold in discovered.EnumerateArray())
                        if (Str(sold, "SystemName") is { } soldSystem)
                            Sold(soldSystem);
                break;

            case "FSSDiscoveryScan":
                if (Str(e, "SystemName") is { } honked)
                    _honked.TryAdd(honked, false);
                break;

            case "SAAScanComplete":
                if (Str(e, "BodyName") is { } mapped && System is { } mappedIn)
                    _mappedBodies.TryAdd(mapped, (mappedIn, false));
                if (Str(e, "BodyName") is { } probed)
                {
                    var efficient = Long(e, "ProbesUsed") <= Long(e, "EfficiencyTarget");
                    _mappedEfficient[probed] = efficient;
                    _sessionMapped.Add(probed);
                    if (efficient)
                        _sessionEfficient.Add(probed);
                }
                break;

            case "Scan":
                if (Str(e, "BodyName") is { } scannedName && ExplorationValues.FromScan(e) is { } value)
                {
                    _explorationUnsold.TryAdd(scannedName, value);
                    _sessionScans.TryAdd(scannedName, value);
                }
                if (Long(e, "SystemAddress") != SystemAddress)
                    break;
                Orbits.Add(e);
                if (Companion.NotableBodies.FromScan(e) is { } notable)
                    _scannedNotable[notable.Name] = notable;
                if (Companion.NotableBodies.Scanned(e) is { } scanned)
                    _scannedBodies[scanned.Name] = scanned;
                if (Str(e, "BodyName") is { } described && BodyDetails.FromScan(e) is { } details)
                    _bodyDetails[described] = details;
                break;

            // Counted by the FSS, then named by the surface scanner: the later says more.
            case "FSSBodySignals":
            case "SAASignalsFound":
                if (Long(e, "SystemAddress") == SystemAddress && Str(e, "BodyName") is { } signalled)
                    _bodySignals[signalled] = BodySignals.FromEvent(e);
                break;

            case "SellOrganicData":
                if (e.TryGetProperty("BioData", out var bio) && bio.ValueKind == JsonValueKind.Array)
                {
                    SessionEarnings += bio.EnumerateArray().Sum(b => (Long(b, "Value") ?? 0) + (Long(b, "Bonus") ?? 0));
                    foreach (var sold in bio.EnumerateArray())
                        if (Str(sold, "Species") is { } soldSpecies)
                            _organicUnsold.Remove(soldSpecies);
                }
                break;

            case "MarketSell":
                var sale = Long(e, "TotalSale") ?? 0;
                var paid = (Long(e, "AvgPricePaid") ?? 0) * (Long(e, "Count") ?? 0);
                SessionEarnings += Math.Max(0, sale - paid);
                break;

            case "FSSSignalDiscovered":
                if (Long(e, "SystemAddress") != SystemAddress)
                    break;
                // The emissions give materials, the higher their grade the better; a salvageable wreck too, and stays.
                var expires = time.AddSeconds(Dbl(e, "TimeRemaining") ?? 900);
                switch (Str(e, "USSType") ?? Str(e, "SignalName"))
                {
                    case "$USS_Type_VeryValuableSalvage;":
                        _signals.Add(new Signal(Str(e, "USSType_Localised") ?? Resources.Strings.SignalHighGrade, expires, HighGrade: true));
                        break;
                    case "$USS_Type_ValuableSalvage;":
                        _signals.Add(new Signal(Str(e, "USSType_Localised") ?? Resources.Strings.SignalEncoded, expires));
                        break;
                    case "$USS_Type_Salvage;":
                        _signals.Add(new Signal(Str(e, "USSType_Localised") ?? Resources.Strings.SignalDegraded, expires));
                        break;
                    case "$MULTIPLAYER_SCENARIO81_TITLE;":
                        _signals.Add(new Signal(Str(e, "SignalName_Localised") ?? Resources.Strings.SignalSalvage, null));
                        break;
                }

                break;
        }
    }

    public void SetBackpackFromFile(JsonElement root) => SetConsumables(_backpack, root);

    public void SetShipLockerFromFile(JsonElement root) => SetConsumables(_locker, root);

    public void SetCargoFromFile(JsonElement root)
    {
        if (root.TryGetProperty("Inventory", out var inventory) && inventory.ValueKind == JsonValueKind.Array)
            SetCargo(inventory);
    }

    /// <summary>The credits as Status.json gives them, fresher than the journal's.</summary>
    public void SetCredits(long credits) => Credits = credits;

    public static IReadOnlyList<RouteHop> ParseRoute(JsonElement route) =>
        route.EnumerateArray()
            .Select(h => new RouteHop(Str(h, "StarSystem") ?? "?", Long(h, "SystemAddress") ?? 0, Str(h, "StarClass") ?? "", Pos(h)))
            .ToList();

    /// <summary>The name of the ammunition a module fires, for the reminder to restock.</summary>
    internal static string AmmoKind(string item)
    {
        item = item.ToLowerInvariant();
        return item switch
        {
            _ when item.Contains("torp") => Resources.Strings.AmmoTorpedoes,
            _ when item.Contains("seismchrg") || item.Contains("subsurfdisp") => Resources.Strings.AmmoMiningCharges,
            _ when item.Contains("missile") => Resources.Strings.AmmoMissiles,
            _ when item.Contains("minelauncher") => Resources.Strings.AmmoMines,
            _ when item.Contains("multicannon") => Resources.Strings.AmmoMulticannons,
            _ when item.Contains("slugshot") => Resources.Strings.AmmoFragmentCannons,
            _ when item.Contains("cannon") => Resources.Strings.AmmoCannons,
            _ when item.Contains("railgun") => Resources.Strings.AmmoRailguns,
            _ when item.Contains("plasmaaccelerator") => Resources.Strings.AmmoPlasma,
            _ when item.Contains("flak") => Resources.Strings.AmmoFlak,
            _ when item.Contains("flechette") => Resources.Strings.AmmoFlechettes,
            _ when item.Contains("chaff") => Resources.Strings.AmmoChaff,
            _ when item.Contains("heatsink") => Resources.Strings.AmmoHeatSinks,
            _ => Resources.Strings.AmmoOther,
        };
    }

    private void Sold(string system)
    {
        if (_honked.ContainsKey(system))
            _honked[system] = true;
        foreach (var body in _mappedBodies.Where(b => string.Equals(b.Value.System, system, StringComparison.OrdinalIgnoreCase)).Select(b => b.Key).ToList())
            _mappedBodies[body] = (system, true);
        foreach (var body in _explorationUnsold.Where(b => string.Equals(b.Value.System, system, StringComparison.OrdinalIgnoreCase)).Select(b => b.Key).ToList())
        {
            _explorationUnsold.Remove(body);
            _mappedEfficient.Remove(body);
        }
    }

    private void EnterSystem(JsonElement e)
    {
        var address = Long(e, "SystemAddress") ?? 0;
        if (address != SystemAddress)
        {
            _signals.Clear();
            Target = null;
            SystemBodiesWanted = true;
            _scannedNotable.Clear();
            _edsmNotable = [];
            _scannedBodies.Clear();
            _bodyDetails.Clear();
            _bodySignals.Clear();
            Orbits = new SystemOrbits();
        }

        // SupercruiseEntry and SupercruiseExit carry no StarPos.
        Position = Pos(e) ?? (address == SystemAddress ? Position : null);
        System = Str(e, "StarSystem") ?? System;
        SystemAddress = address;

        // The route done: the game keeps it, there is nothing left to show.
        if (Route.Count > 0 && Route[^1].Address == address)
        {
            Route = [];
            FsdTarget = null;
            FsdTargetRemaining = null;
        }
    }

    private void SetCargo(JsonElement inventory)
    {
        var drones = inventory.EnumerateArray()
            .Where(i => string.Equals(Str(i, "Name"), "drones", StringComparison.OrdinalIgnoreCase))
            .Sum(i => (int)(Long(i, "Count") ?? 0));
        SetLimpets(drones);
    }

    private void SetLimpets(int count)
    {
        Limpets = count;
        if (ShipId >= 0 && count > _limpetsUsual.GetValueOrDefault(ShipId))
            _limpetsUsual[ShipId] = count;
    }

    /// <summary>
    /// The bonuses a cashed-in bounty may carry: none, a power's (×1.4), Archer's (×2), both; each of them
    /// also 10 % over, as seen on 2026-10-21.
    /// </summary>
    private static readonly double[] BountyBonuses = new[] { 1, 1.4, 2, 2.8 }.SelectMany(b => new[] { b, b * 1.1 }).ToArray();

    /// <summary>
    /// The game often leaves the faction out of a cashed-in line, and its amount carries the bonuses: the
    /// pending faction whose total, times a known bonus, comes closest to it, within 1 %.
    /// </summary>
    private string? MatchByAmount(long amount) =>
        _bountiesByFaction
            .Where(p => p.Value > 0)
            .Select(p => (p.Key, Gap: BountyBonuses.Min(b => Math.Abs(amount / (p.Value * b) - 1))))
            .Where(c => c.Gap < 0.01)
            .OrderBy(c => c.Gap)
            .Select(c => c.Key)
            .FirstOrDefault();

    private void AddBounty(string faction, long amount)
    {
        _bountiesByFaction[faction] = _bountiesByFaction.GetValueOrDefault(faction) + amount;
    }

    private static bool IsOnFoot(string? mission) => mission?.StartsWith("Mission_OnFoot", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The consumables of a Backpack or ShipLocker list; false when the event leaves them to the file.</summary>
    private static bool SetConsumables(Dictionary<string, int> into, JsonElement e)
    {
        if (!e.TryGetProperty("Consumables", out var consumables) || consumables.ValueKind != JsonValueKind.Array)
            return false;

        into.Clear();
        foreach (var item in consumables.EnumerateArray())
            if (Str(item, "Name") is { } consumable)
                into[consumable] = into.GetValueOrDefault(consumable) + (int)(Long(item, "Count") ?? 0);
        return true;
    }

    /// <summary>What weighs in a fight: weapons and shield boosters, the shields, the hull, power and engines.</summary>
    private static bool IsCombatModule(JsonElement module)
    {
        var slot = Str(module, "Slot") ?? "";
        var item = Str(module, "Item") ?? "";
        return (slot.Contains("Hardpoint") && !item.Contains("scanner", StringComparison.OrdinalIgnoreCase))
            || slot is "Armour" or "PowerPlant" or "MainEngines" or "PowerDistributor"
            || item.Contains("shieldgenerator", StringComparison.OrdinalIgnoreCase)
            || item.Contains("reinforcement", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"python_nx" → "Python nx": the journal does not localise every ship.</summary>
    private static string? ShipTypeName(string? id) => id switch
    {
        null or "" => null,
        "python_nx" => "Python Mk II",
        "krait_mkii" => "Krait Mk II",
        "diamondbackxl" => "Diamondback Explorer",
        "diamondback" => "Diamondback Scout",
        "type6" => "Type-6",
        "type7" => "Type-7",
        "type9" => "Type-9",
        "type10" => "Type-10",
        "asp" => "Asp Explorer",
        "asp_scout" => "Asp Scout",
        "federation_corvette" => "Corvette",
        "anaconda" => "Anaconda",
        "ferdelance" => "Fer-de-Lance",
        "empire_eagle" => "Imperial Eagle",
        "cobramkiii" => "Cobra Mk III",
        "cobramkiv" => "Cobra Mk IV",
        "cobramkv" => "Cobra Mk V",
        "viper" => "Viper Mk III",
        "viper_mkiv" => "Viper Mk IV",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('_', ' ')),
    };

    internal static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : null;

    internal static double? Dbl(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    /// <summary>Adds <paramref name="delta"/>, within 0 and the grade's cap; returns the new count.</summary>
    private int AddMaterial(string symbol, int delta)
    {
        var count = Math.Max(0, _materials.GetValueOrDefault(symbol) + delta);
        if (Materials.Cap(symbol) is { } cap)
            count = Math.Min(count, cap);
        _materials[symbol] = count;
        return count;
    }

    /// <summary>Each item of the array <paramref name="array"/>, counted <paramref name="sign"/> times.</summary>
    private void AddMaterials(JsonElement e, string array, string nameField, string countField, int sign)
    {
        if (!e.TryGetProperty(array, out var items) || items.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in items.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Object && Str(item, nameField) is { } symbol)
            {
                LearnName(item, symbol, nameField + "_Localised");
                AddMaterial(symbol, sign * (int)(Long(item, countField) ?? 0));
            }
    }

    private void LearnName(JsonElement e, string symbol, string localisedField)
    {
        if (Str(e, localisedField) is { Length: > 0 } localised)
            _materialNames[symbol] = localised;
    }

    /// <summary>The game's French name; the journal leaves it out when it is the symbol itself ("nickel").</summary>
    private string MaterialName(string symbol) =>
        _materialNames.GetValueOrDefault(symbol) ?? (symbol.Length > 0 ? char.ToUpperInvariant(symbol[0]) + symbol[1..] : symbol);

    private static StarPos? Pos(JsonElement e) =>
        e.TryGetProperty("StarPos", out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3
            && v.EnumerateArray().All(c => c.ValueKind == JsonValueKind.Number)
            ? new StarPos(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble())
            : null;

    internal static bool? Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static IEnumerable<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!)
            : [];
}

public static class Stars
{
    /// <summary>KGBFOAM, giants included ("K_OrangeGiant"); not "MS", "TTS" nor the dwarfs.</summary>
    public static bool IsScoopable(string starClass) =>
        starClass.Split('_')[0] is "O" or "B" or "A" or "F" or "G" or "K" or "M";

    /// <summary>"K_OrangeGiant" → "K géante": the letter, and the size when it is not a plain star.</summary>
    public static string Label(string starClass)
    {
        var letter = starClass.Split('_')[0];
        return starClass.Contains("SuperGiant", StringComparison.OrdinalIgnoreCase) ? string.Format(Resources.Strings.StarSupergiant, letter)
            : starClass.Contains("Giant", StringComparison.OrdinalIgnoreCase) ? string.Format(Resources.Strings.StarGiant, letter)
            : letter;
    }

    /// <summary>A neutron star or a white dwarf: a jet that supercharges the FSD, and damages it.</summary>
    public static string? Hazard(string starClass) => starClass switch
    {
        "N" => Resources.Strings.StarNeutron,
        "H" or "SupermassiveBlackHole" => Resources.Strings.StarBlackHole,
        _ when starClass.StartsWith('D') && starClass.Length <= 4 && starClass.All(char.IsUpper) => Resources.Strings.StarWhiteDwarf,
        _ => null,
    };
}
