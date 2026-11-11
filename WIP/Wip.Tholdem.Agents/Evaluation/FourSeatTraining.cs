using System.Globalization;
using System.Text;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Evaluation;

/// <summary>The pre-registered settings of one additive four-seat training/promotion run.</summary>
public sealed record FourSeatTrainingOptions(
    ulong Seed,
    int Iterations,
    string DataDirectory,
    GameConfiguration? Game = null,
    FrozenOpponentPool? Pool = null,
    IReadOnlyList<string>? Compositions = null,
    int SelectionBlocks = 8,
    int SelectionHandsPerMatch = 4,
    int HoldoutBlocks = 16,
    int HoldoutHandsPerMatch = 4,
    double MarginBbPer100 = 5.0,
    double ConfidenceLevel = 0.95,
    double AnchorToleranceBbPer100 = 5.0,
    StrategyGenome? StartingGenome = null,
    string? AnchorComposition = null,
    Func<StrategyGenome, int, StrategyGenome>? Proposer = null,
    bool DiagnosisDriven = false,
    Func<int, SessionSignatures>? SignatureSource = null,
    int DiagnosisHands = 20);

/// <summary>One iteration's stable outcome row.</summary>
public sealed record FourSeatTrainingIteration(
    int Iteration,
    string ChampionId,
    string ChallengerId,
    string Outcome,
    double SelectionMeanBbPer100,
    string Verdict,
    string Reason);

/// <summary>The final isolated champion, every iteration row and the run's content address.</summary>
public sealed record FourSeatTrainingRun(
    string FinalChampionId,
    IReadOnlyList<FourSeatTrainingIteration> Iterations,
    string RunHash);

/// <summary>
/// The additive four-seat training/promotion loop. Each iteration proposes one challenger, evaluates
/// it against the isolated champion on a fresh, protected selection block, and, only when the
/// selection mean is positive, spends the disjoint holdout block plus the anchor composition and asks
/// <see cref="PopulationPromotionGate"/> for a decision. Only
/// <see cref="PromotionVerdict.VerifiedImprovement"/> replaces the champion. Every artifact is
/// content addressed and persisted as canonical UTF-8 without BOM under <c>four-seat/</c>; there is
/// no clock, GUID, culture or ambient randomness, and no v1 pointer or v1 training file is read or
/// written.
/// </summary>
public static class FourSeatTraining
{
    public const string DirectoryName = "four-seat";

    public const string ChampionFileName = "active-champion.json";

    private const string RejectedSelectionOutcome = "rejected-selection";

    private const string RejectedHoldoutOutcome = "rejected-holdout";

    private const string PromotedOutcome = "promoted";

    private const string SelectionRejectedVerdict = "Rejected";

    private const string SelectionRejectedReason = "selection-mean-not-positive";

    private const string RunSummarySchema = "tholdem.four-seat-training-run.v1";

    private const string DecisionSchema = "tholdem.four-seat-decision.v1";

    private const string StrategiesDirectoryName = "strategies";

    private const string ReportsDirectoryName = "reports";

    private const int PlannedCandidates = 1;

    private const ulong DomainStream = 0x53454154UL;

    /// <summary>The distinct seed stream of the diagnosis capture match ("DIAG").</summary>
    private const ulong DiagnosisStream = 0x44494147UL;

    private const string SignaturesReportSuffix = "-signatures.json";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly SessionAnalysis NeutralAnalysis = new(0, 0, 0, 1, 0, null);

    private static readonly string[] DefaultCompositions = { "primary-mixed" };

    public static FourSeatTrainingRun Run(FourSeatTrainingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(
            options,
            out var game,
            out var pool,
            out var compositions,
            out var anchorComposition,
            out var startingGenome,
            out var dataDirectory);

        var fourSeatDirectory = Path.Combine(dataDirectory, DirectoryName);
        var strategyDirectory = Path.Combine(fourSeatDirectory, StrategiesDirectoryName);
        var reportDirectory = Path.Combine(fourSeatDirectory, ReportsDirectoryName);
        Directory.CreateDirectory(strategyDirectory);
        Directory.CreateDirectory(reportDirectory);

        var champion = StrategyArtifact.Create(startingGenome, 0, null);
        WriteStrategy(strategyDirectory, champion);

        var iterations = new List<FourSeatTrainingIteration>(options.Iterations);
        for (var iteration = 1; iteration <= options.Iterations; iteration++)
        {
            var incumbent = champion;
            var championId = incumbent.Id;
            var challengerGenome = options.DiagnosisDriven
                ? ProposeFromDiagnosis(
                    options,
                    pool,
                    compositions,
                    champion,
                    iteration,
                    dataDirectory,
                    reportDirectory)
                : Propose(options, incumbent.Genome, iteration);
            var challenger = StrategyArtifact.Create(challengerGenome, iteration, incumbent);
            WriteStrategy(strategyDirectory, challenger);

            var domains = BuildDomains(options, iteration);
            var selectionPlan = CreatePlan(
                options,
                game,
                pool,
                compositions,
                new[] { domains.Training, domains.Selection, domains.SelectionEvaluation, domains.Audit },
                options.SelectionHandsPerMatch);
            var selectionReport = Evaluate(selectionPlan, pool, challenger, incumbent);
            WriteUtf8(
                reportDirectory,
                "iter-" + iteration.ToString(CultureInfo.InvariantCulture) + "-selection.json",
                selectionReport.ToCanonicalJson());

            var selectionMean = selectionReport.Estimate.MeanBbPer100;
            if (selectionMean <= 0.0)
            {
                iterations.Add(new FourSeatTrainingIteration(
                    iteration,
                    championId,
                    challenger.Id,
                    RejectedSelectionOutcome,
                    selectionMean,
                    SelectionRejectedVerdict,
                    SelectionRejectedReason));
                continue;
            }

            var holdoutPlan = CreatePlan(
                options,
                game,
                pool,
                compositions,
                new[] { domains.Training, domains.Selection, domains.HoldoutEvaluation, domains.Audit },
                options.HoldoutHandsPerMatch);
            var anchorPlan = CreatePlan(
                options,
                game,
                pool,
                new[] { anchorComposition },
                new[] { domains.Training, domains.Selection, domains.HoldoutEvaluation, domains.Audit },
                options.HoldoutHandsPerMatch);

            var holdoutReport = Evaluate(holdoutPlan, pool, challenger, incumbent);
            var anchorReport = Evaluate(anchorPlan, pool, challenger, incumbent);
            var decision = PopulationPromotionGate.Decide(
                holdoutReport,
                anchorReport,
                holdoutPlan,
                anchorPlan);

            WriteUtf8(
                reportDirectory,
                "iter-" + iteration.ToString(CultureInfo.InvariantCulture) + "-holdout.json",
                holdoutReport.ToCanonicalJson());
            WriteUtf8(
                reportDirectory,
                "iter-" + iteration.ToString(CultureInfo.InvariantCulture) + "-decision.json",
                BuildDecisionJson(iteration, decision, holdoutReport, anchorReport));

            var promoted = decision.Verdict == PromotionVerdict.VerifiedImprovement;
            if (promoted)
            {
                champion = challenger;
                WriteUtf8(
                    fourSeatDirectory,
                    ChampionFileName,
                    BuildChampionPointer(challenger.Id, iteration, holdoutReport.ReportHash));
            }

            iterations.Add(new FourSeatTrainingIteration(
                iteration,
                championId,
                challenger.Id,
                promoted ? PromotedOutcome : RejectedHoldoutOutcome,
                selectionMean,
                decision.Verdict.ToString(),
                decision.Reason));
        }

        return new FourSeatTrainingRun(
            champion.Id,
            iterations,
            StrategyArtifact.ComputeHash(BuildRunSummary(options.Seed, iterations, champion.Id)));
    }

    private static void Validate(
        FourSeatTrainingOptions options,
        out GameConfiguration game,
        out FrozenOpponentPool pool,
        out IReadOnlyList<string> compositions,
        out string anchorComposition,
        out StrategyGenome startingGenome,
        out string dataDirectory)
    {
        if (options.Iterations < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Iterations,
                "Iterations must not be negative.");
        }

        if (options.SelectionBlocks < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.SelectionBlocks,
                "A selection block needs at least one seed.");
        }

        if (options.SelectionHandsPerMatch < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.SelectionHandsPerMatch,
                "A selection match needs at least one hand.");
        }

        if (options.HoldoutBlocks < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.HoldoutBlocks,
                "A holdout block needs at least one seed.");
        }

        if (options.HoldoutHandsPerMatch < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.HoldoutHandsPerMatch,
                "A holdout match needs at least one hand.");
        }

        if (options.DiagnosisDriven && options.DiagnosisHands < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.DiagnosisHands,
                "A diagnosis capture needs at least one hand.");
        }

        if (!double.IsFinite(options.MarginBbPer100) || options.MarginBbPer100 < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MarginBbPer100,
                "The margin must be a finite, non-negative bb/100 value.");
        }

        if (!double.IsFinite(options.ConfidenceLevel)
            || options.ConfidenceLevel <= 0.0
            || options.ConfidenceLevel >= 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.ConfidenceLevel,
                "The confidence level must lie strictly between zero and one.");
        }

        if (!double.IsFinite(options.AnchorToleranceBbPer100)
            || options.AnchorToleranceBbPer100 < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.AnchorToleranceBbPer100,
                "The anchor tolerance must be a finite, non-negative bb/100 value.");
        }

        if (string.IsNullOrWhiteSpace(options.DataDirectory))
        {
            throw new ArgumentException(
                "A four-seat training run requires a data directory.",
                nameof(options));
        }

        game = (options.Game ?? GameConfiguration.FourSeatCash()).Normalized();
        if (game.Seats != 4)
        {
            throw new ArgumentException(
                "seats-unsupported: the four-seat training loop requires a four-seat game; "
                + "the game declares " + game.Seats + " seats.",
                nameof(options));
        }

        pool = options.Pool ?? FrozenOpponentPool.SelectionV1;
        var configured = options.Compositions ?? DefaultCompositions;
        if (configured.Count < 1)
        {
            throw new ArgumentException(
                "At least one composition is required.",
                nameof(options));
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        var resolved = new List<string>(configured.Count);
        foreach (var composition in configured)
        {
            if (string.IsNullOrWhiteSpace(composition))
            {
                throw new ArgumentException(
                    "A composition id must be non-empty.",
                    nameof(options));
            }

            if (!unique.Add(composition))
            {
                throw new ArgumentException(
                    "Composition id '" + composition + "' is declared more than once.",
                    nameof(options));
            }

            EnsureComposition(pool, composition);
            resolved.Add(composition);
        }

        compositions = resolved;
        anchorComposition = string.IsNullOrWhiteSpace(options.AnchorComposition)
            ? resolved.Count == 1 && string.Equals(resolved[0], "three-tag", StringComparison.Ordinal)
                ? "primary-mixed"
                : "three-tag"
            : options.AnchorComposition;
        EnsureComposition(pool, anchorComposition);

        startingGenome = (options.StartingGenome ?? new StrategyGenome()).Clamp();
        dataDirectory = Path.GetFullPath(options.DataDirectory);
    }

    private static void EnsureComposition(FrozenOpponentPool pool, string compositionId)
    {
        try
        {
            pool.GetComposition(compositionId);
        }
        catch (KeyNotFoundException)
        {
            throw new ArgumentException(
                "Unknown composition '" + compositionId + "' in opponent pool '"
                + pool.PoolId + "'.",
                nameof(compositionId));
        }
    }

    private static StrategyGenome Propose(
        FourSeatTrainingOptions options,
        StrategyGenome championGenome,
        int iteration) =>
        options.Proposer is null
            ? new DeterministicStrategyProposer().Propose(championGenome, NeutralAnalysis, iteration)
            : options.Proposer(championGenome, iteration);

    /// <summary>
    /// The diagnosis path: resolve one signature set (the injectable source, or a real four-seat
    /// capture), persist its canonical form, then let <see cref="DiagnosisDrivenProposer"/> derive the
    /// challenger from the neutral analysis. The deterministic path is not consulted.
    /// </summary>
    private static StrategyGenome ProposeFromDiagnosis(
        FourSeatTrainingOptions options,
        FrozenOpponentPool pool,
        IReadOnlyList<string> compositions,
        StrategyArtifact champion,
        int iteration,
        string dataDirectory,
        string reportDirectory)
    {
        var signatures = options.SignatureSource?.Invoke(iteration)
            ?? CaptureSignatures(options, pool, compositions, champion, iteration, dataDirectory);
        WriteUtf8(
            reportDirectory,
            "iter-" + iteration.ToString(CultureInfo.InvariantCulture) + SignaturesReportSuffix,
            signatures.ToCanonicalJson());
        return new DiagnosisDrivenProposer(signatures).Propose(
            champion.Genome,
            NeutralAnalysis,
            iteration);
    }

    /// <summary>
    /// Runs one four-seat training match — the champion plus the first three opponents of the first
    /// composition — on a diagnosis-only seed stream derived from the run seed and iteration, then
    /// reduces the recorded hands to failure signatures.
    /// </summary>
    private static SessionSignatures CaptureSignatures(
        FourSeatTrainingOptions options,
        FrozenOpponentPool pool,
        IReadOnlyList<string> compositions,
        StrategyArtifact champion,
        int iteration,
        string dataDirectory)
    {
        var composition = pool.GetComposition(compositions[0]);
        if (composition.OpponentIds.Count < 3)
        {
            throw new InvalidOperationException(
                "diagnosis-capture-requires-three-opponents: composition '"
                + composition.CompositionId
                + "' declares "
                + composition.OpponentIds.Count
                + " opponents.");
        }

        var agents = new IPokerAgent[4];
        agents[0] = new StrategyAgent(champion.Genome, champion.Id);
        for (var index = 0; index < 3; index++)
        {
            agents[index + 1] = pool.Create(composition.OpponentIds[index]);
        }

        var capture = TrainingTrajectoryCapture.Capture(
            dataDirectory,
            iteration,
            new SimulationOptions(
                SeedDerivation.Derive(
                    options.Seed,
                    iteration,
                    0,
                    (int)SeedPurpose.Training,
                    DiagnosisStream),
                options.DiagnosisHands),
            agents);
        return FailureSignatureExtractor.Extract(capture.Hands);
    }

    private static IterationDomains BuildDomains(FourSeatTrainingOptions options, int iteration)
    {
        var training = new SeedDomain(
            "four-seat-training-" + iteration.ToString(CultureInfo.InvariantCulture),
            SeedPurpose.Training,
            DomainBaseSeed(options.Seed, iteration, SeedPurpose.Training),
            1);
        var selectionBase = DomainBaseSeed(options.Seed, iteration, SeedPurpose.Selection);
        var selection = new SeedDomain(
            "four-seat-selection-" + iteration.ToString(CultureInfo.InvariantCulture),
            SeedPurpose.Selection,
            selectionBase,
            options.SelectionBlocks);
        var audit = new SeedDomain(
            "four-seat-audit-" + iteration.ToString(CultureInfo.InvariantCulture),
            SeedPurpose.Audit,
            DomainBaseSeed(options.Seed, iteration, SeedPurpose.Audit),
            1);

        // The population evaluator always consumes plan.Holdout as its evaluation domain. The
        // selection plan therefore carries a protected selection-base block of SelectionBlocks seeds
        // in that role, while the holdout and anchor plans carry the disjoint holdout block. Every
        // plan still declares the four required purposes: training and audit are tiny, and the
        // Selection-purpose slot records the selection block size from the same base seed.
        var selectionEvaluation = new SeedDomain(
            "four-seat-selection-eval-" + iteration.ToString(CultureInfo.InvariantCulture),
            SeedPurpose.Holdout,
            selectionBase,
            options.SelectionBlocks);
        var holdoutEvaluation = new SeedDomain(
            "four-seat-holdout-" + iteration.ToString(CultureInfo.InvariantCulture),
            SeedPurpose.Holdout,
            DomainBaseSeed(options.Seed, iteration, SeedPurpose.Holdout),
            options.HoldoutBlocks);

        return new IterationDomains(training, selection, selectionEvaluation, holdoutEvaluation, audit);
    }

    private static ulong DomainBaseSeed(ulong seed, int iteration, SeedPurpose purpose) =>
        SeedDerivation.Derive(seed, iteration, 0, (int)purpose, DomainStream);

    private static EvaluationPlanV2 CreatePlan(
        FourSeatTrainingOptions options,
        GameConfiguration game,
        FrozenOpponentPool pool,
        IReadOnlyList<string> compositions,
        IReadOnlyList<SeedDomain> domains,
        int handsPerSeatMatch) =>
        EvaluationPlanV2.Create(
            game,
            pool.PoolId,
            compositions,
            domains,
            handsPerSeatMatch,
            options.MarginBbPer100,
            options.ConfidenceLevel,
            PlannedCandidates,
            options.AnchorToleranceBbPer100);

    private static PopulationEvaluationReport Evaluate(
        EvaluationPlanV2 plan,
        FrozenOpponentPool pool,
        StrategyArtifact challenger,
        StrategyArtifact incumbent) =>
        PopulationMatchEvaluator.Evaluate(
            new PopulationEvaluationRequest(plan, pool, SeatPermutationMode.FocalRotations),
            new StrategyAgent(challenger.Genome, challenger.Id),
            challenger.Id,
            challenger.Hash,
            new StrategyAgent(incumbent.Genome, incumbent.Id),
            incumbent.Id,
            incumbent.Hash);

    private static void WriteStrategy(string strategyDirectory, StrategyArtifact artifact) =>
        WriteUtf8(strategyDirectory, artifact.Id + ".json", artifact.ToCanonicalJson());

    private static string BuildChampionPointer(string strategyId, int iteration, string reportHash)
    {
        var builder = new StringBuilder(160);
        builder.Append("{\"strategyId\":");
        CanonicalJsonWriter.Text(builder, strategyId);
        builder.Append(",\"iteration\":");
        CanonicalJsonWriter.Number(builder, iteration);
        builder.Append(",\"evaluationReportHash\":");
        CanonicalJsonWriter.Text(builder, reportHash);
        builder.Append('}');
        return builder.ToString();
    }

    private static string BuildDecisionJson(
        int iteration,
        PopulationPromotionDecision decision,
        PopulationEvaluationReport holdout,
        PopulationEvaluationReport anchor)
    {
        var builder = new StringBuilder(768);
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, DecisionSchema);
        builder.Append(",\"iteration\":");
        CanonicalJsonWriter.Number(builder, iteration);
        builder.Append(",\"verdict\":");
        CanonicalJsonWriter.Text(builder, decision.Verdict.ToString());
        builder.Append(",\"reason\":");
        CanonicalJsonWriter.Text(builder, decision.Reason);
        builder.Append(",\"lowerBoundBbPer100\":");
        CanonicalJsonWriter.Number(builder, decision.LowerBoundBbPer100);
        builder.Append(",\"marginBbPer100\":");
        CanonicalJsonWriter.Number(builder, decision.MarginBbPer100);
        builder.Append(",\"unitCount\":");
        CanonicalJsonWriter.Number(builder, decision.UnitCount);
        builder.Append(",\"adjustedConfidenceLevel\":");
        CanonicalJsonWriter.Number(builder, decision.AdjustedConfidenceLevel);
        builder.Append(",\"planHash\":");
        CanonicalJsonWriter.Text(builder, holdout.PlanHash);
        builder.Append(",\"anchorPlanHash\":");
        CanonicalJsonWriter.Text(builder, anchor.PlanHash);
        builder.Append(",\"reportHash\":");
        CanonicalJsonWriter.Text(builder, holdout.ReportHash);
        builder.Append(",\"anchorReportHash\":");
        CanonicalJsonWriter.Text(builder, anchor.ReportHash);
        builder.Append('}');
        return builder.ToString();
    }

    private static string BuildRunSummary(
        ulong seed,
        IReadOnlyList<FourSeatTrainingIteration> iterations,
        string finalChampionId)
    {
        var builder = new StringBuilder(512 + (iterations.Count * 320));
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, RunSummarySchema);
        builder.Append(",\"seed\":");
        CanonicalJsonWriter.Number(builder, seed);
        builder.Append(",\"iterations\":");
        CanonicalJsonWriter.Number(builder, iterations.Count);
        builder.Append(",\"finalChampionId\":");
        CanonicalJsonWriter.Text(builder, finalChampionId);
        builder.Append(",\"rows\":[");
        for (var index = 0; index < iterations.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var row = iterations[index];
            builder.Append("{\"iteration\":");
            CanonicalJsonWriter.Number(builder, row.Iteration);
            builder.Append(",\"championId\":");
            CanonicalJsonWriter.Text(builder, row.ChampionId);
            builder.Append(",\"challengerId\":");
            CanonicalJsonWriter.Text(builder, row.ChallengerId);
            builder.Append(",\"outcome\":");
            CanonicalJsonWriter.Text(builder, row.Outcome);
            builder.Append(",\"selectionMeanBbPer100\":");
            CanonicalJsonWriter.Number(builder, row.SelectionMeanBbPer100);
            builder.Append(",\"verdict\":");
            CanonicalJsonWriter.Text(builder, row.Verdict);
            builder.Append(",\"reason\":");
            CanonicalJsonWriter.Text(builder, row.Reason);
            builder.Append('}');
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static void WriteUtf8(string directory, string fileName, string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content, Utf8);
    }

    private sealed record IterationDomains(
        SeedDomain Training,
        SeedDomain Selection,
        SeedDomain SelectionEvaluation,
        SeedDomain HoldoutEvaluation,
        SeedDomain Audit);
}
