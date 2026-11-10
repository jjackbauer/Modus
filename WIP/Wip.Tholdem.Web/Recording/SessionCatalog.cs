using System.Security.Cryptography;
using System.Text.Json;

namespace Wip.Tholdem.Web.Recording;

/// <summary>One recorded match directory under <c>&lt;data&gt;/sessions/*/session.json</c>.</summary>
public sealed record SessionSummary(
    string SessionId,
    ulong Seed,
    int Hands,
    long StartingStack,
    long SmallBlind,
    long BigBlind,
    IReadOnlyList<string> Agents,
    string ManifestHash,
    int HandCount,
    int Seats,
    IReadOnlyList<long> StartingStacks);

/// <summary>The GET /api/sessions response body.</summary>
public sealed record SessionList(IReadOnlyList<SessionSummary> Sessions, int Skipped);

/// <summary>One awarded pot layer, readable from a v2 hand record.</summary>
public sealed record RecordedPot(long Amount, IReadOnlyList<int> Winners);

/// <summary>
/// One hand summary inside a session detail response. <see cref="HoleCards"/> holds one two-card
/// list per seat (2–6), <see cref="FinalStacks"/> one stack per seat and <see cref="Pots"/> the
/// recorded pot layers; a v1 two-seat hand has no layers, so its list is empty.
/// </summary>
public sealed record HandSummary(
    int HandNumber,
    IReadOnlyList<string> Board,
    IReadOnlyList<int> Winners,
    bool Showdown,
    long PotAwarded,
    IReadOnlyList<long> FinalStacks,
    IReadOnlyList<string> HandNames,
    int ButtonSeat,
    IReadOnlyList<IReadOnlyList<string>> HoleCards,
    IReadOnlyList<RecordedPot> Pots);

/// <summary>The GET /api/sessions/{sessionId} response body: the manifest plus per-hand summaries.</summary>
public sealed record SessionDetail(
    string Schema,
    string SessionId,
    ulong Seed,
    long StartingStack,
    long SmallBlind,
    long BigBlind,
    IReadOnlyList<string> Agents,
    IReadOnlyList<string> HandHashes,
    string ManifestHash,
    int HandCount,
    IReadOnlyList<HandSummary> Hands,
    int Seats,
    IReadOnlyList<long> StartingStacks);

/// <summary>One recorded action, as stored in a canonical hand-history file.</summary>
public sealed record RecordedAction(int Street, int Seat, string Kind, long Amount, long PotAfter);

/// <summary>
/// A recorded hand loaded for the server-rendered replay table and the replay JSON API.
/// <see cref="HoleCards"/> is one list per seat (2–6); <see cref="HoleCardsSeat0"/> and
/// <see cref="HoleCardsSeat1"/> are the pre-multiway two-seat fields and stay present in the JSON.
/// </summary>
public sealed record RecordedHand(
    int HandNumber,
    ulong Seed,
    long SmallBlind,
    long BigBlind,
    long StartingStack,
    int ButtonSeat,
    IReadOnlyList<IReadOnlyList<string>> HoleCards,
    IReadOnlyList<string> Board,
    IReadOnlyList<RecordedAction> Actions,
    bool Showdown,
    IReadOnlyList<int> Winners,
    long PotAwarded,
    IReadOnlyList<long> FinalStacks,
    IReadOnlyList<string> Agents,
    int Seats,
    IReadOnlyList<long> StartingStacks,
    IReadOnlyList<RecordedPot> Pots)
{
    public IReadOnlyList<string> HoleCardsSeat0 =>
        HoleCards.Count > 0 ? HoleCards[0] : [];

    public IReadOnlyList<string> HoleCardsSeat1 =>
        HoleCards.Count > 1 ? HoleCards[1] : [];

    /// <summary>The best-five name per seat, <c>"Preflop"</c> before the flop.</summary>
    public IReadOnlyList<string> HandNames => HandNaming.ForSeats(Board, HoleCards);

    public HandSummary ToSummary() =>
        new(HandNumber, Board, Winners, Showdown, PotAwarded, FinalStacks, HandNames, ButtonSeat, HoleCards, Pots);
}

/// <summary>
/// Read-only access to the frozen data directory. Malformed manifests and hand files are skipped
/// and counted; nothing under the data directory is ever written by this class.
/// </summary>
public static class SessionCatalog
{
    public static SessionList List(string dataDirectory)
    {
        var sessionsRoot = Path.Combine(dataDirectory, "sessions");
        if (!Directory.Exists(sessionsRoot))
        {
            return new SessionList([], 0);
        }

        var sessions = new List<SessionSummary>();
        var skipped = 0;
        foreach (var directory in Directory.EnumerateDirectories(sessionsRoot).Order(StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(directory, "session.json");
            if (!TryReadManifest(manifestPath, out var manifest))
            {
                skipped++;
                continue;
            }

            sessions.Add(manifest.ToSummary());
        }

        sessions.Sort(static (left, right) => string.CompareOrdinal(left.SessionId, right.SessionId));
        return new SessionList(sessions, skipped);
    }

    public static int CountSessions(string dataDirectory) => List(dataDirectory).Sessions.Count;

    public static SessionDetail? TryGetDetail(string dataDirectory, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var directory = FindSessionDirectory(dataDirectory, sessionId);
        if (directory is null)
        {
            return null;
        }

        var manifestPath = Path.Combine(directory, "session.json");
        if (!TryReadManifest(manifestPath, out var manifest))
        {
            return null;
        }

        return new SessionDetail(
            manifest.Schema,
            manifest.SessionId,
            manifest.Seed,
            manifest.StartingStack,
            manifest.SmallBlind,
            manifest.BigBlind,
            manifest.Agents,
            manifest.HandHashes,
            manifest.ManifestHash,
            manifest.HandCount,
            ReadHandSummaries(directory),
            manifest.Seats,
            manifest.StartingStacks);
    }

    public static RecordedHand? TryReadHand(string dataDirectory, string sessionId, int handNumber)
    {
        if (handNumber < 1 || string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var directory = FindSessionDirectory(dataDirectory, sessionId);
        if (directory is null)
        {
            return null;
        }

        var handPath = Path.Combine(directory, $"hand-{handNumber:D5}.json");
        if (!File.Exists(handPath))
        {
            return null;
        }

        var recorded = TryReadHandFile(handPath);
        if (recorded is null)
        {
            return null;
        }

        // The seat identity lives in the session manifest; expose it with the hand so the replay
        // surface never has to put a stack label in the agent slot.
        var manifestPath = Path.Combine(directory, "session.json");
        return TryReadManifest(manifestPath, out var manifest)
            ? recorded with { Agents = manifest.Agents }
            : recorded;
    }

    private static string? FindSessionDirectory(string dataDirectory, string sessionId)
    {
        var sessionsRoot = Path.Combine(dataDirectory, "sessions");
        if (!Directory.Exists(sessionsRoot))
        {
            return null;
        }

        foreach (var directory in Directory.EnumerateDirectories(sessionsRoot).Order(StringComparer.Ordinal))
        {
            if (string.Equals(Path.GetFileName(directory), sessionId, StringComparison.Ordinal))
            {
                return directory;
            }

            var manifestPath = Path.Combine(directory, "session.json");
            if (TryReadManifest(manifestPath, out var manifest)
                && string.Equals(manifest.SessionId, sessionId, StringComparison.Ordinal))
            {
                return directory;
            }
        }

        return null;
    }

    private static IReadOnlyList<HandSummary> ReadHandSummaries(string directory)
    {
        var summaries = new List<HandSummary>();
        foreach (var path in Directory.EnumerateFiles(directory, "hand-*.json").Order(StringComparer.Ordinal))
        {
            var hand = TryReadHandFile(path);
            if (hand is not null)
            {
                summaries.Add(hand.ToSummary());
            }
        }

        summaries.Sort(static (left, right) => left.HandNumber.CompareTo(right.HandNumber));
        return summaries;
    }

    private static bool TryReadManifest(string manifestPath, out ManifestData manifest)
    {
        manifest = null!;
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(manifestPath);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetString(root, "sessionId", out var sessionId)
                || !root.TryGetProperty("seed", out var seed)
                || seed.ValueKind != JsonValueKind.Number
                || !root.TryGetProperty("hands", out var hands)
                || hands.ValueKind != JsonValueKind.Number
                || !root.TryGetProperty("startingStack", out var startingStack)
                || startingStack.ValueKind != JsonValueKind.Number
                || !root.TryGetProperty("smallBlind", out var smallBlind)
                || smallBlind.ValueKind != JsonValueKind.Number
                || !root.TryGetProperty("bigBlind", out var bigBlind)
                || bigBlind.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            var schema = TryGetString(root, "schema", out var parsedSchema)
                ? parsedSchema
                : "tholdem.session.v1";
            var seats = root.TryGetProperty("seats", out var seatsElement)
                && seatsElement.ValueKind == JsonValueKind.Number
                && seatsElement.TryGetInt32(out var parsedSeats)
                ? parsedSeats
                : 2;
            if (seats is < 2 or > 6)
            {
                return false;
            }

            var scalarStartingStack = startingStack.GetInt64();
            long[] startingStacks;
            if (root.TryGetProperty("startingStacks", out var stacksElement)
                && stacksElement.ValueKind == JsonValueKind.Array)
            {
                if (stacksElement.GetArrayLength() != seats)
                {
                    return false;
                }

                startingStacks = stacksElement.EnumerateArray()
                    .Select(static item => item.GetInt64())
                    .ToArray();
            }
            else
            {
                startingStacks = new long[seats];
                Array.Fill(startingStacks, scalarStartingStack);
            }

            var agents = new List<string>();
            if (root.TryGetProperty("agents", out var agentsElement) && agentsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var agent in agentsElement.EnumerateArray())
                {
                    if (agent.ValueKind == JsonValueKind.String)
                    {
                        agents.Add(agent.GetString()!);
                    }
                }
            }

            var handHashes = new List<string>();
            if (root.TryGetProperty("handHashes", out var hashesElement) && hashesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var hash in hashesElement.EnumerateArray())
                {
                    if (hash.ValueKind == JsonValueKind.String)
                    {
                        handHashes.Add(hash.GetString()!);
                    }
                }
            }

            var handCount = Directory.EnumerateFiles(Path.GetDirectoryName(manifestPath)!, "hand-*.json").Count();
            manifest = new ManifestData(
                schema,
                sessionId,
                seed.GetUInt64(),
                hands.GetInt32(),
                scalarStartingStack,
                smallBlind.GetInt64(),
                bigBlind.GetInt64(),
                agents.ToArray(),
                handHashes.ToArray(),
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                handCount,
                seats,
                startingStacks);
            return true;
        }
        catch (Exception exception) when (IsTolerableReadFailure(exception))
        {
            return false;
        }
    }

    private static RecordedHand? TryReadHandFile(string handPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(handPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var actions = new List<RecordedAction>();
            if (root.TryGetProperty("actions", out var actionsElement) && actionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var action in actionsElement.EnumerateArray())
                {
                    if (action.ValueKind != JsonValueKind.Object)
                    {
                        return null;
                    }

                    actions.Add(new RecordedAction(
                        action.GetProperty("street").GetInt32(),
                        action.GetProperty("seat").GetInt32(),
                        action.GetProperty("kind").GetString()!,
                        action.GetProperty("amount").GetInt64(),
                        action.GetProperty("potAfter").GetInt64()));
                }
            }

            var seats = root.TryGetProperty("seats", out var seatsElement)
                && seatsElement.ValueKind == JsonValueKind.Number
                && seatsElement.TryGetInt32(out var parsedSeats)
                ? parsedSeats
                : 2;
            if (seats is < 2 or > 6)
            {
                return null;
            }

            IReadOnlyList<IReadOnlyList<string>> holeCards;
            if (root.TryGetProperty("holeCards", out var holeCardsElement)
                && holeCardsElement.ValueKind == JsonValueKind.Array)
            {
                // v2 records the whole table; the list order is seat order.
                var hands = new List<IReadOnlyList<string>>(holeCardsElement.GetArrayLength());
                foreach (var handElement in holeCardsElement.EnumerateArray())
                {
                    hands.Add(handElement.ValueKind == JsonValueKind.Array
                        ? handElement.EnumerateArray()
                            .Where(static card => card.ValueKind == JsonValueKind.String)
                            .Select(static card => card.GetString()!)
                            .ToArray()
                        : []);
                }

                holeCards = hands;
            }
            else
            {
                // v1 records only seats 0 and 1 (the pre-multiway form).
                holeCards = new[] { ReadCards(root, "holeCardsSeat0"), ReadCards(root, "holeCardsSeat1") };
            }

            var scalarStartingStack = root.GetProperty("startingStack").GetInt64();
            var startingStacks = root.TryGetProperty("startingStacks", out var stacksElement)
                ? stacksElement.EnumerateArray().Select(static item => item.GetInt64()).ToArray()
                : Enumerable.Repeat(scalarStartingStack, seats).ToArray();

            var pots = new List<RecordedPot>();
            if (root.TryGetProperty("pots", out var potsElement) && potsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var pot in potsElement.EnumerateArray())
                {
                    pots.Add(new RecordedPot(
                        pot.GetProperty("amount").GetInt64(),
                        pot.GetProperty("winners").EnumerateArray()
                            .Select(static winner => winner.GetInt32())
                            .ToArray()));
                }
            }

            return new RecordedHand(
                root.GetProperty("handNumber").GetInt32(),
                root.GetProperty("seed").GetUInt64(),
                root.GetProperty("smallBlind").GetInt64(),
                root.GetProperty("bigBlind").GetInt64(),
                scalarStartingStack,
                root.GetProperty("buttonSeat").GetInt32(),
                holeCards,
                ReadCards(root, "board"),
                actions,
                root.GetProperty("showdown").GetBoolean(),
                ReadInts(root, "winners"),
                root.GetProperty("potAwarded").GetInt64(),
                ReadLongs(root, "finalStacks"),
                [],
                seats,
                startingStacks,
                pots);
        }
        catch (Exception exception) when (IsTolerableReadFailure(exception))
        {
            return null;
        }
    }

    private static bool TryGetString(JsonElement root, string property, out string value)
    {
        value = string.Empty;
        if (root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString()!;
            return true;
        }

        return false;
    }

    private static string[] ReadCards(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray()
            .Where(static card => card.ValueKind == JsonValueKind.String)
            .Select(static card => card.GetString()!)
            .ToArray();
    }

    private static int[] ReadInts(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray().Select(static item => item.GetInt32()).ToArray();
    }

    private static long[] ReadLongs(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray().Select(static item => item.GetInt64()).ToArray();
    }

    private static bool IsTolerableReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
            or FormatException or OverflowException or KeyNotFoundException;

    private sealed record ManifestData(
        string Schema,
        string SessionId,
        ulong Seed,
        int Hands,
        long StartingStack,
        long SmallBlind,
        long BigBlind,
        string[] Agents,
        string[] HandHashes,
        string ManifestHash,
        int HandCount,
        int Seats,
        long[] StartingStacks)
    {
        public SessionSummary ToSummary() =>
            new(
                SessionId,
                Seed,
                Hands,
                StartingStack,
                SmallBlind,
                BigBlind,
                Agents,
                ManifestHash,
                HandCount,
                Seats,
                StartingStacks);
    }
}
