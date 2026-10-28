using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using EDC.Core;

namespace EDC.Companion;

/// <summary>
/// Keeps the <see cref="GameState"/> up to date: every journal replayed once at start (the bounties not
/// cashed in may date from an earlier session), then the newest followed line by line, with Status.json,
/// NavRoute.json, Cargo.json, Backpack.json and ShipLocker.json read when they change, EDSM asked the value
/// and the planets of each new system, and Spansh the planets of each new route. Lives on the UI thread.
/// </summary>
public sealed class CompanionService : IDisposable
{
    private readonly string _directory;
    private readonly DispatcherTimer _timer;
    private JournalTail? _tail;
    private DateTime _statusWrite;

    public CompanionService(string directory)
    {
        _directory = directory;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Tick();
    }

    public GameState State { get; private set; } = new();
    public GameStatus Status { get; private set; } = GameStatus.None;

    /// <summary>Raised on the UI thread after each pass, twice a second.</summary>
    public event Action? Changed;

    public async void Start()
    {
        try
        {
            State = await Task.Run(Replay);
            AppLog.Write($"compagnon : journaux relus, {State.System ?? "système inconnu"}, primes en attente {Briefing.Credits(State.BountiesPending)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"compagnon : journaux illisibles ({ex.Message})");
        }

        // Never started, the tail reads the newest journal from its beginning.
        _tail = new JournalTail(_directory);
        Tick();
        _timer.Start();
    }

    /// <summary>Every journal but the newest, oldest first; the newest is left to the tail.</summary>
    private GameState Replay()
    {
        var state = new GameState();
        if (!Directory.Exists(_directory))
            return state;

        var journals = new DirectoryInfo(_directory).EnumerateFiles("Journal.*.log").OrderBy(f => f.LastWriteTimeUtc).ToList();
        foreach (var journal in journals.SkipLast(1))
        {
            foreach (var line in ReadShared(journal.FullName).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                state.Apply(line);
        }

        return state;
    }

    private void Tick()
    {
        try
        {
            if (_tail is not null)
            {
                foreach (var line in _tail.ReadNewLines())
                    State.Apply(line);
            }

            if (State.RouteFileWanted)
            {
                State.RouteFileWanted = false;
                using var route = ReadJson("NavRoute.json");
                if (route is not null && route.RootElement.TryGetProperty("Route", out var hops) && hops.ValueKind == JsonValueKind.Array)
                    State.SetRoute(GameState.ParseRoute(hops));
            }

            if (State.SystemValueWanted)
            {
                State.SystemValueWanted = false;
                if (State.SystemAddress != 0)
                {
                    AskSystemValue(State, State.SystemAddress);
                    AskNotableBodies(State, State.SystemAddress);
                    // Spansh too: EDSM often knows a system without its planets, and the route may be gone.
                    AskRouteNotable(State, [State.SystemAddress]);
                }
            }

            if (State.RouteNotableWanted)
            {
                State.RouteNotableWanted = false;
                if (State.Route.Count > 0)
                    AskRouteNotable(State, State.Route.Select(h => h.Address).ToList());
            }

            if (State.CargoFileWanted)
            {
                State.CargoFileWanted = false;
                using var cargo = ReadJson("Cargo.json");
                if (cargo is not null)
                    State.SetCargoFromFile(cargo.RootElement);
            }

            if (State.BackpackFileWanted)
            {
                State.BackpackFileWanted = false;
                using var backpack = ReadJson("Backpack.json");
                if (backpack is not null)
                    State.SetBackpackFromFile(backpack.RootElement);
            }

            if (State.ShipLockerFileWanted)
            {
                State.ShipLockerFileWanted = false;
                using var locker = ReadJson("ShipLocker.json");
                if (locker is not null)
                    State.SetShipLockerFromFile(locker.RootElement);
            }

            var statusFile = Path.Combine(_directory, "Status.json");
            var write = File.Exists(statusFile) ? File.GetLastWriteTimeUtc(statusFile) : DateTime.MinValue;
            if (write != _statusWrite && GameStatus.Parse(ReadShared(statusFile)) is { } status)
            {
                _statusWrite = write;
                Status = status;
                if (status.Balance is { } balance)
                    State.SetCredits(balance);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The game holds its files open; a missed pass is caught up on the next one.
        }

        // Each pass, read or not: the rate and the signals' time left move on their own.
        Changed?.Invoke();
    }

    /// <summary>EDSM's answer comes back on the UI thread; without it the banner only lacks the value.</summary>
    private static async void AskSystemValue(GameState state, long address)
    {
        try
        {
            state.SetSystemValue(address, await SystemValues.FetchAsync(address));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            AppLog.Write($"compagnon : valeur du système indisponible ({ex.Message})");
        }
    }

    /// <summary>Without it, only the planets scanned in the game are told.</summary>
    private static async void AskNotableBodies(GameState state, long address)
    {
        try
        {
            state.SetNotableBodies(address, await NotableBodies.FetchAsync(address));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            AppLog.Write($"compagnon : planètes du système indisponibles ({ex.Message})");
        }
    }

    /// <summary>Without it, the route's planets are only told on arriving.</summary>
    private static async void AskRouteNotable(GameState state, IReadOnlyList<long> addresses)
    {
        try
        {
            var bodies = await NotableBodies.FetchRouteAsync(addresses, state.MinNotableValue);
            state.SetRouteNotable(bodies);
            AppLog.Write($"compagnon : {bodies.Count} système(s) de la route avec une planète à cartographier, d'après Spansh");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            AppLog.Write($"compagnon : planètes de la route indisponibles ({ex.Message})");
        }
    }

    private JsonDocument? ReadJson(string name)
    {
        var path = Path.Combine(_directory, name);
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonDocument.Parse(ReadShared(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose() => _timer.Stop();
}
