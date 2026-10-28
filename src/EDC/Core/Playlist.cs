namespace EDC.Core;

/// <summary>
/// Shuffled rounds over the tracks of one mode. A liked track enters a round <c>likeWeight</c> times
/// instead of once, never twice in a row. Eligibility is checked at each draw, so a track switched to
/// "off" or to the other mode leaves the round at once, and one reclassified into this mode joins it.
/// </summary>
public sealed class Playlist(Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;
    private readonly List<string> _round = [];
    private readonly HashSet<string> _roundMembers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _played = [];

    /// <summary>What is left of the round, in drawing order; a liked track can be there twice.</summary>
    public IReadOnlyList<string> Upcoming => _round;

    /// <summary>The tracks drawn this round, oldest first, led by the last one of the round before.</summary>
    public IReadOnlyList<string> Played => _played;

    /// <summary>The next file to play among <paramref name="eligible"/>, or null when there is none.</summary>
    public string? Next(IReadOnlyList<Track> eligible, int likeWeight, string? previous)
    {
        if (eligible.Count == 0)
            return null;

        var allowed = eligible.Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        JoinNewcomers(allowed);

        _round.RemoveAll(f => !allowed.Contains(f));
        if (_round.Count == 0)
            StartRound(eligible, likeWeight, previous);

        // Skips the track just played when something else is left; otherwise plays it again rather than nothing.
        var index = Math.Max(0, _round.FindIndex(f => !f.Equals(previous, StringComparison.OrdinalIgnoreCase)));
        var file = _round[index];
        _round.RemoveAt(index);
        _played.Add(file);
        return file;
    }

    /// <summary>Takes <paramref name="file"/> out of the round, to play it now rather than in its turn.</summary>
    public void Take(string file)
    {
        var index = _round.FindIndex(f => f.Equals(file, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            _round.RemoveAt(index);
        _played.Add(file);
    }

    private void JoinNewcomers(HashSet<string> allowed)
    {
        if (_roundMembers.Count == 0)
            return;

        foreach (var file in allowed.Where(f => !_roundMembers.Contains(f)))
        {
            _round.Insert(_random.Next(_round.Count + 1), file);
            _roundMembers.Add(file);
        }
    }

    private void StartRound(IReadOnlyList<Track> eligible, int likeWeight, string? previous)
    {
        _round.Clear();
        _roundMembers.Clear();
        // Kept so the track just heard can still be found, and liked, once the round turns.
        _played.Clear();
        if (previous is not null)
            _played.Add(previous);

        foreach (var track in eligible)
        {
            _roundMembers.Add(track.Key);
            for (var n = 0; n < (track.Like ? likeWeight : 1); n++)
                _round.Add(track.Key);
        }

        // Fisher–Yates, then move apart any copy that lands next to its twin (or after the last track played).
        for (var i = _round.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (_round[i], _round[j]) = (_round[j], _round[i]);
        }

        for (var i = 0; i < _round.Count; i++)
        {
            var before = i == 0 ? previous : _round[i - 1];
            if (!_round[i].Equals(before, StringComparison.OrdinalIgnoreCase))
                continue;

            var swap = Enumerable.Range(0, _round.Count)
                .OrderBy(_ => _random.Next())
                .FirstOrDefault(j => j != i && CanSwap(i, j, previous), -1);
            if (swap >= 0)
                (_round[i], _round[swap]) = (_round[swap], _round[i]);
        }
    }

    private bool CanSwap(int i, int j, string? previous)
    {
        bool Fits(int at, string value)
        {
            var before = at == 0 ? previous : (at - 1 == i ? _round[j] : at - 1 == j ? _round[i] : _round[at - 1]);
            var after = at + 1 >= _round.Count ? null : (at + 1 == i ? _round[j] : at + 1 == j ? _round[i] : _round[at + 1]);
            return !value.Equals(before, StringComparison.OrdinalIgnoreCase) && !value.Equals(after, StringComparison.OrdinalIgnoreCase);
        }

        return Fits(i, _round[j]) && Fits(j, _round[i]);
    }
}
