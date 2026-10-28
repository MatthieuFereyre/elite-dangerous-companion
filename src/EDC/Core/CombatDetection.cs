using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace EDC.Core;

public sealed record CombatSignal(DateTime Time, string Why);

/// <summary>
/// Combat as the game itself decides it, read from its journal:
/// <list type="bullet">
/// <item>"Music" events whose MusicTrack starts with "Combat_" (the game switching to its combat score);</item>
/// <item>the events UnderAttack, ShieldState, HullDamage, Interdicted and Bounty.</item>
/// </list>
/// Not the "in danger" flag of Status.json: the game also raises it while overheating near a star.
/// </summary>
public static partial class CombatSignals
{
    private static readonly HashSet<string> CombatEvents = ["UnderAttack", "ShieldState", "HullDamage", "Interdicted", "Bounty"];

    [GeneratedRegex("\"event\":\"(\\w+)\"")]
    private static partial Regex EventPattern();

    [GeneratedRegex("\"MusicTrack\":\"(\\w+)\"")]
    private static partial Regex MusicTrackPattern();

    [GeneratedRegex("\"timestamp\":\"([^\"]+)\"")]
    private static partial Regex TimestampPattern();

    /// <summary>The combat signal carried by one journal line, or null.</summary>
    public static CombatSignal? Parse(string line)
    {
        var match = EventPattern().Match(line);
        if (!match.Success)
            return null;

        var name = match.Groups[1].Value;
        var track = MusicTrackPattern().Match(line);
        if (name == "Music")
        {
            if (!track.Success || !track.Groups[1].Value.StartsWith("Combat_", StringComparison.Ordinal))
                return null;
        }
        else if (!CombatEvents.Contains(name))
        {
            return null;
        }

        var why = track.Success ? $"{name} {track.Groups[1].Value}" : name;
        return new CombatSignal(Timestamp(line) ?? DateTime.UtcNow, why);
    }

    public static DateTime? Timestamp(string line)
    {
        var match = TimestampPattern().Match(line);
        return match.Success && DateTime.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : null;
    }
}

/// <summary>Combat lasts as long as the last signal is younger than the calm delay: each signal pushes it back.</summary>
public sealed class CombatClock(TimeSpan calmDelay)
{
    public TimeSpan CalmDelay { get; } = calmDelay;
    public DateTime LastSignal { get; private set; } = DateTime.MinValue;
    public string? LastWhy { get; private set; }

    public void Record(DateTime time, string why)
    {
        LastSignal = time;
        LastWhy = why;
    }

    public void Reset()
    {
        LastSignal = DateTime.MinValue;
        LastWhy = null;
    }

    public bool IsCombat(DateTime nowUtc) => nowUtc - LastSignal < CalmDelay;
}

/// <summary>
/// Follows the newest Journal.*.log: only the lines added since <see cref="Start"/>, and a journal that
/// appears later is read from its beginning.
/// </summary>
public sealed class JournalTail(string directory)
{
    private string? _path;
    private long _offset;

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "Frontier Developments", "Elite Dangerous");

    /// <summary>Skips what the current journal already holds: only what happens from now on counts.</summary>
    public void Start()
    {
        var latest = Latest();
        _path = latest?.FullName;
        _offset = latest?.Length ?? 0;
    }

    public List<string> ReadNewLines()
    {
        var lines = new List<string>();
        var latest = Latest();
        if (latest is null)
            return lines;

        if (!latest.FullName.Equals(_path, StringComparison.OrdinalIgnoreCase))
        {
            _path = latest.FullName;
            _offset = 0;
        }

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length <= _offset)
            return lines;

        stream.Seek(_offset, SeekOrigin.Begin);
        var buffer = new byte[stream.Length - _offset];
        stream.ReadExactly(buffer);

        // A line still being written is left for the next pass.
        var end = Array.LastIndexOf(buffer, (byte)'\n');
        if (end < 0)
            return lines;

        _offset += end + 1;
        lines.AddRange(Encoding.UTF8.GetString(buffer, 0, end + 1)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return lines;
    }

    private FileInfo? Latest()
    {
        if (!Directory.Exists(directory))
            return null;

        return new DirectoryInfo(directory)
            .EnumerateFiles("Journal.*.log")
            .MaxBy(f => f.LastWriteTimeUtc);
    }
}

/// <summary>Replays a journal at its own timestamps and lists the switches, with no sound.</summary>
public static class JournalReplay
{
    public static IEnumerable<string> Run(IEnumerable<string> lines, TimeSpan calmDelay)
    {
        var clock = new CombatClock(calmDelay);
        var combat = false;
        var started = false;

        foreach (var line in lines)
        {
            if (CombatSignals.Timestamp(line) is not DateTime now)
                continue;

            if (!started)
            {
                started = true;
                yield return string.Format(Resources.Strings.ReplayStart, now.ToLocalTime());
            }

            if (CombatSignals.Parse(line) is { } signal)
                clock.Record(signal.Time, signal.Why);

            var next = clock.IsCombat(now);
            if (next == combat)
                continue;

            combat = next;
            var why = combat ? clock.LastWhy : string.Format(Resources.Strings.SwitchNoSignal, (int)calmDelay.TotalSeconds);
            yield return $"{now.ToLocalTime():HH:mm:ss}  -> {(combat ? "combat" : "calm"),-6}  ({why})";
        }
    }
}
