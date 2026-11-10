using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>Everything one bounded meta-run needs. There is no clock value in it; the wall-clock cap is a budget.</summary>
public sealed record MetaRunOptions(
    ulong Seed,
    int MetaIterations,
    int InnerIterations,
    int TrainingHands,
    int EvaluationSeeds,
    int EvaluationHandsPerMatch,
    EvaluationCriteria Criteria,
    int MetaSeeds,
    int MetaHandsPerMatch,
    int FreshSeeds,
    int FreshHandsPerMatch,
    double RequiredHitRateImprovement,
    string? DataDirectory = null,
    string? StartingStrategyId = null,
    int MaxInnerIterations = 400,
    int MaxEvaluationsPerEpisode = 200,
    TimeSpan? MaxWallClock = null,
    bool UseHarnessRefinement = false)
{
    public static TimeSpan DefaultMaxWallClock { get; } = TimeSpan.FromMinutes(2);
}

/// <summary>One inner-loop episode result. Verification flags are recomputed from the iterations.</summary>
public sealed record InnerTrainingResult(
    string FinalChampionId,
    StrategyGenome FinalChampionGenome,
    IReadOnlyList<TrainingIteration> Iterations);

/// <summary>One meta sample: one episode under one proposer.</summary>
public sealed record MetaEpisode(
    string Role,
    int MetaSeedIndex,
    ulong EpisodeSeed,
    string FinalChampionId,
    StrategyGenome FinalChampionGenome,
    int VerifiedPromotions,
    int InnerIterations);

/// <summary>The outcome of one meta-iteration, including the persisted document when an evaluation ran.</summary>
public sealed record MetaIterationResult(
    int Iteration,
    MetaArtifact Incumbent,
    MetaArtifact Candidate,
    MetaDecision? Decision,
    MetaEvaluationDocument? Document,
    string Outcome,
    IReadOnlyList<MetaEpisode> CandidateEpisodes,
    IReadOnlyList<MetaEpisode> IncumbentEpisodes)
{
    public bool Eligible => Decision?.Eligible == true;
}

/// <summary>The finished meta-run. The active proposer pointer is changed only by explicit promotion.</summary>
public sealed record MetaRunResult(
    MetaRunOptions Options,
    IReadOnlyList<MetaIterationResult> Iterations,
    string PersistedActiveProposerId,
    string? EligibleProposerId,
    FrozenManifest FrozenManifest);

/// <summary>Produces the next proposer genome. The production implementation is deterministic and data-only.</summary>
public interface IMetaProposer
{
    ProposerGenome Propose(ProposerGenome incumbent, int metaIteration);
}

/// <summary>Runs one episode (K inner training iterations) under one proposer genome.</summary>
public interface IMetaEpisodeRunner
{
    InnerTrainingResult Run(
        ProposerGenome proposer,
        ulong episodeSeed,
        MetaRunOptions options,
        string scratchDirectory,
        CancellationToken cancellationToken);
}

/// <summary>Produces the paired primary and fresh comparison reports from two final champions.</summary>
public interface IMetaReportEvaluator
{
    EvaluationReport Compare(
        StrategyGenome candidate,
        StrategyGenome incumbent,
        string candidateId,
        string incumbentId,
        ulong baseSeed,
        int seeds,
        int handsPerMatch);
}

/// <summary>The production episode runner: the standard <see cref="TrainingLoop"/> in a scratch directory.</summary>
public sealed class TrainingLoopEpisodeRunner : IMetaEpisodeRunner
{
    public InnerTrainingResult Run(
        ProposerGenome proposer,
        ulong episodeSeed,
        MetaRunOptions options,
        string scratchDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(scratchDirectory);

        var effectiveSeeds = EffectiveEvaluationSeeds(options.EvaluationSeeds, proposer);
        var training = new TrainingOptions(
            episodeSeed,
            options.InnerIterations,
            options.TrainingHands,
            effectiveSeeds,
            options.EvaluationHandsPerMatch,
            options.Criteria,
            scratchDirectory,
            options.StartingStrategyId,
            HarnessRefinement: options.UseHarnessRefinement,
            SkillRefinement: false,
            RefinementStrategy: proposer.RefinementStrategy);

        TryDelete(scratchDirectory);
        try
        {
            var run = TrainingLoop.Run(
                training,
                policy: null,
                cancellationToken,
                proposer: new DeterministicStrategyProposer(proposer),
                observer: null,
                proposalGuard: ProposalGuards.RejectBehaviorallyInert);
            var final = TrainingLoop.ReadPersistedStrategy(scratchDirectory, run.FinalChampionId);
            return new InnerTrainingResult(final.Id, final.Genome, run.Iterations);
        }
        finally
        {
            TryDelete(scratchDirectory);
        }
    }

    /// <summary>
    /// The proposer spends only its budget share of the configured evaluation seeds, floored, with a
    /// minimum of two. The promotion gate still requires <see cref="EvaluationCriteria.MinSeeds"/>, so
    /// a small share is allowed but can never lower the gate.
    /// </summary>
    public static int EffectiveEvaluationSeeds(int configuredSeeds, ProposerGenome proposer)
    {
        ArgumentNullException.ThrowIfNull(proposer);
        if (configuredSeeds < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(configuredSeeds), configuredSeeds, "A meta episode needs at least two evaluation seeds.");
        }

        var shared = (int)Math.Floor(configuredSeeds * proposer.Clamp().EvaluationBudgetShare);
        return Math.Clamp(shared, 2, configuredSeeds);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>The production comparison evaluator: the frozen paired <see cref="MatchEvaluator"/>.</summary>
public sealed class MatchEvaluatorReportEvaluator : IMetaReportEvaluator
{
    public EvaluationReport Compare(
        StrategyGenome candidate,
        StrategyGenome incumbent,
        string candidateId,
        string incumbentId,
        ulong baseSeed,
        int seeds,
        int handsPerMatch) =>
        MatchEvaluator.Evaluate(
            new StrategyAgent(candidate, "candidate"),
            candidateId,
            new StrategyAgent(incumbent, "incumbent"),
            incumbentId,
            baseSeed,
            seeds,
            handsPerMatch);
}

/// <summary>Meta-loop progress phases.</summary>
public enum MetaPhase
{
    FrozenCheck,
    Propose,
    Episodes,
    Evaluate,
    Gate,
    Persist,
}

/// <summary>One observable meta-loop phase transition.</summary>
public sealed record MetaProgress(
    int Iteration,
    int TotalIterations,
    MetaPhase Phase,
    MetaIterationResult? Completed);

public interface IMetaObserver
{
    void OnProgress(MetaProgress progress);
}

/// <summary>Raised when a frozen component no longer matches the pinned manifest.</summary>
public sealed class FrozenManifestMismatchException : InvalidOperationException
{
    public FrozenManifestMismatchException(string reason, FrozenManifest captured)
        : base("The frozen manifest does not match the pinned manifest: " + reason)
    {
        Reason = reason;
        Captured = captured;
    }

    public string Reason { get; }

    public FrozenManifest Captured { get; }
}

/// <summary>Raised when a hard meta budget is exhausted. No pointer is ever written on this path.</summary>
public sealed class MetaBudgetExceededException : InvalidOperationException
{
    public MetaBudgetExceededException(string budget, string detail)
        : base($"The meta-run exhausted its {budget} budget: {detail}")
    {
        Budget = budget;
    }

    public string Budget { get; }
}

/// <summary>
/// The bounded mini-RSI loop. It evolves one <see cref="ProposerGenome"/> per meta-iteration, runs the
/// standard training loop in scratch directories, and decides eligibility from the frozen gate plus a
/// pre-registered hit-rate improvement and a fresh-holdout ratchet. It never writes
/// <c>active-champion.json</c> and never changes the proposer pointer automatically.
/// </summary>
public static class MetaLoop
{
    public static IReadOnlyList<string> Bounds { get; } =
    [
        "metaIterations 1..8",
        "innerIterations 1..5",
        "trainingHands 1..2000",
        "evaluationSeeds 2..50",
        "evaluationHands 1..20",
        "marginBbPer100 0..100",
        "minSeeds 2..1000",
        "metaSeeds 2..50",
        "metaHands 1..20",
        "freshSeeds 2..50",
        "freshHands 1..20",
        "requiredHitRateImprovement 0..1",
        "maxInnerIterations 1..400",
        "maxEvaluationsPerEpisode 1..200",
    ];

    /// <summary>Returns the offending option name, or null when the options are in bounds.</summary>
    public static string? Validate(MetaRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MetaIterations is < 1 or > 8) return nameof(options.MetaIterations);
        if (options.InnerIterations is < 1 or > 5) return nameof(options.InnerIterations);
        if (options.TrainingHands is < 1 or > 2000) return nameof(options.TrainingHands);
        if (options.EvaluationSeeds is < 2 or > 50) return nameof(options.EvaluationSeeds);
        if (options.EvaluationHandsPerMatch is < 1 or > 20) return nameof(options.EvaluationHandsPerMatch);
        if (options.Criteria.MarginBbPer100 is < 0.0 or > 100.0) return "marginBbPer100";
        if (options.Criteria.MinSeeds is < 2 or > 1000) return nameof(options.Criteria.MinSeeds);
        if (options.MetaSeeds is < 2 or > 50) return nameof(options.MetaSeeds);
        if (options.MetaHandsPerMatch is < 1 or > 20) return nameof(options.MetaHandsPerMatch);
        if (options.FreshSeeds is < 2 or > 50) return nameof(options.FreshSeeds);
        if (options.FreshHandsPerMatch is < 1 or > 20) return nameof(options.FreshHandsPerMatch);
        if (options.RequiredHitRateImprovement is < 0.0 or > 1.0) return nameof(options.RequiredHitRateImprovement);
        if (options.MaxInnerIterations is < 1 or > 400) return nameof(options.MaxInnerIterations);
        if (options.MaxEvaluationsPerEpisode is < 1 or > 200) return nameof(options.MaxEvaluationsPerEpisode);
        if (options.InnerIterations * options.EvaluationSeeds > options.MaxEvaluationsPerEpisode)
        {
            return nameof(options.MaxEvaluationsPerEpisode);
        }

        return null;
    }

    public static MetaRunResult Run(
        MetaRunOptions options,
        CancellationToken cancellationToken = default,
        IMetaObserver? observer = null,
        IMetaEpisodeRunner? episodeRunner = null,
        IMetaReportEvaluator? reportEvaluator = null,
        FrozenManifest? expectedManifest = null,
        IMetaProposer? metaProposer = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var offending = Validate(options);
        if (offending is not null)
        {
            throw new ArgumentOutOfRangeException(offending, "The meta-run option is outside its documented bound.");
        }

        var expected = expectedManifest ?? FrozenComponents.Pinned;
        var runner = episodeRunner ?? new TrainingLoopEpisodeRunner();
        var evaluator = reportEvaluator ?? new MatchEvaluatorReportEvaluator();
        var dataDirectory = options.DataDirectory ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "tholdem");
        var metaDirectory = MetaStore.MetaDirectory(dataDirectory);
        var proposersDirectory = Path.Combine(metaDirectory, MetaStore.ProposersDirectoryName);
        var evaluationsDirectory = Path.Combine(metaDirectory, MetaStore.EvaluationsDirectoryName);
        var scratchRoot = Path.Combine(metaDirectory, MetaStore.ScratchDirectoryName);
        Directory.CreateDirectory(metaDirectory);
        Directory.CreateDirectory(proposersDirectory);
        Directory.CreateDirectory(evaluationsDirectory);
        Directory.CreateDirectory(scratchRoot);

        var ledgerCores = MetaStore.LoadLedger(metaDirectory);
        var incumbent = MetaStore.LoadOrCreateActiveProposer(metaDirectory, proposersDirectory);
        var persistedActive = incumbent.Id;
        var results = new List<MetaIterationResult>();
        var cache = new Dictionary<string, InnerTrainingResult>(StringComparer.Ordinal);
        var proposer = metaProposer ?? new DeterministicMetaProposer();
        var budget = new MetaBudget(
            options.MaxInnerIterations,
            options.MaxWallClock ?? MetaRunOptions.DefaultMaxWallClock,
            Stopwatch.StartNew());
        string? eligibleId = null;

        var frozen = FrozenComponents.Capture();
        if (!FrozenComponents.Matches(frozen, expected, out var frozenReason))
        {
            throw new FrozenManifestMismatchException(frozenReason, frozen);
        }

        for (var iteration = 1; iteration <= options.MetaIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            budget.CheckTime();

            observer?.OnProgress(new MetaProgress(iteration, options.MetaIterations, MetaPhase.FrozenCheck, null));
            frozen = FrozenComponents.Capture();
            if (!FrozenComponents.Matches(frozen, expected, out frozenReason))
            {
                throw new FrozenManifestMismatchException(frozenReason, frozen);
            }

            observer?.OnProgress(new MetaProgress(iteration, options.MetaIterations, MetaPhase.Propose, null));
            var candidateGenome = proposer.Propose(incumbent.Proposer, iteration);
            var candidate = MetaArtifact.Create(candidateGenome, iteration, incumbent);
            MetaStore.WriteProposer(proposersDirectory, candidate);

            if (candidate.Proposer.BehaviorEquals(incumbent.Proposer))
            {
                var refused = new MetaIterationResult(
                    iteration,
                    incumbent,
                    candidate,
                    Decision: null,
                    Document: null,
                    MetaDecision.InertProposerReason,
                    [],
                    []);
                results.Add(refused);
                MetaStore.AppendLedgerEntry(metaDirectory, ledgerCores, iteration, incumbent.Id, candidate.Id, false, MetaDecision.InertProposerReason, null);
                observer?.OnProgress(new MetaProgress(iteration, options.MetaIterations, MetaPhase.Persist, refused));
                continue;
            }

            observer?.OnProgress(new MetaProgress(iteration, options.MetaIterations, MetaPhase.Episodes, null));
            var candidateEpisodes = new List<MetaEpisode>(options.MetaSeeds);
            var incumbentEpisodes = new List<MetaEpisode>(options.MetaSeeds);
            var candidateVerified = new int[options.MetaSeeds];
            var incumbentVerified = new int[options.MetaSeeds];
            var sameChampionEverywhere = true;
            for (var metaIndex = 0; metaIndex < options.MetaSeeds; metaIndex++)
            {
                _ = MetaSeedDerivation.EpisodeSeedSet(options.Seed, metaIndex, options.InnerIterations);
                var episodeSeed = MetaSeedDerivation.EpisodeSeed(options.Seed, metaIndex);
                var candidateEpisode = RunEpisode(
                    cache,
                    runner,
                    proposerClamp: candidate.Proposer,
                    artifact: candidate,
                    episodeSeed,
                    metaIndex,
                    "candidate",
                    options,
                    scratchRoot,
                    budget,
                    cancellationToken);
                var incumbentEpisode = RunEpisode(
                    cache,
                    runner,
                    proposerClamp: incumbent.Proposer,
                    artifact: incumbent,
                    episodeSeed,
                    metaIndex,
                    "incumbent",
                    options,
                    scratchRoot,
                    budget,
                    cancellationToken);

                candidateEpisodes.Add(candidateEpisode);
                incumbentEpisodes.Add(incumbentEpisode);
                candidateVerified[metaIndex] = candidateEpisode.VerifiedPromotions;
                incumbentVerified[metaIndex] = incumbentEpisode.VerifiedPromotions;
                if (!candidateEpisode.FinalChampionGenome.BehaviorEquals(incumbentEpisode.FinalChampionGenome))
                {
                    sameChampionEverywhere = false;
                }
            }

            if (sameChampionEverywhere)
            {
                // A5: comparing a champion with itself cannot establish anything.
                var refused = new MetaIterationResult(
                    iteration,
                    incumbent,
                    candidate,
                    Decision: null,
                    Document: null,
                    MetaDecision.SelfComparisonReason,
                    candidateEpisodes,
                    incumbentEpisodes);
                results.Add(refused);
                MetaStore.AppendLedgerEntry(metaDirectory, ledgerCores, iteration, incumbent.Id, candidate.Id, false, MetaDecision.SelfComparisonReason, null);
                observer?.OnProgress(new MetaProgress(iteration, options.MetaIterations, MetaPhase.Persist, refused));
                continue;
            }

            budget.CheckTime();
            observer?.OnProgress(new MetaProgress(iteration, options.MetaIterations, MetaPhase.Evaluate, null));
            var primary = BuildPrimaryReport(
                evaluator,
                candidate,
                incumbent,
                candidateEpisodes,
                incumbentEpisodes,
                options);
            var freshCandidate = RunEpisode(
                cache,
                runner,
                proposerClamp: candidate.Proposer,
                artifact: candidate,
                episodeSeed: MetaSeedDerivation.FreshEpisodeSeed(options.Seed),
                metaIndex: -1,
                role: "candidate-fresh",
                options,
                scratchRoot,
                budget,
                cancellationToken);
            var freshIncumbent = RunEpisode(
                cache,
                runner,
                proposerClamp: incumbent.Proposer,
                artifact: incumbent,
                episodeSeed: MetaSeedDerivation.FreshEpisodeSeed(options.Seed),
                metaIndex: -1,
                role: "incumbent-fresh",
                options,
                scratchRoot,
                budget,
                cancellationToken);
            var fresh = evaluator.Compare(
                freshCandidate.FinalChampionGenome,
                freshIncumbent.FinalChampionGenome,
                candidate.Id,
                incumbent.Id,
                MetaSeedDerivation.FreshComparisonSeed(options.Seed),
                options.FreshSeeds,
                options.FreshHandsPerMatch);

            observer?.OnProgress(new MetaProgress(iteration, options.MetaIterations, MetaPhase.Gate, null));
            var decision = BuildDecision(
                options,
                frozen,
                candidate,
                incumbent,
                primary,
                fresh,
                candidateVerified,
                incumbentVerified);
            var outcome = decision.Eligible
                ? MetaDecision.EligibleReason
                : "rejected:" + decision.Reason;
            var document = new MetaEvaluationDocument(
                candidate.Id,
                incumbent.Id,
                candidate.Proposer,
                incumbent.Proposer,
                options.MetaSeeds,
                options.InnerIterations,
                primary,
                fresh,
                candidateVerified,
                incumbentVerified,
                options.RequiredHitRateImprovement,
                options.Criteria,
                frozen.ManifestHash,
                decision,
                outcome);

            var verifierAgrees = MetaDecisionVerifier.Verify(document, frozen, out var verifyReason);
            if (verifierAgrees != decision.Eligible)
            {
                throw new InvalidOperationException(
                    $"The meta decision is not reproducible: decision={decision.Reason}, verifier={verifyReason}.");
            }

            MetaStore.WriteEvaluationDocument(dataDirectory, document);
            var result = new MetaIterationResult(
                iteration,
                incumbent,
                candidate,
                decision,
                document,
                outcome,
                candidateEpisodes,
                incumbentEpisodes);
            results.Add(result);
            MetaStore.AppendLedgerEntry(
                metaDirectory,
                ledgerCores,
                iteration,
                incumbent.Id,
                candidate.Id,
                decision.Eligible,
                outcome,
                document.DocumentHash);

            if (decision.Eligible)
            {
                eligibleId = candidate.Id;
                incumbent = candidate;
            }

            observer?.OnProgress(new MetaProgress(iteration, options.MetaIterations, MetaPhase.Persist, result));
        }

        return new MetaRunResult(options, results, persistedActive, eligibleId, frozen);
    }

    private static MetaEpisode RunEpisode(
        Dictionary<string, InnerTrainingResult> cache,
        IMetaEpisodeRunner runner,
        ProposerGenome proposerClamp,
        MetaArtifact artifact,
        ulong episodeSeed,
        int metaIndex,
        string role,
        MetaRunOptions options,
        string scratchRoot,
        MetaBudget budget,
        CancellationToken cancellationToken)
    {
        var effectiveSeeds = TrainingLoopEpisodeRunner.EffectiveEvaluationSeeds(options.EvaluationSeeds, proposerClamp);
        var perEpisodeEvaluations = options.InnerIterations * effectiveSeeds;
        if (perEpisodeEvaluations > options.MaxEvaluationsPerEpisode)
        {
            throw new MetaBudgetExceededException(
                "per-episode-evaluations",
                $"{perEpisodeEvaluations} > {options.MaxEvaluationsPerEpisode}");
        }

        var cacheKey = artifact.Id + "|" + metaIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!cache.TryGetValue(cacheKey, out var result))
        {
            budget.ConsumeEpisode();
            var scratch = Path.Combine(
                scratchRoot,
                "episode-" + metaIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + role);
            result = runner.Run(proposerClamp, episodeSeed, options, scratch, cancellationToken);
            cache[cacheKey] = result;
        }

        var verified = 0;
        foreach (var iteration in result.Iterations)
        {
            if (iteration.Verified && iteration.Outcome == TrainingLoop.PromotedOutcome)
            {
                verified++;
            }
        }

        return new MetaEpisode(
            role,
            metaIndex,
            episodeSeed,
            result.FinalChampionId,
            result.FinalChampionGenome,
            verified,
            result.Iterations.Count);
    }

    private static EvaluationReport BuildPrimaryReport(
        IMetaReportEvaluator evaluator,
        MetaArtifact candidate,
        MetaArtifact incumbent,
        IReadOnlyList<MetaEpisode> candidateEpisodes,
        IReadOnlyList<MetaEpisode> incumbentEpisodes,
        MetaRunOptions options)
    {
        var values = new double[candidateEpisodes.Count];
        var challengerWins = 0;
        var incumbentWins = 0;
        var ties = 0;
        long netChips = 0;
        for (var index = 0; index < candidateEpisodes.Count; index++)
        {
            var report = evaluator.Compare(
                candidateEpisodes[index].FinalChampionGenome,
                incumbentEpisodes[index].FinalChampionGenome,
                candidate.Id,
                incumbent.Id,
                MetaSeedDerivation.ComparisonSeed(options.Seed, index),
                seeds: 1,
                options.MetaHandsPerMatch);
            values[index] = report.MeanBbPer100;
            challengerWins += report.ChallengerWins;
            incumbentWins += report.IncumbentWins;
            ties += report.Ties;
            netChips += report.ChallengerNetChips;
        }

        var mean = MetaStatistics.Mean(values);
        var halfWidth = MetaStatistics.HalfWidth(values, mean, options.Criteria.Z);
        return new EvaluationReport(
            candidate.Id,
            incumbent.Id,
            values.Length,
            options.MetaHandsPerMatch * 2,
            10,
            mean,
            new ConfidenceInterval(mean - halfWidth, mean + halfWidth),
            mean - halfWidth,
            challengerWins,
            incumbentWins,
            ties,
            netChips);
    }

    private static MetaDecision BuildDecision(
        MetaRunOptions options,
        FrozenManifest frozen,
        MetaArtifact candidate,
        MetaArtifact incumbent,
        EvaluationReport primary,
        EvaluationReport fresh,
        IReadOnlyList<int> candidateVerified,
        IReadOnlyList<int> incumbentVerified)
    {
        var hitRate = HitRateCalculator.Compare(
            candidateVerified,
            incumbentVerified,
            options.InnerIterations,
            options.Criteria.Z);
        var policyDecision = new StatisticalPromotionPolicy().Decide(primary, options.Criteria);
        var primaryVerified = PromotionVerifier.Verify(policyDecision, primary, options.Criteria, out _);

        var reason = MetaDecision.EligibleReason;
        if (candidate.Id == incumbent.Id || candidate.Proposer.BehaviorEquals(incumbent.Proposer))
        {
            reason = MetaDecision.IdenticalProposerReason;
        }
        else if (options.MetaSeeds < options.Criteria.MinSeeds)
        {
            reason = MetaDecision.HitRateInsufficientSeedsReason;
        }
        else if (!policyDecision.Promoted)
        {
            reason = MetaDecision.PrimaryRejectedReason;
        }
        else if (!primaryVerified)
        {
            reason = MetaDecision.PrimaryUnverifiedReason;
        }
        else if (hitRate.LowerBoundImprovement <= options.RequiredHitRateImprovement)
        {
            reason = MetaDecision.HitRateBelowImprovementReason;
        }
        else if (fresh.LowerBoundBbPer100 < -options.Criteria.MarginBbPer100)
        {
            reason = MetaDecision.FreshRegressionReason;
        }

        return new MetaDecision(
            reason == MetaDecision.EligibleReason,
            reason,
            frozen.ManifestHash,
            hitRate,
            primary.LowerBoundBbPer100,
            options.Criteria.MarginBbPer100,
            primary.Seeds,
            primaryVerified,
            options.RequiredHitRateImprovement,
            fresh.LowerBoundBbPer100,
            -options.Criteria.MarginBbPer100);
    }

    private sealed class MetaBudget
    {
        private readonly int _maxEpisodes;
        private readonly TimeSpan _maxWallClock;
        private readonly Stopwatch _elapsed;
        private int _episodes;

        public MetaBudget(int maxEpisodes, TimeSpan maxWallClock, Stopwatch elapsed)
        {
            _maxEpisodes = maxEpisodes;
            _maxWallClock = maxWallClock;
            _elapsed = elapsed;
        }

        public void ConsumeEpisode()
        {
            _episodes++;
            if (_episodes > _maxEpisodes)
            {
                throw new MetaBudgetExceededException(
                    "inner-iterations",
                    $"{_episodes} > {_maxEpisodes}");
            }

            CheckTime();
        }

        public void CheckTime()
        {
            if (_elapsed.Elapsed > _maxWallClock)
            {
                throw new MetaBudgetExceededException(
                    "wall-clock",
                    _elapsed.Elapsed.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
                        + " > "
                        + _maxWallClock.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}

/// <summary>
/// The meta artifact store: proposer artifacts, evaluation documents, a hash-chained meta ledger and
/// the active-proposer pointer. The pointer is written by explicit promotion only; the loop creates
/// the default pointer once when the directory is fresh.
/// </summary>
public static class MetaStore
{
    public const string MetaDirectoryName = "meta";

    public const string ProposersDirectoryName = "proposers";

    public const string EvaluationsDirectoryName = "evaluations";

    public const string ScratchDirectoryName = "scratch";

    public const string LedgerFileName = "meta-ledger.json";

    public const string ActiveProposerFileName = "active-proposer.json";

    public const string PinnedManifestFileName = "frozen-manifest.json";

    public static string MetaDirectory(string dataDirectory) =>
        Path.Combine(dataDirectory, MetaDirectoryName);

    public static string EvaluationFileName(string candidateId, string incumbentId) =>
        candidateId + "-vs-" + incumbentId + ".json";

    public static string? TryReadEvaluationDocument(
        string dataDirectory,
        string candidateId,
        string incumbentId) =>
        TryReadText(
            Path.Combine(
                MetaDirectory(dataDirectory),
                EvaluationsDirectoryName,
                EvaluationFileName(candidateId, incumbentId)));

    public static void WriteEvaluationDocument(string dataDirectory, MetaEvaluationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var directory = Path.Combine(MetaDirectory(dataDirectory), EvaluationsDirectoryName);
        Directory.CreateDirectory(directory);
        WriteUtf8(
            Path.Combine(directory, EvaluationFileName(document.CandidateId, document.IncumbentId)),
            document.ToCanonicalJson());
    }

    public static MetaArtifact ReadActiveProposer(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        var metaDirectory = MetaDirectory(dataDirectory);
        var pointerPath = Path.Combine(metaDirectory, ActiveProposerFileName);
        if (!File.Exists(pointerPath))
        {
            throw new InvalidOperationException($"No active proposer pointer exists at '{pointerPath}'.");
        }

        string proposerId;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(pointerPath));
            proposerId = document.RootElement.GetProperty("proposerId").GetString()
                ?? throw new InvalidOperationException("The active proposer pointer has no proposerId.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The active proposer pointer '{pointerPath}' is not readable: {exception.Message}",
                exception);
        }

        var artifactPath = Path.Combine(metaDirectory, ProposersDirectoryName, proposerId + ".json");
        if (!File.Exists(artifactPath))
        {
            throw new InvalidOperationException(
                $"The active proposer pointer references '{proposerId}', but its artifact is missing.");
        }

        if (!MetaArtifact.TryParse(File.ReadAllText(artifactPath), out var artifact) || artifact is null)
        {
            throw new InvalidOperationException(
                $"The proposer artifact '{artifactPath}' does not match its content hash.");
        }

        return artifact;
    }

    public static void WriteActiveProposer(
        string dataDirectory,
        MetaArtifact artifact,
        string? documentHash)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var metaDirectory = MetaDirectory(dataDirectory);
        Directory.CreateDirectory(Path.Combine(metaDirectory, ProposersDirectoryName));
        WriteProposer(Path.Combine(metaDirectory, ProposersDirectoryName), artifact);

        var builder = new StringBuilder(192);
        builder.Append("{\"proposerId\":");
        CanonicalJsonWriter.Text(builder, artifact.Id);
        builder.Append(",\"iteration\":");
        CanonicalJsonWriter.Number(builder, artifact.CreatedAtIteration);
        builder.Append(",\"verified\":true");
        if (!string.IsNullOrEmpty(documentHash))
        {
            builder.Append(",\"documentHash\":");
            CanonicalJsonWriter.Text(builder, documentHash);
        }

        builder.Append('}');
        WriteUtf8(Path.Combine(metaDirectory, ActiveProposerFileName), builder.ToString());
    }

    public static void WritePinnedManifest(string dataDirectory, FrozenManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var metaDirectory = MetaDirectory(dataDirectory);
        Directory.CreateDirectory(metaDirectory);
        WriteUtf8(Path.Combine(metaDirectory, PinnedManifestFileName), manifest.ToCanonicalJson());
    }

    public static LedgerIntegrity VerifyLedger(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        var path = Path.Combine(MetaDirectory(dataDirectory), LedgerFileName);
        return File.Exists(path)
            ? LedgerChain.Verify(File.ReadAllText(path))
            : LedgerChain.Verify(null);
    }

    internal static void WriteProposer(string proposersDirectory, MetaArtifact artifact)
    {
        Directory.CreateDirectory(proposersDirectory);
        WriteUtf8(Path.Combine(proposersDirectory, artifact.Id + ".json"), artifact.ToCanonicalJson());
    }

    internal static MetaArtifact LoadOrCreateActiveProposer(
        string metaDirectory,
        string proposersDirectory)
    {
        var pointerPath = Path.Combine(metaDirectory, ActiveProposerFileName);
        if (File.Exists(pointerPath))
        {
            return ReadActiveProposer(Path.GetDirectoryName(metaDirectory) ?? metaDirectory);
        }

        var fresh = MetaArtifact.Create(ProposerGenome.Default, 0, null);
        WriteProposer(proposersDirectory, fresh);
        var builder = new StringBuilder(160);
        builder.Append("{\"proposerId\":");
        CanonicalJsonWriter.Text(builder, fresh.Id);
        builder.Append(",\"iteration\":0,\"verified\":false}");
        WriteUtf8(pointerPath, builder.ToString());
        return fresh;
    }

    internal static List<string> LoadLedger(string metaDirectory)
    {
        var path = Path.Combine(metaDirectory, LedgerFileName);
        var cores = new List<string>();
        if (!File.Exists(path))
        {
            return cores;
        }

        var text = File.ReadAllText(path);
        var integrity = LedgerChain.Verify(text);
        if (!integrity.Verified)
        {
            throw new InvalidOperationException(
                $"The meta ledger '{path}' failed its hash-chain integrity check: {integrity.Reason}.");
        }

        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"The meta ledger '{path}' is not a JSON array.");
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (!LedgerChain.TrySplit(element.GetRawText(), out var core, out _, out _))
            {
                throw new InvalidOperationException(
                    $"The meta ledger '{path}' contains an entry with partial chain fields.");
            }

            cores.Add(core);
        }

        return cores;
    }

    internal static void AppendLedgerEntry(
        string metaDirectory,
        List<string> cores,
        int iteration,
        string incumbentId,
        string candidateId,
        bool eligible,
        string outcome,
        string? documentHash)
    {
        var builder = new StringBuilder(256);
        builder.Append("{\"iteration\":");
        CanonicalJsonWriter.Number(builder, iteration);
        builder.Append(",\"incumbentId\":");
        CanonicalJsonWriter.Text(builder, incumbentId);
        builder.Append(",\"candidateId\":");
        CanonicalJsonWriter.Text(builder, candidateId);
        builder.Append(",\"eligible\":");
        CanonicalJsonWriter.Boolean(builder, eligible);
        builder.Append(",\"outcome\":");
        CanonicalJsonWriter.Text(builder, outcome);
        builder.Append(",\"documentHash\":");
        if (string.IsNullOrEmpty(documentHash))
        {
            builder.Append("null");
        }
        else
        {
            CanonicalJsonWriter.Text(builder, documentHash);
        }

        builder.Append('}');
        cores.Add(builder.ToString());
        WriteUtf8(Path.Combine(metaDirectory, LedgerFileName), LedgerChain.BuildChainedArray(cores));
    }

    private static string? TryReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteUtf8(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
