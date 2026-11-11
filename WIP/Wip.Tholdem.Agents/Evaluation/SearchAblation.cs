using System.Globalization;
using System.Text;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Randomness;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Evaluation;

/// <summary>The four equal-budget proposal arms of the round-seven ablation.</summary>
public enum SearchArm
{
    NoSearch = 0,
    Random = 1,
    Grid = 2,
    Greedy = 3,
}

/// <summary>One starting genome and opponent task the ablation searches from.</summary>
public sealed record SearchTask(string TaskId, StrategyGenome Start, string CompositionId);

/// <summary>
/// The pre-registered, bounded inputs of one equal-budget proposer ablation. Every arm of one run
/// spends exactly <see cref="CandidatesPerArm"/> selection evaluations per task; the remaining
/// fields fix the selection and holdout evidence before any simulation starts.
/// </summary>
public sealed record SearchAblationOptions(
    GameConfiguration Game,
    FrozenOpponentPool Pool,
    IReadOnlyList<SearchTask> Tasks,
    int CandidatesPerArm,
    int SelectionBlocks,
    int SelectionHandsPerMatch,
    int HoldoutBlocks,
    int HoldoutHandsPerMatch,
    double MinimumPracticalEffectBbPer100,
    double ConfidenceLevel,
    double AnchorRegressionToleranceBbPer100,
    ulong Seed);

/// <summary>
/// The hands every arm is guaranteed to spend. <see cref="SelectionHandsPerArmPerTask"/> counts
/// focal-seat hands the same way the evaluator counts a unit: one hand per block, focal rotation and
/// seat match for every candidate. <see cref="TotalHands"/> adds the best candidate's holdout and
/// anchor evaluations (two holdout plans) for every arm and task.
/// </summary>
public sealed record SearchBudget(
    int CandidatesPerArm,
    int SelectionBlocks,
    int SelectionHandsPerMatch,
    int HoldoutBlocks,
    int HoldoutHandsPerMatch,
    int RotationsPerBlock,
    long SelectionHandsPerArmPerTask,
    long TotalHands);

/// <summary>One evaluated candidate genome and its selection-plan mean.</summary>
public sealed record SearchCandidate(
    string Arm,
    string TaskId,
    int Index,
    StrategyGenome Genome,
    double SelectionMeanBbPer100);

/// <summary>One arm's best candidate, its holdout evidence and the promotion gate's verdict.</summary>
public sealed record SearchArmResult(
    string Arm,
    string TaskId,
    string BestPolicyHash,
    double BestSelectionMeanBbPer100,
    double HoldoutMeanBbPer100,
    double HoldoutLowerBoundBbPer100,
    string Verdict,
    string Reason);

/// <summary>
/// The canonical equal-budget ablation report. Every field is derived from the options and the
/// paired simulations; the canonical JSON has a fixed key order and contains no clock, GUID, culture
/// or ambient randomness.
/// </summary>
public sealed record SearchAblationReport(
    string Schema,
    ulong Seed,
    SearchBudget Budget,
    IReadOnlyList<string> TaskIds,
    IReadOnlyList<SearchCandidate> Candidates,
    IReadOnlyList<SearchArmResult> Results,
    string Outcome)
{
    /// <summary>The content address of the canonical report.</summary>
    public string ReportHash => StrategyArtifact.ComputeHash(ToCanonicalJson());

    /// <summary>The fixed-key-order, culture-free JSON the report hash covers.</summary>
    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(
            1024 + (Candidates.Count * 256) + (Results.Count * 320));
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, Schema);
        builder.Append(",\"seed\":");
        CanonicalJsonWriter.Number(builder, Seed);
        builder.Append(",\"budget\":{\"candidatesPerArm\":");
        CanonicalJsonWriter.Number(builder, Budget.CandidatesPerArm);
        builder.Append(",\"selectionBlocks\":");
        CanonicalJsonWriter.Number(builder, Budget.SelectionBlocks);
        builder.Append(",\"selectionHandsPerMatch\":");
        CanonicalJsonWriter.Number(builder, Budget.SelectionHandsPerMatch);
        builder.Append(",\"holdoutBlocks\":");
        CanonicalJsonWriter.Number(builder, Budget.HoldoutBlocks);
        builder.Append(",\"holdoutHandsPerMatch\":");
        CanonicalJsonWriter.Number(builder, Budget.HoldoutHandsPerMatch);
        builder.Append(",\"rotationsPerBlock\":");
        CanonicalJsonWriter.Number(builder, Budget.RotationsPerBlock);
        builder.Append(",\"selectionHandsPerArmPerTask\":");
        CanonicalJsonWriter.Number(builder, Budget.SelectionHandsPerArmPerTask);
        builder.Append(",\"totalHands\":");
        CanonicalJsonWriter.Number(builder, Budget.TotalHands);
        builder.Append("},\"taskIds\":[");
        for (var index = 0; index < TaskIds.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.Text(builder, TaskIds[index]);
        }

        builder.Append("],\"candidates\":[");
        for (var index = 0; index < Candidates.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var candidate = Candidates[index];
            builder.Append("{\"arm\":");
            CanonicalJsonWriter.Text(builder, candidate.Arm);
            builder.Append(",\"taskId\":");
            CanonicalJsonWriter.Text(builder, candidate.TaskId);
            builder.Append(",\"index\":");
            CanonicalJsonWriter.Number(builder, candidate.Index);
            builder.Append(",\"genome\":");
            AppendGenome(builder, candidate.Genome);
            builder.Append(",\"selectionMeanBbPer100\":");
            CanonicalJsonWriter.Number(builder, candidate.SelectionMeanBbPer100);
            builder.Append('}');
        }

        builder.Append("],\"results\":[");
        for (var index = 0; index < Results.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var result = Results[index];
            builder.Append("{\"arm\":");
            CanonicalJsonWriter.Text(builder, result.Arm);
            builder.Append(",\"taskId\":");
            CanonicalJsonWriter.Text(builder, result.TaskId);
            builder.Append(",\"bestPolicyHash\":");
            CanonicalJsonWriter.Text(builder, result.BestPolicyHash);
            builder.Append(",\"bestSelectionMeanBbPer100\":");
            CanonicalJsonWriter.Number(builder, result.BestSelectionMeanBbPer100);
            builder.Append(",\"holdoutMeanBbPer100\":");
            CanonicalJsonWriter.Number(builder, result.HoldoutMeanBbPer100);
            builder.Append(",\"holdoutLowerBoundBbPer100\":");
            CanonicalJsonWriter.Number(builder, result.HoldoutLowerBoundBbPer100);
            builder.Append(",\"verdict\":");
            CanonicalJsonWriter.Text(builder, result.Verdict);
            builder.Append(",\"reason\":");
            CanonicalJsonWriter.Text(builder, result.Reason);
            builder.Append('}');
        }

        builder.Append("],\"outcome\":");
        CanonicalJsonWriter.Text(builder, Outcome);
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendGenome(StringBuilder builder, StrategyGenome genome)
    {
        builder.Append("{\"raiseThreshold\":");
        CanonicalJsonWriter.Number(builder, genome.RaiseThreshold);
        builder.Append(",\"callThreshold\":");
        CanonicalJsonWriter.Number(builder, genome.CallThreshold);
        builder.Append(",\"bluffFrequency\":");
        CanonicalJsonWriter.Number(builder, genome.BluffFrequency);
        builder.Append(",\"sizingMultiplier\":");
        CanonicalJsonWriter.Number(builder, genome.SizingMultiplier);
        builder.Append('}');
    }
}

/// <summary>
/// The reusable equal-budget proposal ablation. For every task it builds a protected selection plan,
/// a holdout plan and an anchor plan, then spends exactly the same selection budget on four arms
/// (no search, deterministic random sampling, a one-parameter grid and a two-round greedy search).
/// The best candidate per arm is taken by selection mean (ties to the lowest index), re-evaluated on
/// the holdout and anchor plans and handed to <see cref="PopulationPromotionGate.Decide"/>. The
/// run-level outcome is <c>benefit</c> only when a non-search arm verifies more tasks than the
/// no-search control. There is no clock, GUID, culture or ambient randomness.
/// </summary>
public static class SearchAblation
{
    /// <summary>The stable schema of the canonical report.</summary>
    public const string ReportSchema = "tholdem.search-ablation.v1";

    /// <summary>The stable spellings of the run-level outcome.</summary>
    public const string BenefitOutcome = "benefit";

    /// <summary>The stable spellings of the run-level outcome.</summary>
    public const string NoBenefitOutcome = "no-benefit";

    /// <summary>The evaluator's focal-rotation count for a four-seat table.</summary>
    public const int RotationsPerBlock = 4;

    public const int MinimumTasks = 1;

    public const int MaximumTasks = 8;

    public const int MaximumBlocks = 500;

    public const int MaximumHandsPerMatch = 100;

    private const int ParameterCount = 4;

    private const double Nudge = 0.1;

    private static readonly SearchArm[] Arms =
    {
        SearchArm.NoSearch,
        SearchArm.Random,
        SearchArm.Grid,
        SearchArm.Greedy,
    };

    /// <summary>
    /// Validates every bound, builds the protected plans per task, runs all four arms at the same
    /// candidate budget and returns the canonical report. A null outcome is a valid result.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// An option, task, composition or genome is outside the documented contract; the refusal carries
    /// a stable code in its message.
    /// </exception>
    public static SearchAblationReport Run(SearchAblationOptions options)
    {
        ValidateOptions(options);

        var tasks = options.Tasks;
        var taskIds = new string[tasks.Count];
        var candidates = new List<SearchCandidate>(
            tasks.Count * Arms.Length * options.CandidatesPerArm);
        var results = new List<SearchArmResult>(tasks.Count * Arms.Length);

        for (var taskIndex = 0; taskIndex < tasks.Count; taskIndex++)
        {
            var task = tasks[taskIndex];
            taskIds[taskIndex] = task.TaskId;
            var start = task.Start.Clamp();
            var startPolicyHash = StrategyArtifact.Create(task.Start, 0, null).Hash;
            var plans = BuildPlans(options, task, taskIndex);

            foreach (var arm in Arms)
            {
                var evaluated = EvaluateArm(
                    arm,
                    task,
                    start,
                    taskIndex,
                    options,
                    plans,
                    startPolicyHash);
                var bestIndex = BestIndex(evaluated);
                for (var index = 0; index < evaluated.Count; index++)
                {
                    candidates.Add(new SearchCandidate(
                        arm.ToString(),
                        task.TaskId,
                        index,
                        evaluated[index].Genome,
                        evaluated[index].MeanBbPer100));
                }

                results.Add(EvaluateBest(
                    arm,
                    task,
                    start,
                    startPolicyHash,
                    options,
                    plans,
                    evaluated,
                    bestIndex));
            }
        }

        return new SearchAblationReport(
            ReportSchema,
            options.Seed,
            CreateBudget(options, tasks.Count),
            taskIds,
            candidates,
            results,
            ClassifyOutcome(results));
    }

    private static List<EvaluatedCandidate> EvaluateArm(
        SearchArm arm,
        SearchTask task,
        StrategyGenome start,
        int taskIndex,
        SearchAblationOptions options,
        TaskPlans plans,
        string startPolicyHash)
    {
        var evaluated = new List<EvaluatedCandidate>(options.CandidatesPerArm);
        var incumbentId = "task-start-" + task.TaskId;

        double Evaluate(StrategyGenome genome, int index)
        {
            var challengerId = "candidate-" + arm.ToString() + "-" + task.TaskId + "-"
                + index.ToString(CultureInfo.InvariantCulture);
            var report = PopulationMatchEvaluator.Evaluate(
                new PopulationEvaluationRequest(plans.Selection, options.Pool),
                new StrategyAgent(genome, challengerId),
                challengerId,
                StrategyArtifact.Create(genome, 0, null).Hash,
                new StrategyAgent(start, incumbentId),
                incumbentId,
                startPolicyHash);
            return report.Estimate.MeanBbPer100;
        }

        switch (arm)
        {
            case SearchArm.NoSearch:
                for (var index = 0; index < options.CandidatesPerArm; index++)
                {
                    evaluated.Add(new EvaluatedCandidate(start, Evaluate(start, index)));
                }

                break;

            case SearchArm.Random:
            {
                var random = new Xorshift64Star(
                    SeedDerivation.Derive(options.Seed, taskIndex + 1, 100 + (int)arm, 0, 0));
                for (var index = 0; index < options.CandidatesPerArm; index++)
                {
                    var genome = new StrategyGenome(
                        NextUnit(ref random),
                        NextUnit(ref random),
                        NextUnit(ref random),
                        StrategyGenome.MinimumSizingMultiplier
                            + (NextUnit(ref random) * (StrategyGenome.MaximumSizingMultiplier
                                - StrategyGenome.MinimumSizingMultiplier)),
                        start.PromptText,
                        start.Skills).Clamp();
                    evaluated.Add(new EvaluatedCandidate(genome, Evaluate(genome, index)));
                }

                break;
            }

            case SearchArm.Grid:
                for (var index = 0; index < options.CandidatesPerArm; index++)
                {
                    var delta = index < ParameterCount ? Nudge : -Nudge;
                    var genome = NudgeFrom(start, index % ParameterCount, delta);
                    evaluated.Add(new EvaluatedCandidate(genome, Evaluate(genome, index)));
                }

                break;

            case SearchArm.Greedy:
            {
                var current = start;
                var rounds = options.CandidatesPerArm / ParameterCount;
                for (var round = 0; round < rounds; round++)
                {
                    var neighbours = new StrategyGenome[ParameterCount];
                    var means = new double[ParameterCount];
                    for (var parameter = 0; parameter < ParameterCount; parameter++)
                    {
                        neighbours[parameter] = NudgeFrom(current, parameter, Nudge);
                        means[parameter] = Evaluate(
                            neighbours[parameter],
                            (round * ParameterCount) + parameter);
                    }

                    var best = 0;
                    for (var parameter = 1; parameter < ParameterCount; parameter++)
                    {
                        if (means[parameter] > means[best])
                        {
                            best = parameter;
                        }
                    }

                    for (var parameter = 0; parameter < ParameterCount; parameter++)
                    {
                        evaluated.Add(new EvaluatedCandidate(neighbours[parameter], means[parameter]));
                    }

                    current = neighbours[best];
                }

                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(arm), arm, "Unknown search arm.");
        }

        return evaluated;
    }

    private static SearchArmResult EvaluateBest(
        SearchArm arm,
        SearchTask task,
        StrategyGenome start,
        string startPolicyHash,
        SearchAblationOptions options,
        TaskPlans plans,
        IReadOnlyList<EvaluatedCandidate> evaluated,
        int bestIndex)
    {
        var best = evaluated[bestIndex];
        var challengerId = "search-best-" + arm.ToString() + "-" + task.TaskId;
        var incumbentId = "task-start-" + task.TaskId;
        var challengerPolicyHash = StrategyArtifact.Create(best.Genome, 0, null).Hash;

        var holdout = EvaluatePlan(
            plans.Holdout,
            options.Pool,
            best.Genome,
            start,
            challengerId,
            challengerPolicyHash,
            incumbentId,
            startPolicyHash);
        var anchor = EvaluatePlan(
            plans.Anchor,
            options.Pool,
            best.Genome,
            start,
            challengerId,
            challengerPolicyHash,
            incumbentId,
            startPolicyHash);
        var decision = PopulationPromotionGate.Decide(
            holdout,
            anchor,
            plans.Holdout,
            plans.Anchor);

        return new SearchArmResult(
            arm.ToString(),
            task.TaskId,
            challengerPolicyHash,
            best.MeanBbPer100,
            holdout.Estimate.MeanBbPer100,
            holdout.Estimate.LowerBoundBbPer100,
            decision.Verdict.ToString(),
            decision.Reason);
    }

    private static PopulationEvaluationReport EvaluatePlan(
        EvaluationPlanV2 plan,
        FrozenOpponentPool pool,
        StrategyGenome genome,
        StrategyGenome start,
        string challengerId,
        string challengerPolicyHash,
        string incumbentId,
        string incumbentPolicyHash) =>
        PopulationMatchEvaluator.Evaluate(
            new PopulationEvaluationRequest(plan, pool),
            new StrategyAgent(genome, challengerId),
            challengerId,
            challengerPolicyHash,
            new StrategyAgent(start, incumbentId),
            incumbentId,
            incumbentPolicyHash);

    private static TaskPlans BuildPlans(
        SearchAblationOptions options,
        SearchTask task,
        int taskIndex)
    {
        var suffix = (taskIndex + 1).ToString(CultureInfo.InvariantCulture) + "-" + task.TaskId;
        var training = new SeedDomain(
            "search-training-" + suffix,
            SeedPurpose.Training,
            DomainBaseSeed(options.Seed, taskIndex, SeedPurpose.Training),
            1);
        var selectionDomain = new SeedDomain(
            "search-selection-domain-" + suffix,
            SeedPurpose.Selection,
            DomainBaseSeed(options.Seed, taskIndex, SeedPurpose.Selection),
            options.SelectionBlocks);
        var audit = new SeedDomain(
            "search-audit-" + suffix,
            SeedPurpose.Audit,
            DomainBaseSeed(options.Seed, taskIndex, SeedPurpose.Audit),
            1);

        // The population evaluator always consumes plan.Holdout as its evaluation domain. The
        // selection plan therefore carries a protected selection-base block of SelectionBlocks seeds
        // in that role, while the holdout and anchor plans carry the strictly disjoint holdout block.
        // Every plan still declares the four required purposes: training and audit are tiny, and the
        // Selection-purpose slot records the selection block size from the same base seed.
        var selectionEvaluation = new SeedDomain(
            "search-selection-" + suffix,
            SeedPurpose.Holdout,
            DomainBaseSeed(options.Seed, taskIndex, SeedPurpose.Selection),
            options.SelectionBlocks);
        var holdoutEvaluation = new SeedDomain(
            "search-holdout-" + suffix,
            SeedPurpose.Holdout,
            DomainBaseSeed(options.Seed, taskIndex, SeedPurpose.Holdout),
            options.HoldoutBlocks);

        var selectionDomains = new[]
        {
            training, selectionDomain, selectionEvaluation, audit,
        };
        var holdoutDomains = new[]
        {
            training, selectionDomain, holdoutEvaluation, audit,
        };
        var anchorComposition = string.Equals(
            task.CompositionId,
            "primary-mixed",
            StringComparison.Ordinal)
            ? "three-tag"
            : "primary-mixed";

        var selectionPlan = CreatePlan(
            options,
            new[] { task.CompositionId },
            selectionDomains,
            options.SelectionHandsPerMatch);
        var holdoutPlan = CreatePlan(
            options,
            new[] { task.CompositionId },
            holdoutDomains,
            options.HoldoutHandsPerMatch);
        var anchorPlan = CreatePlan(
            options,
            new[] { anchorComposition },
            holdoutDomains,
            options.HoldoutHandsPerMatch);
        return new TaskPlans(selectionPlan, holdoutPlan, anchorPlan);
    }

    private static EvaluationPlanV2 CreatePlan(
        SearchAblationOptions options,
        IReadOnlyList<string> compositions,
        IReadOnlyList<SeedDomain> domains,
        int handsPerSeatMatch) =>
        EvaluationPlanV2.Create(
            options.Game,
            options.Pool.PoolId,
            compositions,
            domains,
            handsPerSeatMatch,
            options.MinimumPracticalEffectBbPer100,
            options.ConfidenceLevel,
            options.CandidatesPerArm,
            options.AnchorRegressionToleranceBbPer100);

    private static ulong DomainBaseSeed(ulong seed, int taskIndex, SeedPurpose purpose) =>
        SeedDerivation.Derive(seed, taskIndex + 1, 700 + (int)purpose, 0, 0);

    private static double NextUnit(ref Xorshift64Star random) =>
        (random.Next() >> 11) * (1.0 / 9007199254740992.0);

    private static StrategyGenome NudgeFrom(
        StrategyGenome genome,
        int parameter,
        double delta)
    {
        var nudged = parameter switch
        {
            0 => genome with { RaiseThreshold = genome.RaiseThreshold + delta },
            1 => genome with { CallThreshold = genome.CallThreshold + delta },
            2 => genome with { BluffFrequency = genome.BluffFrequency + delta },
            3 => genome with { SizingMultiplier = genome.SizingMultiplier + delta },
            _ => throw new ArgumentOutOfRangeException(
                nameof(parameter),
                parameter,
                "A genome has four parameters."),
        };
        return nudged.Clamp();
    }

    private static int BestIndex(IReadOnlyList<EvaluatedCandidate> evaluated)
    {
        var best = 0;
        for (var index = 1; index < evaluated.Count; index++)
        {
            if (evaluated[index].MeanBbPer100 > evaluated[best].MeanBbPer100)
            {
                best = index;
            }
        }

        return best;
    }

    /// <summary>
    /// Classifies the run-level outcome. The proposal process under test is Greedy: a run shows a
    /// benefit only when Greedy has at least two verified improvements, zero unsafe results and
    /// strictly more verified improvements than each control arm (NoSearch, Random and Grid).
    /// Every other pattern is the valid null outcome.
    /// </summary>
    public static string ClassifyOutcome(IReadOnlyList<SearchArmResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var verified = PromotionVerdict.VerifiedImprovement.ToString();
        var unsafeVerdict = PromotionVerdict.Unsafe.ToString();
        var greedy = SearchArm.Greedy.ToString();
        var noSearch = SearchArm.NoSearch.ToString();
        var random = SearchArm.Random.ToString();
        var grid = SearchArm.Grid.ToString();

        var greedyVerified = 0;
        var greedyUnsafe = 0;
        var noSearchVerified = 0;
        var randomVerified = 0;
        var gridVerified = 0;

        foreach (var result in results)
        {
            ArgumentNullException.ThrowIfNull(result);
            var isVerified = string.Equals(result.Verdict, verified, StringComparison.Ordinal);
            if (string.Equals(result.Arm, greedy, StringComparison.Ordinal))
            {
                if (isVerified)
                {
                    greedyVerified++;
                }

                if (string.Equals(result.Verdict, unsafeVerdict, StringComparison.Ordinal))
                {
                    greedyUnsafe++;
                }

                continue;
            }

            if (!isVerified)
            {
                continue;
            }

            if (string.Equals(result.Arm, noSearch, StringComparison.Ordinal))
            {
                noSearchVerified++;
            }
            else if (string.Equals(result.Arm, random, StringComparison.Ordinal))
            {
                randomVerified++;
            }
            else if (string.Equals(result.Arm, grid, StringComparison.Ordinal))
            {
                gridVerified++;
            }
        }

        if (greedyVerified < 2 || greedyUnsafe > 0)
        {
            return NoBenefitOutcome;
        }

        if (greedyVerified <= noSearchVerified
            || greedyVerified <= randomVerified
            || greedyVerified <= gridVerified)
        {
            return NoBenefitOutcome;
        }

        return BenefitOutcome;
    }

    private static SearchBudget CreateBudget(
        SearchAblationOptions options,
        int taskCount)
    {
        var selectionHandsPerArmPerTask = (long)options.CandidatesPerArm
            * options.SelectionBlocks
            * RotationsPerBlock
            * options.SelectionHandsPerMatch;
        var holdoutHandsPerBest = (long)options.HoldoutBlocks
            * RotationsPerBlock
            * options.HoldoutHandsPerMatch;
        var totalHands = (long)taskCount
            * Arms.Length
            * (selectionHandsPerArmPerTask + (2 * holdoutHandsPerBest));
        return new SearchBudget(
            options.CandidatesPerArm,
            options.SelectionBlocks,
            options.SelectionHandsPerMatch,
            options.HoldoutBlocks,
            options.HoldoutHandsPerMatch,
            RotationsPerBlock,
            selectionHandsPerArmPerTask,
            totalHands);
    }

    private static void ValidateOptions(SearchAblationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Game);
        ArgumentNullException.ThrowIfNull(options.Pool);
        ArgumentNullException.ThrowIfNull(options.Tasks);

        if (options.Game.Seats != 4)
        {
            throw new ArgumentException(
                "seats-unsupported: the search ablation is a four-seat experiment; "
                + $"the game declares {options.Game.Seats} seats.",
                nameof(options));
        }

        options.Game.Validate();

        if (options.Tasks.Count is < MinimumTasks or > MaximumTasks)
        {
            throw new ArgumentException(
                $"tasks-out-of-range: between {MinimumTasks} and {MaximumTasks} tasks are required; "
                + $"got {options.Tasks.Count}.",
                nameof(options));
        }

        var taskIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in options.Tasks)
        {
            ArgumentNullException.ThrowIfNull(task, nameof(options));
            if (string.IsNullOrWhiteSpace(task.TaskId))
            {
                throw new ArgumentException(
                    "task-id-invalid: every search task requires a non-empty id.",
                    nameof(options));
            }

            if (!taskIds.Add(task.TaskId))
            {
                throw new ArgumentException(
                    $"task-id-duplicate: task id '{task.TaskId}' is declared more than once.",
                    nameof(options));
            }

            if (task.Start is null)
            {
                throw new ArgumentException(
                    $"task-start-missing: task '{task.TaskId}' has no starting genome.",
                    nameof(options));
            }

            if (string.IsNullOrWhiteSpace(task.CompositionId))
            {
                throw new ArgumentException(
                    $"composition-mismatch: task '{task.TaskId}' has no composition id.",
                    nameof(options));
            }

            var resolves = false;
            foreach (var composition in options.Pool.Compositions)
            {
                if (string.Equals(
                    composition.CompositionId,
                    task.CompositionId,
                    StringComparison.Ordinal))
                {
                    resolves = true;
                    break;
                }
            }

            if (!resolves)
            {
                throw new ArgumentException(
                    "composition-mismatch: the task composition "
                    + $"'{task.CompositionId}' does not resolve in pool '{options.Pool.PoolId}'.",
                    nameof(options));
            }
        }

        if (options.CandidatesPerArm < ParameterCount
            || options.CandidatesPerArm % ParameterCount != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.CandidatesPerArm,
                "candidates-out-of-range: CandidatesPerArm must be at least four and a multiple of four.");
        }

        RequireRange(nameof(options.SelectionBlocks), options.SelectionBlocks, MaximumBlocks);
        RequireRange(nameof(options.HoldoutBlocks), options.HoldoutBlocks, MaximumBlocks);
        RequireRange(
            nameof(options.SelectionHandsPerMatch),
            options.SelectionHandsPerMatch,
            MaximumHandsPerMatch);
        RequireRange(
            nameof(options.HoldoutHandsPerMatch),
            options.HoldoutHandsPerMatch,
            MaximumHandsPerMatch);

        if (!double.IsFinite(options.MinimumPracticalEffectBbPer100)
            || options.MinimumPracticalEffectBbPer100 < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MinimumPracticalEffectBbPer100,
                "margin-out-of-range: the minimum practical effect must be a finite, non-negative bb/100 value.");
        }

        if (!double.IsFinite(options.ConfidenceLevel)
            || options.ConfidenceLevel <= 0.0
            || options.ConfidenceLevel >= 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.ConfidenceLevel,
                "confidence-out-of-range: the confidence level must lie strictly between zero and one.");
        }

        if (!double.IsFinite(options.AnchorRegressionToleranceBbPer100)
            || options.AnchorRegressionToleranceBbPer100 < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.AnchorRegressionToleranceBbPer100,
                "tolerance-out-of-range: the anchor regression tolerance must be a finite, non-negative bb/100 value.");
        }
    }

    private static void RequireRange(string option, int value, int maximum)
    {
        if (value < 1 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                option,
                value,
                $"{option}-out-of-range: {option} must be between 1 and {maximum}; observed {value}.");
        }
    }

    private sealed record EvaluatedCandidate(StrategyGenome Genome, double MeanBbPer100);

    private sealed record TaskPlans(
        EvaluationPlanV2 Selection,
        EvaluationPlanV2 Holdout,
        EvaluationPlanV2 Anchor);
}
