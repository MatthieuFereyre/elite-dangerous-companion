using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using EDC.Core;
using EDC.Resources;

namespace EDC.Services;

/// <summary>A release published on GitHub: its version, from the tag, and its installer.</summary>
public sealed record Release(Version Version, string Installer);

/// <summary>
/// Updates the installed companion from its GitHub releases: checks the latest at start and every six
/// hours, downloads a newer installer and runs it silently right away, game running or not (it closes
/// this app and starts the new one, the music back within seconds). A build from the sources
/// (version 0.0.0) only reports.
/// </summary>
public sealed class Updater : IDisposable
{
    public const string Repository = "MatthieuFereyre/elite-dangerous-companion";

    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    private readonly PlayerSettings _settings;
    private readonly DispatcherTimer _timer;
    private DateTime _nextCheck = DateTime.UtcNow.AddSeconds(30);
    private bool _checking;

    public Updater(PlayerSettings settings)
    {
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) => await Tick();
    }

    /// <summary>The version of this executable: the release's, or 0.0.0 for one built from the sources.</summary>
    public static Version Current { get; } = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public string StatusText { get; private set; } = IsLocalBuild(Current) ? Strings.UpdateLocalBuild : "";

    /// <summary>Raised on the UI thread when <see cref="StatusText"/> changes.</summary>
    public event Action? Changed;

    public void Start() => _timer.Start();

    internal static bool IsLocalBuild(Version version) => version.Major == 0 && version.Minor == 0 && version.Build <= 0;

    internal static bool IsNewer(Version current, Version latest) =>
        new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0)) > new Version(current.Major, current.Minor, Math.Max(current.Build, 0));

    /// <summary>The latest release as GitHub's API gives it; null without a "vX.Y.Z" tag or an .exe among its files.</summary>
    internal static Release? ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var name) ? name.GetString() : null;
        if (tag is null || !Version.TryParse(tag.TrimStart('v', 'V'), out var version))
            return null;

        var installer = root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array
            ? assets.EnumerateArray()
                .Where(a => a.TryGetProperty("name", out var n) && n.GetString()?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
                .Select(a => a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null)
                .FirstOrDefault(u => u is not null)
            : null;
        return installer is null ? null : new Release(version, installer);
    }

    private async Task Tick()
    {
        if (_settings.AutoUpdate && DateTime.UtcNow >= _nextCheck)
            await CheckAsync();
    }

    /// <summary>From the menu or the settings: checks now, and installs what it finds.</summary>
    public Task CheckNowAsync() => CheckAsync();

    private async Task CheckAsync()
    {
        if (_checking)
            return;

        _checking = true;
        _nextCheck = DateTime.UtcNow + CheckEvery;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            // GitHub's API turns away a request without a User-Agent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("EliteDangerousCompanion");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var release = ParseRelease(await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest"));
            if (release is null)
            {
                SetStatus(string.Format(Strings.UpdateUpToDate, DateTime.Now));
                return;
            }

            if (IsLocalBuild(Current))
            {
                SetStatus(string.Format(Strings.UpdateLocalLatest, release.Version.ToString(3)));
                return;
            }

            if (!IsNewer(Current, release.Version))
            {
                SetStatus(string.Format(Strings.UpdateUpToDate, DateTime.Now));
                return;
            }

            var path = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(release.Installer).LocalPath));
            await using (var source = await http.GetStreamAsync(release.Installer))
            await using (var target = File.Create(path))
                await source.CopyToAsync(target);

            AppLog.Write($"mise à jour : version {release.Version.ToString(3)} téléchargée ({path})");
            SetStatus(string.Format(Strings.UpdateInstalling, release.Version.ToString(3)));
            Install(path, release.Version);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException or InvalidOperationException or UriFormatException)
        {
            AppLog.Write($"mise à jour non vérifiée : {ex.Message}");
            SetStatus(string.Format(Strings.UpdateCheckFailed, DateTime.Now));
        }
        finally
        {
            _checking = false;
        }
    }

    private void Install(string installer, Version version)
    {
        try
        {
            AppLog.Write($"mise à jour : installation de la version {version.ToString(3)}");
            // Silent: the installer closes this app, replaces it and starts the new one.
            Process.Start(new ProcessStartInfo(installer, "/S") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            AppLog.Write($"mise à jour non installée : {ex.Message}");
            SetStatus(Strings.UpdateInstallFailed);
        }
    }

    private void SetStatus(string text)
    {
        StatusText = text;
        Changed?.Invoke();
    }

    public void Dispose() => _timer.Stop();
}
