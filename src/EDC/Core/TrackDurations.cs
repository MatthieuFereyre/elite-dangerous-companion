using System.Collections.Concurrent;
using System.IO;
using NAudio.Wave;

namespace EDC.Core;

/// <summary>
/// The length of each track, for the playlist: read in the background the first time it is asked
/// for, then kept for the session. A file that will not open is not tried again.
/// </summary>
public sealed class TrackDurations
{
    private readonly ConcurrentDictionary<string, TimeSpan?> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised from a worker thread once a batch of durations has been read.</summary>
    public event Action? Changed;

    public TimeSpan? Get(string path) => _known.GetValueOrDefault(path);

    /// <summary>Reads, in the background, the durations not known yet.</summary>
    public void Request(IEnumerable<string> paths)
    {
        var missing = paths.Where(p => !_known.ContainsKey(p) && _queued.TryAdd(p, 0)).ToList();
        if (missing.Count == 0)
            return;

        Task.Run(() =>
        {
            foreach (var path in missing)
            {
                _known[path] = Read(path);
                _queued.TryRemove(path, out _);
            }

            Changed?.Invoke();
        });
    }

    private static TimeSpan? Read(string path)
    {
        try
        {
            using var reader = new MediaFoundationReader(path);
            return reader.TotalTime;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return null;
        }
    }
}
