using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wip.Bones.Benchmark;

public static class HistoryScanner
{
    private const int StudyPairsPerSeat = 12;

    public static HistorySnapshot Scan(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        var sessionsRoot = Path.Combine(dataDirectory, ".wip", "sessions");
        if (!Directory.Exists(sessionsRoot))
            throw new DirectoryNotFoundException($"No sessions directory at '{sessionsRoot}'.");

        var decisions = new List<PromotionDecisionRecord>();
        var rawPairs = new List<RawPair>();
        var strategyArtifactCount = 0;
        var promotionArtifactCount = 0;
        var sessionCount = 0;
        var identicalSourceDecisions = 0;
        var identicalSourcePromoted = 0;

        foreach (var sessionDirectory in Directory.EnumerateDirectories(sessionsRoot))
        {
            sessionCount++;
            var artifactsDirectory = Path.Combine(sessionDirectory, "artifacts");
            if (!Directory.Exists(artifactsDirectory))
                continue;

            var strategiesInSession = new Dictionary<string, SessionStrategy>(StringComparer.Ordinal);
            var promotionFiles = new List<string>();

            foreach (var file in Directory.EnumerateFiles(artifactsDirectory, "*.json"))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.EndsWith(".descriptor.json", StringComparison.Ordinal))
                    continue;

                if (fileName.StartsWith("bones-strategy-promotion", StringComparison.Ordinal))
                {
                    promotionArtifactCount++;
                    promotionFiles.Add(file);
                }
                else if (fileName.StartsWith("bones-player-", StringComparison.Ordinal)
                    && fileName.Contains("-strategy-", StringComparison.Ordinal))
                {
                    strategyArtifactCount++;
                    var strategy = TryReadStrategy(file);
                    if (strategy is null)
                        continue;

                    if (!strategiesInSession.TryGetValue(strategy.Id, out var existing)
                        || strategy.Source.Length > existing.Source.Length)
                    {
                        strategiesInSession[strategy.Id] = strategy;
                    }
                }
            }

            foreach (var promotionFile in promotionFiles)
            {
                var decision = TryReadDecision(promotionFile);
                if (decision is null)
                    continue;

                decisions.Add(decision);

                if (!strategiesInSession.TryGetValue(decision.IncumbentId, out var incumbent)
                    || !strategiesInSession.TryGetValue(decision.CandidateId, out var candidate))
                {
                    continue;
                }

                var incumbentHash = HashSource(incumbent.Source);
                var candidateHash = HashSource(candidate.Source);
                if (string.Equals(incumbentHash, candidateHash, StringComparison.Ordinal))
                {
                    identicalSourceDecisions++;
                    if (string.Equals(decision.Outcome, "Promoted", StringComparison.Ordinal))
                        identicalSourcePromoted++;
                }

                rawPairs.Add(new RawPair(
                    decision.Seat,
                    decision.IncumbentId,
                    decision.CandidateId,
                    incumbent.Source,
                    candidate.Source,
                    incumbentHash,
                    candidateHash,
                    decision.Outcome));
            }
        }

        var distinctPairs = rawPairs
            .GroupBy(pair => (pair.Seat, pair.IncumbentHash, pair.CandidateHash))
            .Select(group => new DistinctPair(
                group.First(),
                group.Count(pair => pair.Outcome == "Promoted"),
                group.Count(pair => pair.Outcome == "Rejected")))
            .ToArray();

        var selectedPairs = distinctPairs
            .Where(pair => pair.Pair.IncumbentHash != pair.Pair.CandidateHash)
            .Where(pair => !pair.Pair.IncumbentId.StartsWith("deterministic-seed-", StringComparison.Ordinal)
                && !pair.Pair.CandidateId.StartsWith("deterministic-seed-", StringComparison.Ordinal))
            .GroupBy(pair => pair.Pair.Seat)
            .SelectMany(group => group
                .OrderBy(pair => pair.Pair.CandidateHash, StringComparer.Ordinal)
                .ThenBy(pair => pair.Pair.IncumbentHash, StringComparer.Ordinal)
                .Take(StudyPairsPerSeat))
            .OrderBy(pair => pair.Pair.Seat)
            .ThenBy(pair => pair.Pair.CandidateHash, StringComparer.Ordinal)
            .ToArray();

        var bank = BuildBank(selectedPairs);

        return new HistorySnapshot
        {
            SessionCount = sessionCount,
            PromotionArtifactCount = promotionArtifactCount,
            StrategyArtifactCount = strategyArtifactCount,
            ResolvedDecisionCount = rawPairs.Count,
            DistinctContentPairCount = distinctPairs.Length,
            IdenticalSourceDecisionCount = identicalSourceDecisions,
            IdenticalSourcePromotedCount = identicalSourcePromoted,
            Strategies = bank,
            StudyPairs = selectedPairs.Select(pair => new StudyPair(
                pair.Pair.Seat,
                Key(pair.Pair.IncumbentId, pair.Pair.IncumbentHash),
                Key(pair.Pair.CandidateId, pair.Pair.CandidateHash),
                pair.Pair.IncumbentId,
                pair.Pair.CandidateId,
                pair.Pair.IncumbentHash,
                pair.Pair.CandidateHash,
                pair.Pair.Outcome,
                pair.PromotedCount,
                pair.RejectedCount)).ToArray(),
            Decisions = decisions,
        };
    }

    private static IReadOnlyList<StrategyRecord> BuildBank(IReadOnlyList<DistinctPair> pairs)
    {
        var bank = new Dictionary<string, StrategyRecord>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            Add(pair.Pair.IncumbentId, pair.Pair.IncumbentHash, pair.Pair.IncumbentSource, pair.Pair.Seat);
            Add(pair.Pair.CandidateId, pair.Pair.CandidateHash, pair.Pair.CandidateSource, pair.Pair.Seat);
        }

        return bank.Values.OrderBy(strategy => strategy.Seat).ThenBy(strategy => strategy.Id, StringComparer.Ordinal).ToArray();

        void Add(string id, string hash, string source, int seat)
        {
            var key = Key(id, hash);
            bank.TryAdd(key, new StrategyRecord(key, seat, source));
        }
    }

    private static string Key(string id, string hash) => $"{id}@{hash[..8]}";

    private static string HashSource(string source)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant()[..16];

    private static SessionStrategy? TryReadStrategy(string file)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;

            var id = GetString(root, "StrategyId") ?? GetString(root, "strategyId");
            var kind = GetString(root, "Kind") ?? GetString(root, "kind");
            var source = GetString(root, "Source") ?? GetString(root, "source");
            var seat = GetInt(root, "PlayerSeat") ?? GetInt(root, "playerSeat") ?? 0;

            if (string.IsNullOrWhiteSpace(id)
                || string.IsNullOrWhiteSpace(source)
                || !string.Equals(kind, "Script", StringComparison.OrdinalIgnoreCase)
                || seat is < 1 or > 4)
            {
                return null;
            }

            return new SessionStrategy(id, seat, source);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static PromotionDecisionRecord? TryReadDecision(string file)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;

            var seat = GetInt(root, "LearningPlayerSeat") ?? GetInt(root, "learningPlayerSeat");
            var incumbent = GetString(root, "IncumbentStrategyId") ?? GetString(root, "incumbentStrategyId");
            var candidate = GetString(root, "CandidateStrategyId") ?? GetString(root, "candidateStrategyId");
            var outcome = GetString(root, "Outcome") ?? GetString(root, "outcome");

            if (seat is null or < 1 or > 4
                || string.IsNullOrWhiteSpace(incumbent)
                || string.IsNullOrWhiteSpace(candidate)
                || string.IsNullOrWhiteSpace(outcome))
            {
                return null;
            }

            return new PromotionDecisionRecord(
                seat.Value,
                incumbent,
                candidate,
                outcome,
                GetInt(root, "EvaluationMatchCount") ?? GetInt(root, "evaluationMatchCount") ?? 0,
                GetInt(root, "CandidateWins") ?? GetInt(root, "candidateWins") ?? 0,
                GetInt(root, "IncumbentWins") ?? GetInt(root, "incumbentWins") ?? 0,
                GetDouble(root, "WinRateImprovement") ?? GetDouble(root, "winRateImprovement") ?? 0,
                GetDouble(root, "ScoreDifferentialDelta") ?? GetDouble(root, "scoreDifferentialDelta") ?? 0,
                GetDouble(root, "CandidateAverageScore") ?? GetDouble(root, "candidateAverageScore") ?? 0,
                GetDouble(root, "IncumbentAverageScore") ?? GetDouble(root, "incumbentAverageScore") ?? 0);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;

    private static double? GetDouble(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.TryGetDouble(out var value)
            ? value
            : null;

    private sealed record SessionStrategy(string Id, int Seat, string Source);

    private sealed record RawPair(
        int Seat,
        string IncumbentId,
        string CandidateId,
        string IncumbentSource,
        string CandidateSource,
        string IncumbentHash,
        string CandidateHash,
        string Outcome);

    private sealed record DistinctPair(RawPair Pair, int PromotedCount, int RejectedCount);
}
