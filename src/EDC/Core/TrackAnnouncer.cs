namespace EDC.Core;

/// <summary>
/// When to announce a track over the game: each time a different one is heard, in game, music on. A track
/// already announced is not announced again when the music resumes it after a pause.
/// </summary>
public sealed class TrackAnnouncer
{
    private string? _announced;

    /// <summary>True when <paramref name="file"/> should be shown now.</summary>
    public bool Check(string? file, bool playing, bool inGame)
    {
        if (!inGame || !playing || file is null || string.Equals(file, _announced, StringComparison.OrdinalIgnoreCase))
            return false;

        _announced = file;
        return true;
    }

    /// <summary>The game closed: the first track of the next session is announced, even the same one.</summary>
    public void Reset() => _announced = null;
}
