using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDC.Companion;
using EDC.Core;
using EDC.Resources;

namespace EDC.ViewModels;

/// <summary>A step of the list: done once reached, the next one picked out.</summary>
public sealed partial class NeutronRow(int number, NeutronStep step, string? previous) : ObservableObject
{

    public int Number { get; } = number;
    public string System => step.System;
    public bool Neutron => step.Neutron;

    /// <summary>The jumps counted from the step before: the game plots them once the system is in the galaxy map.</summary>
    public string Detail => previous is null
        ? string.Format(Strings.NeutronStart, Distance(step.DistanceLeft))
        : string.Format(Strings.NeutronFrom, Loc.Plural(step.Jumps, Strings.JumpsOne, Strings.JumpsMany), previous) + (step.Neutron ? Strings.NeutronStar : "")
          + (step.DistanceLeft > 0 ? string.Format(Strings.NeutronLeft, Distance(step.DistanceLeft)) : Strings.NeutronArrival);

    internal static string Distance(double ly) => string.Format(Strings.LightYears, ly.ToString("N0", Loc.Culture));

    [ObservableProperty] private bool _done;
    [ObservableProperty] private bool _isNext;
}

/// <summary>
/// The Route tab: a route plotted by Spansh through neutron stars from here to a system, or any route of
/// Spansh's given by the link of its results (pasted here, or passed with --route); each step copied by
/// a click, the steps reached as the ship arrives there, and the route kept for the next session.
/// </summary>
public sealed partial class NeutronViewModel : ObservableObject
{

    private readonly PlayerSettings _settings;
    private readonly Action _save;
    private readonly Func<string, bool> _copy;
    private readonly Func<string, string, double, CancellationToken, Task<NeutronRoute>> _plot;
    private readonly Func<string, CancellationToken, Task<NeutronRoute>> _import;
    private NeutronRoute? _route;
    private string? _system;
    private bool _fromTyped;
    private bool _rangeTyped;
    private long _rangeShip = -1;
    private bool _filling;

    public NeutronViewModel(PlayerSettings settings, Action save, Func<string, bool>? copy = null,
        Func<string, string, double, CancellationToken, Task<NeutronRoute>>? plot = null,
        Func<string, CancellationToken, Task<NeutronRoute>>? import = null)
    {
        _settings = settings;
        _save = save;
        _copy = copy ?? CopyToClipboard;
        _plot = plot ?? NeutronPlotter.PlotAsync;
        _import = import ?? NeutronPlotter.ImportAsync;
        _autoCopy = settings.NeutronAutoCopy;
        _showPlotter = settings.ShowNeutronPlotter;
        if (settings.NeutronRoute is { } kept)
        {
            _filling = true;
            From = kept.From;
            To = kept.To;
            RangeText = kept.Range.ToString("0.##", Loc.Culture);
            _filling = false;
            // The range is the current ship's, read at the first refresh: the route may come from another.
            _fromTyped = true;
            Show(kept);
        }
    }

    [ObservableProperty] private string _from = "";
    [ObservableProperty] private string _to = "";
    [ObservableProperty] private string _rangeText = "";
    [ObservableProperty] private string _statusText = Strings.PlotTip;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private NeutronRow? _next;
    [ObservableProperty] private bool _autoCopy;
    [ObservableProperty] private bool _plotting;
    [ObservableProperty] private string _link = "";
    [ObservableProperty] private bool _showPlotter;

    public ObservableCollection<NeutronRow> Steps { get; } = [];

    // What the commander types stays: the game no longer fills it.
    partial void OnFromChanged(string value) => _fromTyped |= !_filling;
    partial void OnRangeTextChanged(string value) => _rangeTyped |= !_filling;

    partial void OnAutoCopyChanged(bool value)
    {
        _settings.NeutronAutoCopy = value;
        _save();
    }

    partial void OnShowPlotterChanged(bool value)
    {
        _settings.ShowNeutronPlotter = value;
        _save();
    }

    /// <summary>
    /// From the game, twice a second: the start until typed, the range until typed for this ship
    /// (another ship brings its own), and the steps reached.
    /// </summary>
    public void Refresh(GameState state)
    {
        _system = state.System;
        if (state.ShipId != _rangeShip)
        {
            _rangeShip = state.ShipId;
            _rangeTyped = false;
        }

        _filling = true;
        if (!_fromTyped && state.System is { } system)
            From = system;
        if (!_rangeTyped && state.MaxJumpRange > 0)
            RangeText = state.MaxJumpRange.ToString("0.##", Loc.Culture);
        _filling = false;

        if (_route is null)
            return;

        var advanced = _route.Advance(state.System);
        if (advanced.Reached == _route.Reached)
            return;

        _route = advanced;
        _settings.NeutronRoute = advanced;
        _save();
        Mark();
        if (AutoCopy && advanced.Next is { } next)
            Copy(next.System);
    }

    [RelayCommand]
    private async Task Plot()
    {
        if (string.IsNullOrWhiteSpace(To))
        {
            StatusText = Strings.NeutronNeedTo;
            return;
        }

        if (string.IsNullOrWhiteSpace(From))
        {
            StatusText = Strings.NeutronNeedFrom;
            return;
        }

        if (!double.TryParse(RangeText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var range) || range <= 0)
        {
            StatusText = Strings.NeutronNeedRange;
            return;
        }

        StatusText = string.Format(Strings.NeutronPlotting, From.Trim(), To.Trim());
        Plotting = true;
        try
        {
            var route = await _plot(From.Trim(), To.Trim(), range, CancellationToken.None);
            _settings.NeutronRoute = route;
            _save();
            Show(route);
            // Over a short way no neutron star saves time: Spansh then gives the end alone.
            StatusText = route.Steps.Any(s => s.Neutron) || route.Next is not { } end
                ? Strings.NeutronClickStep
                : string.Format(Strings.NeutronNoGain, end.System, Loc.Plural(end.Jumps, Strings.JumpsOne, Strings.JumpsMany));
        }
        catch (NeutronException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            StatusText = string.Format(Strings.NeutronFailed, ex.Message);
            AppLog.Write($"neutrons : calcul impossible de {From} à {To} ({ex.Message})");
        }
        finally
        {
            Plotting = false;
        }
    }

    [RelayCommand]
    private Task Load() => ImportAsync(Link);

    /// <summary>
    /// A route of Spansh's from the link of its results. Away from it, its first system is the next;
    /// from one of its systems, it goes on from there.
    /// </summary>
    public async Task ImportAsync(string link)
    {
        StatusText = Strings.RouteLoading;
        Plotting = true;
        try
        {
            var route = ((await _import(link.Trim(), CancellationToken.None)) with { Reached = -1 }).Advance(_system);
            _settings.NeutronRoute = route;
            _save();
            Show(route);
            StatusText = string.Format(Strings.RouteLoaded, Loc.Plural(route.Steps.Count, Strings.StepsOne, Strings.StepsMany));
            if (AutoCopy && route.Next is { } next)
                Copy(next.System);
        }
        catch (NeutronException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            StatusText = string.Format(Strings.NeutronFailed, ex.Message);
            AppLog.Write($"route Spansh : lecture impossible de {link} ({ex.Message})");
        }
        finally
        {
            Plotting = false;
        }
    }

    [RelayCommand]
    private void CopyStep(NeutronRow? row)
    {
        if (row is not null)
            Copy(row.System);
    }

    [RelayCommand]
    private void Clear()
    {
        _route = null;
        _settings.NeutronRoute = null;
        _save();
        Steps.Clear();
        Next = null;
        Summary = "";
    }

    private void Copy(string system) =>
        StatusText = _copy(system)
            ? string.Format(Strings.Copied, system)
            : Strings.ClipboardBusy;

    private void Show(NeutronRoute route)
    {
        _route = route;
        Steps.Clear();
        for (var i = 0; i < route.Steps.Count; i++)
            Steps.Add(new NeutronRow(i, route.Steps[i], i > 0 ? route.Steps[i - 1].System : null));

        var count = route.Steps.Count;
        Summary = $"{Loc.Plural(count, Strings.StepsOne, Strings.StepsMany)} · {Loc.Plural(route.TotalJumps, Strings.JumpsOne, Strings.JumpsMany)} · {NeutronRow.Distance(route.Distance)}";
        Mark();
    }

    private void Mark()
    {
        if (_route is null)
            return;

        foreach (var row in Steps)
        {
            row.Done = row.Number <= _route.Reached;
            row.IsNext = row.Number == _route.Reached + 1;
        }

        Next = Steps.FirstOrDefault(s => s.IsNext);
    }

    private static bool CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (COMException)
        {
            // Another program holds the clipboard for a moment.
            return false;
        }
    }
}
