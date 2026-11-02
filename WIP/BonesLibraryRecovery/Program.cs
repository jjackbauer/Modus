using System.Text.Json;

// ── Recovery v2: collect all candidates first, then pick best per seat ──

var dataDir = args.Length > 0 ? args[0] : @"C:\Users\ricar\dev\Modus\WIP\Wip.Bones.Host\bones-data";
var sessionsDir = Path.Combine(dataDir, ".wip", "sessions");
var libraryDir = Path.Combine(dataDir, ".bones", "strategy-library");

if (!Directory.Exists(sessionsDir))
{
    Console.Error.WriteLine($"Sessions directory not found: {sessionsDir}");
    return 1;
}

Directory.CreateDirectory(libraryDir);

// ═══════════════════════════════════════════════════════════════════
// PASS 1: Collect every promoted candidate from every session
// ═══════════════════════════════════════════════════════════════════

var allCandidates = new List<Candidate>();
int scanned = 0, notFound = 0;

foreach (var sessionDir in Directory.GetDirectories(sessionsDir))
{
    var artifactsDir = Path.Combine(sessionDir, "artifacts");
    if (!Directory.Exists(artifactsDir)) continue;

    var promoFiles = Directory.GetFiles(artifactsDir, "*bones-strategy-promotion*.json")
        .Where(f => !f.EndsWith(".descriptor.json", StringComparison.OrdinalIgnoreCase));

    foreach (var promoFile in promoFiles)
    {
        try
        {
            using var promoDoc = JsonDocument.Parse(File.ReadAllText(promoFile));
            var root = promoDoc.RootElement;
            if (root.GetProperty("Outcome").GetString() != "Promoted") continue;

            var candidateId = root.GetProperty("CandidateStrategyId").GetString()!;
            var seat = root.GetProperty("LearningPlayerSeat").GetInt32();
            var evalCount = root.GetProperty("EvaluationMatchCount").GetInt32();
            var candidateWins = root.GetProperty("CandidateWins").GetInt32();
            var scoreDelta = root.GetProperty("ScoreDifferentialDelta").GetDouble();
            var winRate = root.GetProperty("CandidateWinRate").GetDouble();
            var totalScoreDiff = (int)Math.Round(scoreDelta * evalCount);

            // Find strategy source
            string? source = null;
            string? kind = null;

            // Direct match on candidateId in filename
            var matchingFiles = Directory.GetFiles(artifactsDir, "*" + candidateId + "*.json")
                .Where(f => !f.EndsWith(".descriptor.json", StringComparison.OrdinalIgnoreCase)
                    && !f.Contains("promotion", StringComparison.OrdinalIgnoreCase)
                    && !f.Contains("bootstrap", StringComparison.OrdinalIgnoreCase)
                    && !f.Contains("effectiveness", StringComparison.OrdinalIgnoreCase)
                    && !f.Contains("match", StringComparison.OrdinalIgnoreCase)
                    && !f.Contains("observation", StringComparison.OrdinalIgnoreCase));

            foreach (var mf in matchingFiles)
            {
                try
                {
                    var content = File.ReadAllText(mf);
                    if (content.TrimStart().StartsWith("{"))
                    {
                        using var sDoc = JsonDocument.Parse(content);
                        var sr = sDoc.RootElement;
                        if (sr.TryGetProperty("StrategyId", out var sid) && sid.GetString() == candidateId)
                        {
                            source = sr.GetProperty("Source").GetString()!;
                            kind = sr.TryGetProperty("Kind", out var k) ? k.GetString()! : "Markdown";
                            break;
                        }
                    }
                }
                catch { }
            }

            if (source is null)
            {
                notFound++;
                continue;
            }

            kind ??= "Markdown";

            // Extract version number for tiebreaking (e.g. "initial-seat-1-v15" → "v15")
            var version = ExtractVersion(candidateId);

            allCandidates.Add(new Candidate(
                Seat: seat,
                StrategyId: candidateId,
                Kind: kind,
                Source: source,
                MatchesPlayed: evalCount,
                Wins: candidateWins,
                Losses: evalCount - candidateWins,
                CumulativeScoreDifferential: totalScoreDiff,
                WinRate: winRate,
                ScoreDifferentialDelta: scoreDelta,
                StrategyVersion: version));

            scanned++;
        }
        catch { }
    }
}

Console.WriteLine($"Pass 1 done: {scanned} promoted candidates collected, {notFound} source-not-found");

// ═══════════════════════════════════════════════════════════════════
// PASS 2: Per seat, pick the best candidate
// ═══════════════════════════════════════════════════════════════════

int upserted = 0;

for (int seat = 1; seat <= 4; seat++)
{
    var seatCandidates = allCandidates
        .Where(c => c.Seat == seat)
        .ToList();

    Console.WriteLine($"\n  Seat {seat}: {seatCandidates.Count} candidates");

    if (seatCandidates.Count == 0) continue;

    // Deduplicate by StrategyId: for the same strategy appearing in multiple
    // sessions (different evaluation seeds), keep the run with best metrics.
    var deduped = seatCandidates
        .GroupBy(c => c.StrategyId)
        .Select(g => g.OrderByDescending(c => c.WinRate)
                      .ThenByDescending(c => c.MatchesPlayed)
                      .First())
        .ToList();

    Console.WriteLine($"    After dedup: {deduped.Count} unique strategies");

    // Show top candidates with Bayesian scores
    const double pw = 5, pm = 10;
    foreach (var c in deduped.OrderByDescending(c => BayesianScore(c, pw, pm)).Take(8))
    {
        var bayes = BayesianScore(c, pw, pm);
        Console.WriteLine($"      {c.StrategyId} ({c.Kind}) W:{c.Wins}/{c.MatchesPlayed} raw:{c.WinRate:P1} bayes:{bayes:P1} Diff:{c.CumulativeScoreDifferential} v={c.StrategyVersion}");
    }

    // Pick best using Bayesian-weighted score: (Wins + priorWins) / (Matches + priorMatches)
    // This naturally penalizes 1-match flukes while rewarding sustained performance.
    // priorWins=5, priorMatches=10 gives a 50% prior with weight equivalent to 10 matches.
    const double priorWins = 5, priorMatches = 10;

    var best = deduped
        .OrderByDescending(c => BayesianScore(c, priorWins, priorMatches))
        .ThenByDescending(c => c.WinRate)           // if tied, prefer higher raw WR
        .ThenByDescending(c => c.MatchesPlayed)      // if still tied, more data
        .ThenByDescending(c => c.StrategyVersion, new VersionComparer())
        .First();

    var bayesianWR = BayesianWR(best, priorWins, priorMatches);
    Console.WriteLine($"    → Best: {best.StrategyId} ({best.Kind}) W:{best.Wins}/{best.MatchesPlayed} ({best.WinRate:P1}) Bayesian:({bayesianWR:P1})");

    // Write to library
    var seatPath = Path.Combine(libraryDir, $"seat-{seat}.json");
    var entry = new
    {
        strategyId = best.StrategyId,
        playerSeat = seat,
        kind = best.Kind,
        source = best.Source,
        matchesPlayed = best.MatchesPlayed,
        wins = best.Wins,
        losses = best.Losses,
        cumulativeScoreDifferential = best.CumulativeScoreDifferential,
        lastUpdatedUtc = DateTimeOffset.UtcNow,
    };

    var writeOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    var json = JsonSerializer.Serialize(entry, writeOpts);
    var tmpPath = seatPath + ".tmp" + Guid.NewGuid().ToString("N");
    File.WriteAllText(tmpPath, json);
    File.Move(tmpPath, seatPath, overwrite: true);
    upserted++;
}

// ═══════════════════════════════════════════════════════════════════
// Final report
// ═══════════════════════════════════════════════════════════════════

Console.WriteLine($"\nRecovery v2 complete: {upserted} seats upserted from {scanned} candidates.");

Console.WriteLine("\n--- Final Library State ---");
for (int seat = 1; seat <= 4; seat++)
{
    var path = Path.Combine(libraryDir, $"seat-{seat}.json");
    if (File.Exists(path))
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var r = doc.RootElement;
        var id = r.GetProperty("strategyId").GetString();
        var k = r.GetProperty("kind").GetString();
        var w = r.GetProperty("wins").GetInt32();
        var mp = r.GetProperty("matchesPlayed").GetInt32();
        var d = r.GetProperty("cumulativeScoreDifferential").GetInt32();
        var wr = mp > 0 ? (double)w / mp : 0.0;
        Console.WriteLine($"  Seat {seat}: {id} ({k}) W:{w}/{mp} ({wr:P1}) Diff:{d}");
    }
    else
    {
        Console.WriteLine($"  Seat {seat}: (missing)");
    }
}

// ── Local functions ──

static double BayesianScore(Candidate c, double priorWins, double priorMatches)
    => (c.Wins + priorWins) / (c.MatchesPlayed + priorMatches) * Math.Sqrt(c.MatchesPlayed);

static double BayesianWR(Candidate c, double priorWins, double priorMatches)
    => BayesianScore(c, priorWins, priorMatches);

static string ExtractVersion(string strategyId)
{
    var idx = strategyId.LastIndexOf("-v", StringComparison.Ordinal);
    return idx >= 0 ? strategyId[(idx + 2)..] : "0";
}

return 0;

// ── Types ──

record Candidate(
    int Seat,
    string StrategyId,
    string Kind,
    string Source,
    int MatchesPlayed,
    int Wins,
    int Losses,
    int CumulativeScoreDifferential,
    double WinRate,
    double ScoreDifferentialDelta,
    string StrategyVersion);

sealed class VersionComparer : IComparer<string>
{
    public int Compare(string? a, string? b)
    {
        _ = int.TryParse(a, out var va);
        _ = int.TryParse(b, out var vb);
        return va.CompareTo(vb);
    }
}
