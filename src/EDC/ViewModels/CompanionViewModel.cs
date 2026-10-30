using System.Collections.ObjectModel;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDC.Companion;
using EDC.Core;
using EDC.Resources;
using EDC.Services;
using EDC.Views;

namespace EDC.ViewModels;

/// <summary>A line of the window: a chip of the banner or a reminder, with its longer explanation.</summary>
public sealed record ChipRow(string Text, string? Detail, Brush Foreground)
{
    public static ChipRow From(Chip chip) => new(chip.Text, chip.Detail, CompanionBanner.ToneBrush(chip.Tone));
}

/// <summary>A line of the exploration tab: what the panel over the game says, the landing icon included.</summary>
public sealed record PanelRow(string Text, Brush Foreground, bool Header, bool Loud, bool Landable = false)
{
    /// <summary>A car, for the SRV, of Segoe MDL2 Assets: there on Windows 10 and 11.</summary>
    public const string LandGlyph = "\uE804";

    public static readonly FontFamily Glyphs = new("Segoe MDL2 Assets");

    public string LandIcon => Landable ? " " + LandGlyph : "";

    public static IReadOnlyList<PanelRow> From(IReadOnlyList<PanelLine> lines) => lines.Count == 0
        ? [new PanelRow(Strings.NothingHere, CompanionBanner.ToneBrush(Tone.Muted), false, false)]
        : lines.Select(l => new PanelRow(l.Text, CompanionBanner.ToneBrush(l.Tone), l.Header, l.Loud, l.Landable)).ToList();
}

/// <summary>A system of the route still to come.</summary>
public sealed record RouteRow(int Number, string System, string StarClass, string Note, Brush Foreground);

/// <summary>The companion's tab: the band's content, the reminders, the session and the route.</summary>
public sealed partial class CompanionViewModel : ObservableObject
{
    private readonly CompanionService? _service;
    private readonly Func<PadBattery>? _pad;
    private readonly PlayerSettings? _settings;
    private readonly Action? _saveSettings;
    private readonly Updater? _updater;
    private bool _refreshing;
    private string? _routeKey;

    public CompanionViewModel(CompanionService service, Func<PadBattery> pad, PlayerSettings settings, Action saveSettings, Updater updater)
    {
        _service = service;
        _pad = pad;
        _settings = settings;
        _saveSettings = saveSettings;
        _updater = updater;
        Neutron = new NeutronViewModel(settings, saveSettings);
        service.Changed += Refresh;
        updater.Changed += Refresh;
        Refresh();
    }

    /// <summary>For an off-screen render: the values are then set by hand.</summary>
    internal CompanionViewModel()
    {
        Neutron = new NeutronViewModel(new PlayerSettings(), () => { });
    }

    /// <summary>The neutron tab, fed by the same journal.</summary>
    public NeutronViewModel Neutron { get; }

    [ObservableProperty] private string _shipText = "";
    [ObservableProperty] private string _whereText = "";
    [ObservableProperty] private string _routeText = "";
    [ObservableProperty] private string _sessionText = "";
    [ObservableProperty] private string _rateText = "";
    [ObservableProperty] private string _explorationRateText = "";
    [ObservableProperty] private string _explorationSessionText = "";
    [ObservableProperty] private string _updateText = "";
    [ObservableProperty] private bool _bannerEnabled;
    [ObservableProperty] private bool _autoUpdate;
    [ObservableProperty] private bool _explorationPanel;
    [ObservableProperty] private bool _firstDiscoveries;
    [ObservableProperty] private bool _firstMappings;
    [ObservableProperty] private bool _controllerInBanner;
    [ObservableProperty] private bool _honkReminder;
    [ObservableProperty] private bool _launchEdmc;

    /// <summary>The box to launch EDMarketConnector shows only when it is installed; looked for once, on opening.</summary>
    public bool EdmcInstalled { get; } = EdmcLauncher.Find() is not null;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _trackToast;
    /// <summary>The setting's value in thousands of credits, for the slider.</summary>
    [ObservableProperty] private int _panelMinValueK;
    [ObservableProperty] private string _huntText = Strings.HuntIntro;
    [ObservableProperty] private HuntSpot? _huntNearest;
    [ObservableProperty] private HuntSpot? _huntOfPower;
    [ObservableProperty] private string _huntPowerCaption = "";
    [ObservableProperty] private string _huntNearestTrip = "";
    [ObservableProperty] private string _huntOfPowerTrip = "";
    [ObservableProperty] private string _stationText = Strings.StationIntro;
    [ObservableProperty] private StationSpot? _station;
    [ObservableProperty] private string _stationTrip = "";

    public ObservableCollection<ChipRow> CombatSales { get; } = [];
    public ObservableCollection<ChipRow> TargetRows { get; } = [];
    public ObservableCollection<ChipRow> Reminders { get; } = [];
    public ObservableCollection<ChipRow> ExplorationSales { get; } = [];
    public ObservableCollection<RouteRow> RouteRows { get; } = [];
    public ObservableCollection<PanelRow> PanelRows { get; } = [];
    public ObservableCollection<CommanderRank> PilotRanks { get; } = [];
    public ObservableCollection<CommanderRank> NavyRanks { get; } = [];
    public ObservableCollection<string> ReputationLines { get; } = [];
    [ObservableProperty] private bool _hasRanks;

    partial void OnBannerEnabledChanged(bool value)
    {
        if (_refreshing || _settings is null)
            return;
        _settings.CompanionBanner = value;
        _saveSettings?.Invoke();
        AppLog.Write(value ? "bandeau du compagnon affiché" : "bandeau du compagnon masqué");
    }

    partial void OnAutoUpdateChanged(bool value)
    {
        if (_refreshing || _settings is null)
            return;
        _settings.AutoUpdate = value;
        _saveSettings?.Invoke();
    }

    partial void OnPanelMinValueKChanged(int value)
    {
        if (_refreshing || _settings is null)
            return;
        _settings.PanelMinValue = value * 1000L;
        _saveSettings?.Invoke();
        Refresh();
    }

    partial void OnFirstDiscoveriesChanged(bool value)
    {
        if (_refreshing || _settings is null)
            return;
        _settings.PanelFirstDiscoveries = value;
        _saveSettings?.Invoke();
        Refresh();
    }

    partial void OnFirstMappingsChanged(bool value)
    {
        if (_refreshing || _settings is null)
            return;
        _settings.PanelFirstMappings = value;
        _saveSettings?.Invoke();
        Refresh();
    }

    partial void OnExplorationPanelChanged(bool value)
    {
        if (_refreshing || _settings is null)
            return;
        _settings.ExplorationPanel = value;
        _saveSettings?.Invoke();
        AppLog.Write(value ? "panneau d'exploration affiché" : "panneau d'exploration masqué");
    }

    partial void OnLaunchEdmcChanged(bool value)
    {
        if (_refreshing || _settings is null)
            return;
        _settings.LaunchEdmc = value;
        _saveSettings?.Invoke();
    }

    partial void OnHonkReminderChanged(bool value)
    {
        if (_refreshing || _settings is null)
            return;
        _settings.HonkReminder = value;
        _saveSettings?.Invoke();
    }

    partial void OnControllerInBannerChanged(bool value)
    {
        if (_refreshing || _settings is null)
            return;
        _settings.ControllerInBanner = value;
        _saveSettings?.Invoke();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_refreshing && _settings is not null)
            StartupManager.SetEnabled(value);
    }

    partial void OnTrackToastChanged(bool value)
    {
        if (_refreshing || _settings is null)
            return;
        _settings.TrackToast = value;
        _saveSettings?.Invoke();
        AppLog.Write(value ? "morceau affiché en jeu" : "morceau plus affiché en jeu");
    }

    [RelayCommand]
    private async Task CheckUpdate()
    {
        if (_updater is not null)
            await _updater.CheckNowAsync();
    }

    /// <summary>The two systems where to hunt, from the current one: the nearest, and the nearest of the power pledged to.</summary>
    [RelayCommand]
    private async Task SearchHunt()
    {
        if (_service?.State is not { System: { } system } state)
        {
            HuntText = Strings.HuntNoSystem;
            return;
        }

        HuntText = string.Format(Strings.HuntSearching, system);
        try
        {
            var power = HuntPower(_settings?.Power, state.Power);
            var result = await HuntFinder.FindAsync(system, power);
            HuntNearest = result.Nearest;
            HuntOfPower = result.OfPower;
            HuntPowerCaption = power?.ToUpperInvariant() ?? "";
            HuntNearestTrip = result.Nearest?.Trip(state.MaxJumpRange) ?? "";
            HuntOfPowerTrip = result.OfPower?.Trip(state.MaxJumpRange) ?? "";
            HuntText = result.Nearest is null ? string.Format(Strings.HuntNone, system)
                : power is null ? Strings.HuntNoPower
                : result.OfPower is null ? string.Format(Strings.HuntNoPowerRes, power)
                : Strings.ClickToCopy;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            HuntText = string.Format(Strings.SearchFailed, ex.Message);
            AppLog.Write($"chasse : recherche impossible depuis {system} ({ex.Message})");
        }
    }

    /// <summary>The nearest station with every service but the material trader, from the current system.</summary>
    [RelayCommand]
    private async Task SearchStation()
    {
        if (_service?.State is not { System: { } system })
        {
            StationText = Strings.HuntNoSystem;
            return;
        }

        StationText = string.Format(Strings.HuntSearching, system);
        try
        {
            Station = await StationFinder.FindAsync(system);
            StationTrip = Station is { } found ? StationLine(found) : "";
            StationText = Station is null ? string.Format(Strings.StationNone, system) : Strings.StationFound;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            StationText = string.Format(Strings.SearchFailed, ex.Message);
            AppLog.Write($"station : recherche impossible depuis {system} ({ex.Message})");
        }
    }

    internal static string StationLine(StationSpot station) => string.Format(Strings.StationTrip,
        station.Distance <= 0 ? Strings.StationHere : string.Format(Strings.LightYears, station.Distance.ToString("0.#", Loc.Culture)),
        (station.ArrivalLs ?? 0).ToString("N0", Loc.Culture),
        station.LargePad ? Strings.LargePad : Strings.MediumPad);

    [RelayCommand]
    private void CopyStation(StationSpot? station)
    {
        if (station is null)
            return;

        try
        {
            Clipboard.SetText(station.System);
            StationText = string.Format(Strings.Copied, station.System);
        }
        catch (COMException)
        {
            StationText = Strings.ClipboardBusy;
        }
    }

    [RelayCommand]
    private void CopySystem(HuntSpot? spot)
    {
        if (spot is null)
            return;

        try
        {
            Clipboard.SetText(spot.System);
            HuntText = string.Format(Strings.Copied, spot.System);
        }
        catch (COMException)
        {
            // Another program holds the clipboard for a moment.
            HuntText = Strings.ClipboardBusy;
        }
    }

    public void Refresh()
    {
        if (_service is null)
            return;

        _refreshing = true;
        try
        {
            _service.State.MinNotableValue = _settings?.PanelMinValue ?? 700_000;
            _service.State.FirstDiscoveries = _settings?.PanelFirstDiscoveries ?? false;
            _service.State.FirstMappings = _settings?.PanelFirstMappings ?? false;
            Show(_service.State, _service.Status, _pad?.Invoke() ?? PadBattery.None, DateTime.UtcNow);
            BannerEnabled = _settings?.CompanionBanner ?? true;
            ExplorationPanel = _settings?.ExplorationPanel ?? true;
            FirstDiscoveries = _settings?.PanelFirstDiscoveries ?? false;
            FirstMappings = _settings?.PanelFirstMappings ?? false;
            ControllerInBanner = _settings?.ControllerInBanner ?? true;
            HonkReminder = _settings?.HonkReminder ?? true;
            LaunchEdmc = _settings?.LaunchEdmc ?? false;
            Language = _settings?.Language ?? "auto";
            SetPowers(_service.State.Power);
            Power = _settings?.Power ?? "auto";
            AutoUpdate = _settings?.AutoUpdate ?? true;
            TrackToast = _settings?.TrackToast ?? true;
            StartWithWindows = StartupManager.IsEnabled();
            PanelMinValueK = (int)((_settings?.PanelMinValue ?? 700_000) / 1000);
            UpdateText = _updater?.StatusText ?? "";
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Everything read from the game; also what an off-screen render calls.</summary>
    internal void Show(GameState state, GameStatus status, PadBattery pad, DateTime now)
    {
        ShipText = ShipLine(state.Commander, state.ShipName, state.ShipType);
        WhereText = state.Station is { } station ? $"{station}, {state.System}" : state.System ?? "";

        RouteText = state.Destination is { } destination
            ? string.Format(Strings.RouteTo, destination.System, Loc.Plural(state.JumpsLeft, Strings.JumpsOne, Strings.JumpsMany))
            : Strings.NoRoute;

        // The combat tab: what fighting pays, what to cash in, the target, what not to forget at the station.
        if (state.SessionStartUtc is { } start)
        {
            var elapsed = now - start;
            SessionText = SessionLine(start, now, Loc.Plural(state.SessionKills, Strings.KillsOne, Strings.KillsMany));
            RateText = Briefing.CombatRate(state, now) is { } rate ? string.Format(Strings.PerHour, Briefing.Credits(rate)) : Strings.RateWaiting;
        }
        else
        {
            SessionText = Strings.NoSession;
            RateText = "";
        }

        var cash = Briefing.CombatSales(state);
        SetRows(CombatSales, cash.Count > 0 ? cash : [new Chip(Strings.NothingToCash, Tone.Muted)], "cash");
        SetRows(TargetRows, Briefing.Target(state) is { } target ? [target] : [new Chip(Strings.NoTarget, Tone.Muted)], "target");
        var reminders = Briefing.Alerts(state, status).Concat(Briefing.Reminders(state, status, sales: false)).ToList();
        SetRows(Reminders, reminders.Count > 0 ? reminders : [new Chip(Strings.NothingToDo, Tone.Good)], "reminders");
        var sales = Briefing.ExplorationSales(state);
        SetRows(ExplorationSales, sales.Count > 0 ? sales : [new Chip(Strings.NothingToSell, Tone.Muted)], "sales");
        ExplorationRateText = Briefing.ExplorationRate(state, now) is { } exploring ? string.Format(Strings.PerHour, Briefing.Credits(exploring))
            : state.SessionStartUtc is null ? "" : Strings.RateWaiting;
        ExplorationSessionText = state.SessionStartUtc is { } since ? SessionLine(since, now, Loc.Plural(state.ExplorationSessionCount, Strings.ScannedOne, Strings.ScannedMany)) : Strings.NoSession;
        SetRoute(state);
        SetPanel(PanelRow.From(Briefing.Panel(state, status, now)));
        Neutron.Refresh(state);
        SetRanks(state);
    }

    private void SetRanks(GameState state)
    {
        var ranks = Commander.Ranks(state);
        var reputation = Commander.Reputation(state);
        var key = string.Join("|", ranks.Select(r => $"{r.Title}{r.Name}{r.Next}")) + string.Join("|", reputation);
        if (_keys.GetValueOrDefault("ranks") == key)
            return;

        _keys["ranks"] = key;
        HasRanks = ranks.Count > 0;
        Fill(PilotRanks, ranks.Where(r => r.Kind is not (RankKind.Federation or RankKind.Empire)));
        Fill(NavyRanks, ranks.Where(r => r.Kind is RankKind.Federation or RankKind.Empire));
        Fill(ReputationLines, reputation);
    }

    private static void Fill<T>(ObservableCollection<T> rows, IEnumerable<T> items)
    {
        rows.Clear();
        foreach (var item in items)
            rows.Add(item);
    }

    /// <summary>"Depuis 06:13 (1 h 05) · 4 abattus": under the rate of the combat and exploration tabs.</summary>
    private static string SessionLine(DateTime startUtc, DateTime nowUtc, string count)
    {
        var elapsed = nowUtc - startUtc;
        return string.Format(Loc.Culture, Strings.CombatSince, startUtc.ToLocalTime(), (int)elapsed.TotalHours, elapsed.Minutes, count);
    }

    /// <summary>"CMDR Nova · Krait (Krait Mk II)"; a ship without a name of its own shows its type alone.</summary>
    internal static string ShipLine(string? commander, string? name, string? type)
    {
        if (commander is null)
            return Strings.NoGameRead;

        var ship = (string.IsNullOrWhiteSpace(name), string.IsNullOrWhiteSpace(type)) switch
        {
            (false, false) => $"{name} ({type})",
            (false, true) => name,
            (true, false) => type,
            _ => null,
        };
        return ship is null ? $"CMDR {commander}" : $"CMDR {commander} · {ship}";
    }

    /// <summary>The power the hunt looks for: the one chosen in the settings, or else the game's.</summary>
    internal static string? HuntPower(string? setting, string? game) => setting switch
    {
        null or "auto" => game,
        "none" => null,
        _ => setting,
    };

    /// <summary>The choices of power: the game's (named), none, or one of the twelve.</summary>
    public ObservableCollection<KeyValuePair<string, string>> Powers { get; } = [];

    [ObservableProperty] private string _power = "auto";

    partial void OnPowerChanged(string value)
    {
        if (_refreshing || _settings is null || value is null || _settings.Power == value)
            return;
        _settings.Power = value;
        _saveSettings?.Invoke();
    }

    private void SetPowers(string? game)
    {
        var fromGame = game is null ? Strings.PowerFromGameNone : string.Format(Strings.PowerFromGame, game);
        if (Powers.Count > 0 && Powers[0].Value == fromGame)
            return;

        var choices = new[] { new KeyValuePair<string, string>("auto", fromGame), new("none", Strings.PowerNone) }
            .Concat(HuntFinder.Powers.Select(p => new KeyValuePair<string, string>(p, p)));
        Powers.Clear();
        foreach (var choice in choices)
            Powers.Add(choice);
    }

    /// <summary>The languages offered, each named in its own language.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Languages { get; } =
        [new("auto", Strings.LanguageAuto), new("en", "English"), new("fr", "Français")];

    [ObservableProperty] private string _language = "auto";

    partial void OnLanguageChanged(string value)
    {
        if (_refreshing || _settings is null || _settings.Language == value)
            return;
        _settings.Language = value;
        _saveSettings?.Invoke();
    }

    private readonly Dictionary<string, string> _keys = [];

    private void SetRows(ObservableCollection<ChipRow> rows, IReadOnlyList<Chip> chips, string name)
    {
        var key = string.Join("|", chips.Select(c => $"{c.Text}{c.Tone}{c.Detail}"));
        if (_keys.GetValueOrDefault(name) == key)
            return;

        _keys[name] = key;
        rows.Clear();
        foreach (var chip in chips)
            rows.Add(ChipRow.From(chip));
    }

    private void SetPanel(IReadOnlyList<PanelRow> rows)
    {
        var key = string.Join("|", rows.Select(r => $"{r.Text}{r.Header}{r.Loud}{r.Foreground}"));
        if (_keys.GetValueOrDefault("panel") == key)
            return;

        _keys["panel"] = key;
        PanelRows.Clear();
        foreach (var row in rows)
            PanelRows.Add(row);
    }

    private void SetRoute(GameState state)
    {
        var hops = state.HopsAhead;
        var key = string.Join("|", hops.Select(h => h.Address));
        if (key == _routeKey)
            return;

        _routeKey = key;
        RouteRows.Clear();
        var number = 1;
        foreach (var hop in hops)
        {
            var hazard = Stars.Hazard(hop.StarClass);
            var (note, tone) = hop.Scoopable ? (Strings.Scoopable, Tone.Good)
                : hazard is not null ? (hazard, Tone.Warn)
                : (Strings.NotScoopable, Tone.Warn);
            RouteRows.Add(new RouteRow(number++, hop.System, Stars.Label(hop.StarClass), note, CompanionBanner.ToneBrush(tone)));
        }
    }
}
