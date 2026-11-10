using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wip.Tholdem.Agents;

namespace Wip.Tholdem.Web.Live;

/// <summary>
/// In-memory registry of live matches. Ids are deterministic 16-character lowercase hex strings
/// derived from the request plus a registry sequence number; no clock, random generator or GUID
/// participates in match identity or play.
/// </summary>
public sealed class LiveMatchRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, LiveMatch> _matches = new(StringComparer.Ordinal);
    private long _sequence;
    private bool _disposed;

    public int Count => _matches.Count;

    /// <summary>
    /// Matches that are running or paused right now. Completed and stopped matches stay retained
    /// (their final snapshot remains readable) but are not "live"; <see cref="Count"/> is therefore
    /// retention, not liveness.
    /// </summary>
    public int ActiveCount => _matches.Values.Count(static match => match.IsActive);

    /// <summary>Starts a two-seat live match (the pre-multiway public surface).</summary>
    public LiveMatch Start(ulong seed, int hands, int delayMs, IPokerAgent seat0, IPokerAgent seat1)
    {
        ArgumentNullException.ThrowIfNull(seat0);
        ArgumentNullException.ThrowIfNull(seat1);

        return Start(seed, hands, delayMs, new[] { seat0, seat1 });
    }

    /// <summary>Starts a two-to-six-seat live match with optional per-seat starting stacks.</summary>
    public LiveMatch Start(
        ulong seed,
        int hands,
        int delayMs,
        IReadOnlyList<IPokerAgent> agents,
        IReadOnlyList<long>? startingStacks = null)
    {
        ArgumentNullException.ThrowIfNull(agents);
        if (agents.Count is < 2 or > 6)
        {
            throw new ArgumentOutOfRangeException(
                nameof(agents),
                agents.Count,
                "A live match must have between 2 and 6 seats.");
        }

        var id = CreateId(seed, hands, delayMs, agents);
        var match = new LiveMatch(id, seed, hands, delayMs, agents, startingStacks);
        if (!_matches.TryAdd(id, match))
        {
            throw new InvalidOperationException($"A live match with id '{id}' already exists.");
        }

        match.Start();
        return match;
    }

    public bool TryGet(string id, out LiveMatch? match) => _matches.TryGetValue(id, out match);

    public bool Stop(string id)
    {
        if (!_matches.TryGetValue(id, out var match))
        {
            return false;
        }

        match.Stop();
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var match in _matches.Values)
        {
            match.Stop();
        }
    }

    private string CreateId(ulong seed, int hands, int delayMs, IReadOnlyList<IPokerAgent> agents)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var parts = new List<string>(agents.Count + 4)
        {
            seed.ToString(CultureInfo.InvariantCulture),
            hands.ToString(CultureInfo.InvariantCulture),
            delayMs.ToString(CultureInfo.InvariantCulture),
        };
        foreach (var agent in agents)
        {
            parts.Add(agent.Name);
        }

        // The sequence keeps two identical requests distinct. For two seats the material is exactly
        // the pre-multiway seed/hands/delay/seat0/seat1/sequence list.
        parts.Add(sequence.ToString(CultureInfo.InvariantCulture));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', parts)));
        return Convert.ToHexString(digest).ToLowerInvariant()[..16];
    }
}
