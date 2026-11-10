using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Agents.Learning.Skills;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// The statistically gated learning loop. It trains and evaluates champions and challengers from
/// seeds only, verifies every policy verdict against the evaluation report, and persists strategies,
/// evaluation reports, the champion-challenger ledger and the champion pointer. It never merges,
/// publishes, deploys or runs model-authored content.
/// </summary>
/// <remarks>
/// The evaluator, the promotion policy and the verifier are never replaced by this type's callers:
/// the optional policy parameter exists for the negative control only. Reporting hooks observe phase
/// transitions and cannot change a verdict.
/// </remarks>
public static class TrainingLoop
{
    /// <summary>The hand-number stream the champion's training match lives in.</summary>
    public const int TrainingSeedHandBase = 1_000;

    /// <summary>The hand-number stream the per-iteration evaluation base seed lives in.</summary>
    public const int EvaluationSeedHandBase = 5_000;

    public const int TrainingSeedStream = 1;

    public const int EvaluationSeedStream = 3;

    public const string VerificationFailedOutcome = "verification-failed";

    public const string PromotedOutcome = "promoted";

    public const string RejectedOutcome = "rejected";

    public const string InertProposalOutcome = "inert-proposal";

    public const string ProposalAcceptedReason = "accepted";

    public const string LedgerFileName = "ledger.json";

    public const string ChampionFileName = "active-champion.json";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private sealed record LedgerEntry(
        (int Iteration, string ChampionId, string ChallengerId) Key,
        string CoreJson);

    public static TrainingRun Run(
        TrainingOptions options,
        IPromotionPolicy? policy = null,
        CancellationToken cancellationToken = default,
        IStrategyProposer? proposer = null,
        ITrainingObserver? observer = null,
        IProposalGuard? proposalGuard = null,
        IRefinementPolicy? refinementPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Iterations < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Iterations, "Iterations must not be negative.");
        }

        if (options.TrainingHands < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.TrainingHands, "A training match needs at least one hand.");
        }

        if (options.EvaluationSeeds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.EvaluationSeeds, "An evaluation needs at least one seed.");
        }

        if (options.EvaluationHandsPerMatch < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.EvaluationHandsPerMatch,
                "An evaluation match needs at least one hand.");
        }

        var criteria = options.Criteria ?? new EvaluationCriteria();
        var promotionPolicy = policy ?? new StatisticalPromotionPolicy();
        var strategyProposer = proposer ?? new DeterministicStrategyProposer();
        var dataDirectory = options.DataDirectory ?? DefaultDataDirectory();

        // R-06: an opt-in bootstrap loads and verifies the source state and prepares the fresh target
        // before the loop reads the target's champion pointer, so the run starts from the source
        // champion and lineage. A plain run (None) skips this block entirely.
        HarnessBootstrapState? bootstrap = null;
        if (options.BootstrapMode != HarnessBootstrapMode.None)
        {
            if (string.IsNullOrWhiteSpace(options.BootstrapFrom))
            {
                throw new ArgumentException(
                    "A bootstrap mode requires the source data directory in BootstrapFrom.",
                    nameof(options));
            }

            bootstrap = HarnessBootstrap.Load(options.BootstrapFrom, options.BootstrapMode);
            HarnessBootstrap.PrepareTarget(dataDirectory, bootstrap);
        }

        var strategyDirectory = Path.Combine(dataDirectory, "strategies");
        var evaluationDirectory = Path.Combine(dataDirectory, "evaluations");
        Directory.CreateDirectory(strategyDirectory);
        Directory.CreateDirectory(evaluationDirectory);

        // Round-4 R-03: the memory store exists only in refinement mode and is written after a
        // completed evaluation iteration. It is never passed into the refinement policy, so it cannot
        // change a proposal, a verdict or a persisted strategy/evaluation/ledger byte.
        var memoryStore = options.HarnessRefinement ? new HarnessMemoryStore(dataDirectory) : null;

        // R-06: a frozen bootstrap reads memory but never writes it. Updating (and a plain opt-in
        // run without bootstrap) writes as before; legacy mode has no store, so it never writes.
        var memoryWritable = memoryStore is not null
            && (bootstrap is null || bootstrap.Mode == HarnessBootstrapMode.Updating);

        // R-10: skill refinement is a separate opt-in. The guard is installed only for that path, so
        // the measured HarnessRefinement arm with the default flag never passes through it.
        IProposalGuard? effectiveProposalGuard = proposalGuard;
        if (options.HarnessRefinement && options.SkillRefinement)
        {
            effectiveProposalGuard = new RefinementGuard(proposalGuard);
        }

        VerifyExistingLedger(dataDirectory);

        var champion = LoadChampion(dataDirectory, strategyDirectory, options.StartingStrategyId);
        WriteUtf8(strategyDirectory, champion.Id + ".json", champion.ToCanonicalJson());

        var iterations = new List<TrainingIteration>(options.Iterations);

        // The ledger accumulates across runs instead of being overwritten by the current run.
        var ledger = LoadLedger(dataDirectory);
        string ledgerJson = BuildLedgerArray(ledger);

        for (var iteration = 1; iteration <= options.Iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            observer?.OnProgress(new TrainingProgress(iteration, options.Iterations, TrainingPhase.SelfPlay, null));
            var championAgent = new StrategyAgent(champion.Genome, "champion");
            var trainingSeed = SeedDerivation.Derive(
                options.Seed,
                TrainingSeedHandBase + iteration,
                0,
                0,
                TrainingSeedStream);
            var trainingOptions = new SimulationOptions(trainingSeed, options.TrainingHands);
            var baselineAgent = new TightAggressiveAgent(name: "baseline");
            SimulationReport trainingReport;
            IReadOnlyList<HandRecord> capturedHands = Array.Empty<HandRecord>();
            if (options.HarnessRefinement)
            {
                // Opt-in only: the simulator writes the training hands through its existing history
                // directory, the capture parses them back, and the scratch copy is always removed.
                var capture = TrainingTrajectoryCapture.Capture(
                    dataDirectory,
                    iteration,
                    trainingOptions,
                    championAgent,
                    baselineAgent,
                    cancellationToken);
                trainingReport = capture.Report;
                capturedHands = capture.Hands;
            }
            else
            {
                // Legacy path, byte-for-byte identical to the pre-refinement loop: no history
                // directory is passed, so the simulator creates no scratch artifacts.
                trainingReport = MatchSimulator.Run(
                    championAgent,
                    baselineAgent,
                    trainingOptions,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            observer?.OnProgress(new TrainingProgress(iteration, options.Iterations, TrainingPhase.Analyze, null));
            var analysis = StrategyAnalyzer.Analyze(trainingReport);

            observer?.OnProgress(new TrainingProgress(iteration, options.Iterations, TrainingPhase.Propose, null));
            StrategyGenome proposal;
            string? rationaleCode = null;
            if (options.HarnessRefinement)
            {
                var signatures = FailureSignatureExtractor.Extract(capturedHands);

                // WP-1: the injected policy, when present, is the policy. The deterministic policy
                // is constructed inside this branch only when no policy was supplied, so a null
                // argument leaves the pre-existing path byte-identical.
                var effectiveRefinementPolicy = refinementPolicy
                    ?? new SignatureDrivenRefinementPolicy(
                        strategyProposer,
                        enableSkillEdits: options.SkillRefinement,
                        strategy: options.RefinementStrategy,
                        minimumSignalRate: options.SignalRateFloor);
                var refinement = effectiveRefinementPolicy.Propose(
                    new RefinementContext(champion.Genome, analysis, signatures, iteration));
                proposal = refinement.Genome;
                rationaleCode = refinement.RationaleCode;
            }
            else
            {
                proposal = strategyProposer.Propose(champion.Genome, analysis, iteration);
            }

            var challenger = StrategyArtifact.Create(proposal, iteration, champion);
            cancellationToken.ThrowIfCancellationRequested();

            if (effectiveProposalGuard is not null
                && !effectiveProposalGuard.IsAcceptable(champion.Genome, proposal, out var refusalReason))
            {
                // A7: an inert proposal never reaches the evaluator. The challenger artifact and the
                // ledger row still record that it was considered, and no promotion can follow.
                WriteUtf8(strategyDirectory, challenger.Id + ".json", challenger.ToCanonicalJson());
                var inertEntry = BuildInertLedgerEntry(iteration, champion.Id, challenger.Id, refusalReason);
                UpsertLedger(ledger, iteration, champion.Id, challenger.Id, inertEntry);
                ledgerJson = BuildLedgerArray(ledger);
                WriteUtf8(dataDirectory, LedgerFileName, ledgerJson);

                var inertIteration = new TrainingIteration(
                    iteration,
                    champion.Id,
                    challenger.Id,
                    Report: null,
                    new PromotionDecision(false, refusalReason, 0.0, criteria.MarginBbPer100, 0),
                    Verified: false,
                    InertProposalOutcome);
                iterations.Add(inertIteration);
                observer?.OnProgress(
                    new TrainingProgress(iteration, options.Iterations, TrainingPhase.Persist, inertIteration));
                continue;
            }

            observer?.OnProgress(new TrainingProgress(iteration, options.Iterations, TrainingPhase.Evaluate, null));
            var evaluationBaseSeed = SeedDerivation.Derive(
                options.Seed,
                EvaluationSeedHandBase + iteration,
                0,
                0,
                EvaluationSeedStream);
            var report = MatchEvaluator.Evaluate(
                new StrategyAgent(challenger.Genome, "challenger"),
                challenger.Id,
                new StrategyAgent(champion.Genome, "champion"),
                champion.Id,
                evaluationBaseSeed,
                options.EvaluationSeeds,
                options.EvaluationHandsPerMatch);
            cancellationToken.ThrowIfCancellationRequested();

            observer?.OnProgress(new TrainingProgress(iteration, options.Iterations, TrainingPhase.Gate, null));
            var decision = promotionPolicy.Decide(report, criteria);
            cancellationToken.ThrowIfCancellationRequested();

            observer?.OnProgress(new TrainingProgress(iteration, options.Iterations, TrainingPhase.Verify, null));
            var verified = PromotionVerifier.Verify(decision, report, criteria, out var verificationReason);
            if (!verified)
            {
                decision = decision with { Promoted = false, Reason = verificationReason };
            }

            cancellationToken.ThrowIfCancellationRequested();

            var promoted = verified && decision.Promoted;
            var outcome = !verified
                ? VerificationFailedOutcome
                : promoted ? PromotedOutcome : RejectedOutcome;

            observer?.OnProgress(new TrainingProgress(iteration, options.Iterations, TrainingPhase.Persist, null));
            WriteUtf8(strategyDirectory, challenger.Id + ".json", challenger.ToCanonicalJson());
            WriteUtf8(
                evaluationDirectory,
                challenger.Id + "-vs-" + champion.Id + ".json",
                report.ToCanonicalJson());

            var ledgerEntry = BuildLedgerEntry(iteration, champion.Id, challenger.Id, report, decision, verified, outcome);
            UpsertLedger(ledger, iteration, champion.Id, challenger.Id, ledgerEntry);
            ledgerJson = BuildLedgerArray(ledger);
            WriteUtf8(dataDirectory, LedgerFileName, ledgerJson);

            var completedIteration = new TrainingIteration(
                iteration,
                champion.Id,
                challenger.Id,
                report,
                decision,
                verified,
                outcome);
            iterations.Add(completedIteration);

            if (promoted)
            {
                champion = challenger;
                WriteUtf8(
                    dataDirectory,
                    ChampionFileName,
                    BuildChampionPointer(challenger.Id, iteration, report.ReportHash));
            }

            // R-03 write-only memory: a completed evaluation iteration is remembered as a failed
            // approach (not promoted, including a verifier refusal) or a pattern (promoted). An inert
            // proposal never reaches here, so the guard-refused path writes no memory entry.
            if (memoryStore is not null && memoryWritable && rationaleCode is not null)
            {
                if (promoted)
                {
                    memoryStore.RecordPattern(rationaleCode, iteration);
                }
                else
                {
                    memoryStore.RecordRejection(rationaleCode, iteration);
                }
            }

            observer?.OnProgress(
                new TrainingProgress(iteration, options.Iterations, TrainingPhase.Persist, completedIteration));
        }

        HarnessBootstrapSummary? bootstrapSummary = null;
        if (bootstrap is not null)
        {
            var finalMemory = new HarnessMemoryStore(dataDirectory).Load();
            bootstrapSummary = new HarnessBootstrapSummary(
                bootstrap.Mode,
                Path.GetFullPath(options.BootstrapFrom!),
                bootstrap.Champion.Id,
                bootstrap.Lineage.Count,
                bootstrap.MemoryHash,
                finalMemory.ContentHash,
                memoryWritable);
        }

        return new TrainingRun(
            options,
            iterations,
            champion.Id,
            ReadLedgerEntries(ledgerJson),
            bootstrapSummary);
    }

    private static string DefaultDataDirectory() =>
        Path.Combine(Environment.CurrentDirectory, "artifacts", "tholdem");

    /// <summary>Reads a persisted strategy artifact and refuses one whose id no longer matches its content.</summary>
    public static StrategyArtifact ReadPersistedStrategy(string dataDirectory, string strategyId)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        ArgumentException.ThrowIfNullOrEmpty(strategyId);

        var path = Path.Combine(dataDirectory, "strategies", strategyId + ".json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The strategy artifact '{strategyId}' was not found at '{path}'.");
        }

        var artifact = ReadArtifact(path, strategyId);
        if (!artifact.Id.EndsWith("-" + artifact.Hash[..8], StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The strategy artifact '{strategyId}' does not match its content hash.");
        }

        return artifact;
    }

    private static void VerifyExistingLedger(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, LedgerFileName);
        if (!File.Exists(path))
        {
            return;
        }

        var integrity = LedgerChain.Verify(File.ReadAllText(path));
        if (!integrity.Verified)
        {
            throw new InvalidOperationException(
                $"The training ledger '{path}' failed its hash-chain integrity check: {integrity.Reason}.");
        }
    }

    private static StrategyArtifact LoadChampion(
        string dataDirectory,
        string strategyDirectory,
        string? startingStrategyId)
    {
        if (!string.IsNullOrWhiteSpace(startingStrategyId))
        {
            var requestedPath = Path.Combine(strategyDirectory, startingStrategyId + ".json");
            if (!File.Exists(requestedPath))
            {
                throw new InvalidOperationException(
                    $"The requested starting strategy '{startingStrategyId}' has no artifact at '{requestedPath}'.");
            }

            var requested = ReadArtifact(requestedPath, startingStrategyId);
            var suffix = requested.Hash[..8];
            if (!requested.Id.EndsWith("-" + suffix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The requested starting strategy '{startingStrategyId}' does not match its content hash.");
            }

            return requested;
        }

        var pointerPath = Path.Combine(dataDirectory, ChampionFileName);
        if (!File.Exists(pointerPath))
        {
            // A fresh data directory has no active champion yet; persist the version-1 champion
            // the loop actually starts from so the pointer never silently falls back again.
            var fresh = StrategyArtifact.Create(new StrategyGenome(), 0, null);
            WriteUtf8(dataDirectory, ChampionFileName, BuildChampionPointer(fresh.Id, fresh.CreatedAtIteration));
            return fresh;
        }

        string strategyId;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(pointerPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("strategyId", out var strategyIdElement)
                || strategyIdElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(strategyIdElement.GetString()))
            {
                throw new InvalidOperationException(
                    $"The champion pointer '{pointerPath}' does not contain a non-empty strategyId.");
            }

            strategyId = strategyIdElement.GetString()!;
        }
        catch (Exception exception) when (IsTolerableReadFailure(exception))
        {
            throw new InvalidOperationException(
                $"The champion pointer '{pointerPath}' is not readable: {exception.Message}",
                exception);
        }

        var artifactPath = Path.Combine(strategyDirectory, strategyId + ".json");
        if (!File.Exists(artifactPath))
        {
            throw new InvalidOperationException(
                $"The champion pointer '{pointerPath}' references strategy '{strategyId}', "
                + $"but its artifact '{artifactPath}' is missing.");
        }

        try
        {
            return ReadArtifact(artifactPath, strategyId);
        }
        catch (Exception exception) when (IsTolerableReadFailure(exception))
        {
            throw new InvalidOperationException(
                $"The champion pointer '{pointerPath}' references strategy '{strategyId}', "
                + $"but its artifact '{artifactPath}' could not be read: {exception.Message}",
                exception);
        }
    }

    private static StrategyArtifact ReadArtifact(string path, string expectedId)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The strategy artifact must be a JSON object.");
        }

        var id = root.GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new FormatException("The strategy artifact has no id.");
        }

        if (!string.Equals(id, expectedId, StringComparison.Ordinal))
        {
            throw new FormatException(
                $"The strategy artifact names '{id}' but the pointer requires '{expectedId}'.");
        }

        var version = root.GetProperty("version").GetInt32();
        if (version < 1)
        {
            throw new FormatException("The strategy artifact version must be positive.");
        }

        var parentId = root.TryGetProperty("parentId", out var parent) && parent.ValueKind == JsonValueKind.String
            ? parent.GetString()
            : null;
        var createdAtIteration = root.GetProperty("createdAtIteration").GetInt32();
        var parameters = root.GetProperty("parameters");
        StrategySkillSet? skills = null;
        if (root.TryGetProperty("skills", out var skillsElement))
        {
            skills = StrategySkillSetJson.Read(skillsElement);
        }

        var genome = new StrategyGenome(
            parameters.GetProperty("raiseThreshold").GetDouble(),
            parameters.GetProperty("callThreshold").GetDouble(),
            parameters.GetProperty("bluffFrequency").GetDouble(),
            parameters.GetProperty("sizingMultiplier").GetDouble(),
            root.GetProperty("promptText").GetString() ?? new StrategyGenome().PromptText,
            skills);

        return new StrategyArtifact(id, version, parentId, createdAtIteration, genome.Clamp());
    }

    private static bool IsTolerableReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or OverflowException or ArgumentException;

    private static string BuildLedgerEntry(
        int iteration,
        string championId,
        string challengerId,
        EvaluationReport report,
        PromotionDecision decision,
        bool verified,
        string outcome)
    {
        var builder = new StringBuilder(384);
        builder.Append("{\"iteration\":");
        CanonicalJsonWriter.Number(builder, iteration);
        builder.Append(",\"championId\":");
        CanonicalJsonWriter.Text(builder, championId);
        builder.Append(",\"challengerId\":");
        CanonicalJsonWriter.Text(builder, challengerId);
        builder.Append(",\"promoted\":");
        CanonicalJsonWriter.Boolean(builder, decision.Promoted);
        builder.Append(",\"reason\":");
        CanonicalJsonWriter.Text(builder, decision.Reason);
        builder.Append(",\"winRate\":");
        CanonicalJsonWriter.Number(builder, report.ChallengerWins / (double)(2 * report.Seeds));
        builder.Append(",\"meanBbPer100\":");
        CanonicalJsonWriter.Number(builder, report.MeanBbPer100);
        builder.Append(",\"lowerBoundBbPer100\":");
        CanonicalJsonWriter.Number(builder, report.LowerBoundBbPer100);
        builder.Append(",\"marginBbPer100\":");
        CanonicalJsonWriter.Number(builder, decision.MarginBbPer100);
        builder.Append(",\"seeds\":");
        CanonicalJsonWriter.Number(builder, report.Seeds);
        builder.Append(",\"verified\":");
        CanonicalJsonWriter.Boolean(builder, verified);
        builder.Append(",\"outcome\":");
        CanonicalJsonWriter.Text(builder, outcome);
        builder.Append('}');
        return builder.ToString();
    }

    private static string BuildInertLedgerEntry(
        int iteration,
        string championId,
        string challengerId,
        string reason)
    {
        var builder = new StringBuilder(256);
        builder.Append("{\"iteration\":");
        CanonicalJsonWriter.Number(builder, iteration);
        builder.Append(",\"championId\":");
        CanonicalJsonWriter.Text(builder, championId);
        builder.Append(",\"challengerId\":");
        CanonicalJsonWriter.Text(builder, challengerId);
        builder.Append(",\"promoted\":false,\"reason\":");
        CanonicalJsonWriter.Text(builder, reason);
        builder.Append(",\"verified\":false,\"outcome\":");
        CanonicalJsonWriter.Text(builder, InertProposalOutcome);
        builder.Append('}');
        return builder.ToString();
    }

    private static List<LedgerEntry> LoadLedger(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, LedgerFileName);
        var entries = new List<LedgerEntry>();
        if (!File.Exists(path))
        {
            return entries;
        }

        var text = File.ReadAllText(path);
        var integrity = LedgerChain.Verify(text);
        if (!integrity.Verified)
        {
            throw new InvalidOperationException(
                $"The training ledger '{path}' failed its hash-chain integrity check: {integrity.Reason}.");
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"The training ledger '{path}' is not a JSON array.");
            }

            var seen = new HashSet<(int Iteration, string ChampionId, string ChallengerId)>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object
                    || !element.TryGetProperty("iteration", out var iterationElement)
                    || !iterationElement.TryGetInt32(out var iteration)
                    || !element.TryGetProperty("championId", out var championElement)
                    || championElement.ValueKind != JsonValueKind.String
                    || !element.TryGetProperty("challengerId", out var challengerElement)
                    || challengerElement.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException(
                        $"The training ledger '{path}' contains an entry without iteration, championId and challengerId.");
                }

                if (!LedgerChain.TrySplit(element.GetRawText(), out var core, out _, out _))
                {
                    throw new InvalidOperationException(
                        $"The training ledger '{path}' contains an entry with partial chain fields.");
                }

                var key = (
                    iteration,
                    championElement.GetString()!,
                    challengerElement.GetString()!);
                if (seen.Add(key))
                {
                    entries.Add(new LedgerEntry(key, core));
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The training ledger '{path}' could not be parsed: {exception.Message}",
                exception);
        }

        return entries;
    }

    private static void UpsertLedger(
        List<LedgerEntry> entries,
        int iteration,
        string championId,
        string challengerId,
        string coreJson)
    {
        var key = (iteration, championId, challengerId);
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Key == key)
            {
                entries[index] = new LedgerEntry(key, coreJson);
                return;
            }
        }

        entries.Add(new LedgerEntry(key, coreJson));
    }

    private static string BuildLedgerArray(IReadOnlyList<LedgerEntry> entries)
    {
        var cores = new string[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            cores[index] = entries[index].CoreJson;
        }

        return LedgerChain.BuildChainedArray(cores);
    }

    private static IReadOnlyList<string> ReadLedgerEntries(string ledgerJson)
    {
        using var document = JsonDocument.Parse(ledgerJson);
        return document.RootElement
            .EnumerateArray()
            .Select(static element => element.GetRawText())
            .ToArray();
    }

    private static string BuildChampionPointer(string strategyId, int iteration, string? evaluationReportHash = null)
    {
        var builder = new StringBuilder(160);
        builder.Append("{\"strategyId\":");
        CanonicalJsonWriter.Text(builder, strategyId);
        builder.Append(",\"iteration\":");
        CanonicalJsonWriter.Number(builder, iteration);
        if (!string.IsNullOrEmpty(evaluationReportHash))
        {
            builder.Append(",\"evaluationReportHash\":");
            CanonicalJsonWriter.Text(builder, evaluationReportHash);
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static void WriteUtf8(string directory, string fileName, string content) =>
        File.WriteAllText(Path.Combine(directory, fileName), content, Utf8);
}
