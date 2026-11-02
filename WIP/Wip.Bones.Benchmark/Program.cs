using System.Globalization;
using System.Text.Json;
using Wip.Bones.Benchmark;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

var mode = args.Length > 0 ? args[0] : "help";
var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var index = 1; index < args.Length; index++)
{
    if (args[index].StartsWith("--", StringComparison.Ordinal) && index + 1 < args.Length)
    {
        options[args[index][2..]] = args[index + 1];
        index++;
    }
}

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
};

switch (mode)
{
    case "history":
        RunHistory();
        break;
    case "arena":
        RunArena();
        break;
    case "champion":
        RunChampion();
        break;
    case "gate":
        RunGate();
        break;
    case "smoke":
        RunSmoke();
        break;
    case "match":
        RunMatchProbe();
        break;
    default:
        Console.WriteLine("""
            Bones benchmark.

              history --data <data-dir> --out <snapshot.json>
              arena   --snapshot <snapshot.json> --out <arena.json> --games <n> --master-seed <n>
                      [--target 12] [--label dev|heldout] [--max-degree 8]
              champion --snapshot <snapshot.json> --out-dir <dir> [--seat 1] [--games 2000]
                      [--seeds 910001,910002] [--target 12] [--incumbent <id>] [--max-degree 8]
              gate    --snapshot <snapshot.json> --arena <arena.json> --out <gate.json>
                      [--opponents-arena <arena.json>] [--reps 100] [--matches 8]
                      [--seed 700001] [--target 12] [--max-degree 8]
              smoke   --snapshot <snapshot.json> [--games 20] [--max-degree 4]
            """);
        break;
}

void RunHistory()
{
    var data = Require("--data");
    var output = Require("--out");
    var snapshot = HistoryScanner.Scan(data);
    File.WriteAllText(output, JsonSerializer.Serialize(snapshot, jsonOptions));

    Console.WriteLine($"sessions={snapshot.SessionCount} strategyArtifacts={snapshot.StrategyArtifactCount} promotionArtifacts={snapshot.PromotionArtifactCount}");
    Console.WriteLine($"resolvedDecisions={snapshot.ResolvedDecisionCount} distinctContentPairs={snapshot.DistinctContentPairCount}");
    Console.WriteLine($"identicalSourceDecisions={snapshot.IdenticalSourceDecisionCount} identicalSourcePromoted={snapshot.IdenticalSourcePromotedCount}");
    Console.WriteLine($"studyPairs={snapshot.StudyPairs.Count} studyStrategies={snapshot.Strategies.Count}");
    Console.WriteLine($"-> {output}");
}

void RunArena()
{
    var snapshotPath = Require("--snapshot");
    var output = Require("--out");
    var games = GetInt("--games", 200);
    var masterSeed = GetInt("--master-seed", 900001);
    var target = GetInt("--target", 12);
    var label = Get("--label") ?? "benchmark";
    var maxDegree = GetInt("--max-degree", Environment.ProcessorCount);

    var snapshot = JsonSerializer.Deserialize<HistorySnapshot>(File.ReadAllText(snapshotPath), jsonOptions)
        ?? throw new InvalidOperationException("Unable to read history snapshot.");

    // Historical deterministic-seed fallback scripts embed System.HashCode.Combine, whose
    // output is randomized per process. They are synthetic bootstrap artifacts, not model
    // strategies; exclude them so the benchmark itself is reproducible.
    var benchmarkStrategies = snapshot.Strategies
        .Where(strategy => !strategy.Id.StartsWith("deterministic-seed-", StringComparison.Ordinal))
        .ToArray();

    var compiled = Arena.Compile(benchmarkStrategies, out var failures, out var compileMilliseconds);
    if (failures.Count > 0)
    {
        Console.WriteLine($"compile failures: {failures.Count}");
        foreach (var failure in failures.Take(10))
            Console.WriteLine("  " + failure);
    }

    var field = new List<FieldEntry>();
    field.AddRange(DeterministicOpponents.CreateArchetypes());
    if (!string.Equals(Get("--field"), "archetypes", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var strategy in compiled)
            field.Add(new ScriptFieldEntry(strategy.Record.Id, strategy.SlotType));
    }

    Console.WriteLine($"arena '{label}': strategies={compiled.Count} field={field.Count} gamesPerStrategy={games} seed={masterSeed}");
    var result = new Arena(field, compiled, target, masterSeed, label).Run(games, maxDegree);
    File.WriteAllText(output, JsonSerializer.Serialize(result, jsonOptions));

    Console.WriteLine($"games={result.GamesPlayed} elapsedMs={result.ElapsedMilliseconds:F0} msPerGame={result.MillisecondsPerGame:F3} compileMs={compileMilliseconds:F0}");
    foreach (var metrics in result.Strategies.Take(8))
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {metrics.StrategyId,-28} seat={metrics.Seat} win={metrics.WinRate:P1} [{metrics.WinRateCiLow:P1},{metrics.WinRateCiHigh:P1}] scoreDiff={metrics.MeanScoreDifferential,7:F2} [{metrics.ScoreDifferentialCiLow,7:F2},{metrics.ScoreDifferentialCiHigh,7:F2}]"));
    }

    Console.WriteLine($"-> {output}");
}

void RunChampion()
{
    var snapshot = Require("--snapshot");
    var outputDirectory = Get("--out-dir") ?? ".tmp/champion-goal/champion-run";
    var seat = GetInt("--seat", 1);
    var games = GetInt("--games", 2000);
    var seedsValue = Get("--seeds");
    var target = GetInt("--target", 12);
    var incumbent = Get("--incumbent");
    var maxDegree = GetInt("--max-degree", 8);
    var minLcbDelta = GetDouble("--min-lcb-delta", 0.0);

    IReadOnlyList<int>? seeds = null;
    if (!string.IsNullOrWhiteSpace(seedsValue))
    {
        seeds = seedsValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();
    }

    var result = ChampionRunner.Run(new ChampionRunOptions(
        snapshot,
        outputDirectory,
        seat,
        games,
        seeds,
        target,
        incumbent,
        maxDegree,
        minLcbDelta));

    Console.WriteLine($"-> {Path.Combine(outputDirectory, "champion-result.json")}");
    Console.WriteLine($"selected={result.SelectedStrategyId} rate={result.WinRate:P3} lcb={result.Lcb:P3} written={result.LibraryWritten}");
}

void RunGate()
{
    var snapshotPath = Require("--snapshot");
    var arenaPath = Require("--arena");
    var opponentsArenaPath = Get("--opponents-arena");
    var output = Require("--out");
    var reps = GetInt("--reps", 100);
    var matches = GetInt("--matches", 8);
    var seed = GetInt("--seed", 700001);
    var target = GetInt("--target", 12);
    var maxDegree = GetInt("--max-degree", Environment.ProcessorCount);

    var snapshot = JsonSerializer.Deserialize<HistorySnapshot>(File.ReadAllText(snapshotPath), jsonOptions)
        ?? throw new InvalidOperationException("Unable to read history snapshot.");
    var arena = JsonSerializer.Deserialize<ArenaResult>(File.ReadAllText(arenaPath), jsonOptions)
        ?? throw new InvalidOperationException("Unable to read arena result.");

    var opponentsArena = arena;
    if (!string.IsNullOrWhiteSpace(opponentsArenaPath))
    {
        opponentsArena = JsonSerializer.Deserialize<ArenaResult>(File.ReadAllText(opponentsArenaPath), jsonOptions)
            ?? throw new InvalidOperationException("Unable to read opponents arena result.");
    }

    var compiled = Arena.Compile(snapshot.Strategies, out var failures, out _);
    if (failures.Count > 0)
        Console.WriteLine($"compile failures: {failures.Count}");

    var types = compiled.ToDictionary(strategy => strategy.Record.Id, strategy => strategy.SlotType, StringComparer.Ordinal);
    var pairs = snapshot.StudyPairs
        .Where(pair => types.ContainsKey(pair.IncumbentKey) && types.ContainsKey(pair.CandidateKey))
        .ToArray();

    // Fixed opponent field for evaluation: the strongest measured strategy per seat in the
    // opponents arena (dev when --opponents-arena is supplied). The scored truth strengths always
    // come from --arena so a held-out truth arena cannot leak into the opponent selection.
    var opponents = opponentsArena.Strategies
        .GroupBy(metrics => metrics.Seat)
        .ToDictionary(
            group => group.Key,
            group => group.OrderByDescending(metrics => metrics.MeanScoreDifferential).First().StrategyId);

    var strengths = arena.Strategies.ToDictionary(
        metrics => metrics.StrategyId,
        metrics => metrics.MeanScoreDifferential,
        StringComparer.Ordinal);

    var arms = new List<GateArm>
    {
        new("and_independent_n8", Paired: false, MatchCount: matches, GateRule.AndLegacy),
        new("or_independent_n8", Paired: false, MatchCount: matches, GateRule.OrCurrent),
        new("or_paired_n8", Paired: true, MatchCount: matches, GateRule.OrCurrent),
        new("confidence_paired_n8", Paired: true, MatchCount: matches, GateRule.ConfidenceComposite),
        new("confidence_paired_n16", Paired: true, MatchCount: 16, GateRule.ConfidenceComposite),
        new("confidence_paired_n32", Paired: true, MatchCount: 32, GateRule.ConfidenceComposite),
    };

    var maxMatches = arms.Max(arm => arm.MatchCount);
    Console.WriteLine($"gate arenas: truth='{arena.Label}' ({arenaPath}); opponents='{opponentsArena.Label}' ({opponentsArenaPath ?? arenaPath})");
    Console.WriteLine($"gate study: pairs={pairs.Length} reps={reps} maxMatches={maxMatches} seed={seed} opponents={string.Join(",", opponents.OrderBy(kv => kv.Key).Select(kv => $"s{kv.Key}:{kv.Value}"))}");
    var result = GateStudy.Run(pairs, types, opponents, strengths, arms, reps, seed, target, maxDegree);
    File.WriteAllText(output, JsonSerializer.Serialize(result, jsonOptions));

    Console.WriteLine(GateStudy.ToMarkdown(result));
    Console.WriteLine($"elapsedMs={result.ElapsedMilliseconds:F0}");
    Console.WriteLine($"-> {output}");
}

void RunMatchProbe()
{
    var snapshotPath = Require("--snapshot");
    var requestedStrategy = Get("--strategy");
    var seed = GetInt("--seed", 12345);
    var snapshot = JsonSerializer.Deserialize<HistorySnapshot>(File.ReadAllText(snapshotPath), jsonOptions)
        ?? throw new InvalidOperationException("Unable to read history snapshot.");
    var strategy = requestedStrategy is null
        ? snapshot.Strategies[0]
        : snapshot.Strategies.First(s => s.Id == requestedStrategy);
    var strategyId = strategy.Id;
    var compiled = Arena.Compile([strategy], out var failures, out _);
    var type = compiled[0].SlotType;
    var slots = new Dictionary<BonesPlayerId, IBonesPlayerSlot>
    {
        [new BonesPlayerId(strategy.Seat)] = (IBonesPlayerSlot)Activator.CreateInstance(type)!,
        [new BonesPlayerId(1)] = new DeterministicOpponents.FirstLegalMoveSlot(),
        [new BonesPlayerId(2)] = new DeterministicOpponents.HighestPipSlot(),
        [new BonesPlayerId(3)] = new DeterministicOpponents.LowestPipSlot(),
        [new BonesPlayerId(4)] = new DeterministicOpponents.FirstLegalMoveSlot(),
    };
    slots[new BonesPlayerId(strategy.Seat)] = (IBonesPlayerSlot)Activator.CreateInstance(type)!;
    var config = new BonesMatchConfig(new BonesGameId("probe"), seed, 12, slots);
    var result = new BonesMatchSimulator().RunMatch(config);
    var transcript = string.Join(";", result.Transcript.Select(e => $"{e.TurnIndex}:{e.PlayerId.Seat}:{e.Kind}:{e.Tile?.LowPip}-{e.Tile?.HighPip}:{e.Side}"));
    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(transcript)));
    Console.WriteLine($"strategy={strategyId} seat={strategy.Seat} winner={result.Winner.Seat} scores={string.Join(",", result.CumulativeScores.OrderBy(kv => kv.Key.Seat).Select(kv => kv.Value))} rounds={result.Rounds.Length} events={result.Transcript.Length} hash={hash[..16]}");
    Console.WriteLine("derived=" + string.Join(",", Enumerable.Range(0, 5).Select(i => BonesSeedDerivation.Derive(900001, i))));
}

void RunSmoke()
{
    var snapshotPath = Require("--snapshot");
    var games = GetInt("--games", 20);
    var maxDegree = GetInt("--max-degree", 4);
    var snapshot = JsonSerializer.Deserialize<HistorySnapshot>(File.ReadAllText(snapshotPath), jsonOptions)
        ?? throw new InvalidOperationException("Unable to read history snapshot.");
    var subset = snapshot.Strategies.Take(6).ToArray();
    var compiled = Arena.Compile(subset, out var failures, out var compileMs);
    var field = new List<FieldEntry>();
    field.AddRange(DeterministicOpponents.CreateArchetypes());
    foreach (var strategy in compiled)
        field.Add(new ScriptFieldEntry(strategy.Record.Id, strategy.SlotType));

    var started = System.Diagnostics.Stopwatch.StartNew();
    var result = new Arena(field, compiled, 12, 900001, "smoke").Run(games, maxDegree);
    started.Stop();
    Console.WriteLine($"strategies={compiled.Count} failures={failures.Count} compileMs={compileMs:F0} games={result.GamesPlayed} elapsedMs={started.Elapsed.TotalMilliseconds:F0} msPerGame={result.MillisecondsPerGame:F3}");
    foreach (var metrics in result.Strategies)
        Console.WriteLine($"  {metrics.StrategyId,-28} win={metrics.WinRate:P1} scoreDiff={metrics.MeanScoreDifferential:F2}");
}

string Require(string name)
    => Get(name) ?? throw new ArgumentException($"Missing required option {name}.");

string? Get(string name)
    => options.TryGetValue(name.StartsWith("--", StringComparison.Ordinal) ? name[2..] : name, out var value)
        ? value
        : null;

int GetInt(string name, int fallback)
    => int.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

double GetDouble(string name, double fallback)
    => double.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
