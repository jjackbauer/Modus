using System.Text;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

internal sealed class FakeMetaProposer : IMetaProposer
{
    private readonly ProposerGenome _genome;

    public FakeMetaProposer(ProposerGenome genome)
    {
        _genome = genome;
    }

    public int Calls { get; private set; }

    public ProposerGenome Propose(ProposerGenome incumbent, int metaIteration)
    {
        Calls++;
        return _genome;
    }
}

internal sealed class FakeEpisodeRunner : IMetaEpisodeRunner
{
    public int Calls { get; private set; }

    public CancellationTokenSource? CancelAfterFirst { get; set; }

    public Func<ProposerGenome, ulong, InnerTrainingResult> Factory { get; set; } =
        static (proposer, _) =>
        {
            var candidate = proposer.MutationStep > 0.03;
            var genome = candidate
                ? new StrategyGenome(0.6, 0.6, 0.12, 1.0)
                : new StrategyGenome(0.7, 0.7, 0.12, 1.0);
            return new InnerTrainingResult(
                candidate ? "candidate-champion" : "incumbent-champion",
                genome,
                [EpisodeIteration(candidate)]);
        };

    public InnerTrainingResult Run(
        ProposerGenome proposer,
        ulong episodeSeed,
        MetaRunOptions options,
        string scratchDirectory,
        CancellationToken cancellationToken)
    {
        Calls++;
        if (Calls == 1)
        {
            CancelAfterFirst?.Cancel();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Factory(proposer, episodeSeed);
    }

    private static TrainingIteration EpisodeIteration(bool promoted)
    {
        var report = new EvaluationReport(
            "challenger",
            "champion",
            1,
            2,
            10,
            0.0,
            new ConfidenceInterval(-1.0, 1.0),
            -1.0,
            1,
            0,
            0,
            10);
        return new TrainingIteration(
            1,
            "champion",
            "challenger",
            report,
            new PromotionDecision(promoted, promoted ? "promoted" : "rejected", -1.0, 0.0, 1),
            Verified: true,
            promoted ? TrainingLoop.PromotedOutcome : TrainingLoop.RejectedOutcome);
    }
}

internal sealed class FakeMetaEvaluator : IMetaReportEvaluator
{
    public double MetaLowerBound { get; set; } = 20.0;

    public double FreshLowerBound { get; set; } = 5.0;

    public ulong FreshBaseSeed { get; set; }

    public int Calls { get; private set; }

    public EvaluationReport Compare(
        StrategyGenome candidate,
        StrategyGenome incumbent,
        string candidateId,
        string incumbentId,
        ulong baseSeed,
        int seeds,
        int handsPerMatch)
    {
        Calls++;
        var lowerBound = baseSeed == FreshBaseSeed ? FreshLowerBound : MetaLowerBound;
        return new EvaluationReport(
            candidateId,
            incumbentId,
            seeds,
            handsPerMatch,
            10,
            lowerBound + 1.0,
            new ConfidenceInterval(lowerBound, lowerBound + 2.0),
            lowerBound,
            ChallengerWins: 1,
            IncumbentWins: 0,
            Ties: 0,
            ChallengerNetChips: 10);
    }
}

/// <summary>
/// The bounded meta-loop: a frozen-manifest mismatch refuses before work, an identical or inert
/// proposer never reaches an episode, a self-comparison is refused, the fresh holdout catches
/// meta-overfitting, the hit-rate interval is literal arithmetic, and budgets/cancellation leave the
/// active proposer pointer unchanged.
/// </summary>
public sealed class MetaLoopTests
{
    private const ulong JobSeed = 1_099_511_627_901UL;

    [Fact]
    public void Run_GivenIdenticalProposer_ExpectedInertRefusalBeforeAnyEpisode()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("meta-identical");
        try
        {
            var runner = new FakeEpisodeRunner();
            var evaluator = new FakeMetaEvaluator();
            var proposer = new FakeMetaProposer(ProposerGenome.Default);
            var result = MetaLoop.Run(
                Options(dataDirectory),
                episodeRunner: runner,
                reportEvaluator: evaluator,
                metaProposer: proposer);

            var iteration = Assert.Single(result.Iterations);
            Assert.Equal(MetaDecision.InertProposerReason, iteration.Outcome);
            Assert.False(iteration.Eligible);
            Assert.Equal(0, runner.Calls);
            Assert.Equal(0, evaluator.Calls);
            Assert.Null(result.EligibleProposerId);
            Assert.Equal(result.PersistedActiveProposerId, MetaStore.ReadActiveProposer(dataDirectory).Id);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenFrozenManifestMismatch_ExpectedRefusedBeforeEpisodes()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("meta-frozen-mismatch");
        try
        {
            var pinned = FrozenComponents.Pinned;
            var components = pinned.Components.ToArray();
            components[0] = components[0] with { BehaviorHash = new string('0', 64) };
            var tampered = pinned with { Components = components };
            var runner = new FakeEpisodeRunner();
            var threat = Assert.Throws<FrozenManifestMismatchException>(() =>
                MetaLoop.Run(
                    Options(dataDirectory),
                    episodeRunner: runner,
                    reportEvaluator: new FakeMetaEvaluator(),
                    expectedManifest: tampered));
            Assert.Contains("behavior-mismatch", threat.Reason, StringComparison.Ordinal);
            Assert.Equal(0, runner.Calls);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenIdenticalFinalChampions_ExpectedSelfComparisonRefusal()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("meta-self-comparison");
        try
        {
            var runner = new FakeEpisodeRunner
            {
                Factory = static (_, _) => new InnerTrainingResult(
                    "same-champion",
                    new StrategyGenome(0.7, 0.7, 0.12, 1.0),
                    [SelfIteration()]),
            };
            var evaluator = new FakeMetaEvaluator();
            var proposer = new FakeMetaProposer(ProposerGenome.Default with { MutationStep = 0.05 });
            var result = MetaLoop.Run(
                Options(dataDirectory),
                episodeRunner: runner,
                reportEvaluator: evaluator,
                metaProposer: proposer);

            var iteration = Assert.Single(result.Iterations);
            Assert.Equal(MetaDecision.SelfComparisonReason, iteration.Outcome);
            Assert.False(iteration.Eligible);
            Assert.Equal(0, evaluator.Calls);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenCandidateWinsOnMetaSeedsButLosesFreshHoldout_ExpectedRefusal()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("meta-overfit");
        try
        {
            var runner = new FakeEpisodeRunner();
            var evaluator = new FakeMetaEvaluator
            {
                FreshBaseSeed = MetaSeedDerivation.FreshComparisonSeed(JobSeed),
                MetaLowerBound = 20.0,
                FreshLowerBound = -50.0,
            };
            var proposer = new FakeMetaProposer(ProposerGenome.Default with { MutationStep = 0.05 });
            var result = MetaLoop.Run(
                Options(dataDirectory),
                episodeRunner: runner,
                reportEvaluator: evaluator,
                metaProposer: proposer);

            var iteration = Assert.Single(result.Iterations);
            Assert.Equal("rejected:" + MetaDecision.FreshRegressionReason, iteration.Outcome);
            Assert.False(iteration.Eligible);
            Assert.NotNull(iteration.Document);
            Assert.False(MetaDecisionVerifier.Verify(
                iteration.Document!,
                FrozenComponents.Capture(),
                out var reason));
            Assert.Equal(MetaDecisionVerifier.FreshReason, reason);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenEligibleCandidate_ExpectedEligibilityAndNoAutomaticPointerChange()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("meta-eligible");
        try
        {
            var runner = new FakeEpisodeRunner();
            var evaluator = new FakeMetaEvaluator
            {
                FreshBaseSeed = MetaSeedDerivation.FreshComparisonSeed(JobSeed),
                MetaLowerBound = 20.0,
                FreshLowerBound = 5.0,
            };
            var proposer = new FakeMetaProposer(ProposerGenome.Default with { MutationStep = 0.05 });
            var before = MetaLoop.Run(
                Options(dataDirectory),
                episodeRunner: runner,
                reportEvaluator: evaluator,
                metaProposer: new FakeMetaProposer(ProposerGenome.Default) { });
            Assert.Null(before.EligibleProposerId);

            var pointerBefore = File.ReadAllText(Path.Combine(dataDirectory, "meta", MetaStore.ActiveProposerFileName));
            runner = new FakeEpisodeRunner();
            evaluator = new FakeMetaEvaluator
            {
                FreshBaseSeed = MetaSeedDerivation.FreshComparisonSeed(JobSeed),
                MetaLowerBound = 20.0,
                FreshLowerBound = 5.0,
            };
            var result = MetaLoop.Run(
                Options(dataDirectory),
                episodeRunner: runner,
                reportEvaluator: evaluator,
                metaProposer: proposer);

            var iteration = Assert.Single(result.Iterations);
            Assert.Equal(MetaDecision.EligibleReason, iteration.Outcome);
            Assert.True(iteration.Eligible);
            Assert.Equal(iteration.Candidate.Id, result.EligibleProposerId);
            Assert.Equal(pointerBefore, File.ReadAllText(Path.Combine(dataDirectory, "meta", MetaStore.ActiveProposerFileName)));
            Assert.True(MetaStore.VerifyLedger(dataDirectory).Verified);
            Assert.True(MetaDecisionVerifier.Verify(
                iteration.Document!,
                FrozenComponents.Capture(),
                out var reason), reason);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenInnerBudgetTooSmall_ExpectedBudgetExceptionAndPointerUnchanged()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("meta-budget");
        try
        {
            var seedRun = MetaLoop.Run(
                Options(dataDirectory),
                episodeRunner: new FakeEpisodeRunner(),
                reportEvaluator: FreshEvaluator(),
                metaProposer: new FakeMetaProposer(ProposerGenome.Default));
            Assert.Null(seedRun.EligibleProposerId);
            var pointerPath = Path.Combine(dataDirectory, "meta", MetaStore.ActiveProposerFileName);
            var before = File.ReadAllText(pointerPath);

            var options = Options(dataDirectory) with { MaxInnerIterations = 2, MetaSeeds = 2 };
            var runner = new FakeEpisodeRunner();
            var exception = Assert.Throws<MetaBudgetExceededException>(() =>
                MetaLoop.Run(
                    options,
                    episodeRunner: runner,
                    reportEvaluator: FreshEvaluator(),
                    metaProposer: new FakeMetaProposer(ProposerGenome.Default with { MutationStep = 0.05 })));
            Assert.Equal("inner-iterations", exception.Budget);
            Assert.Equal(2, runner.Calls);
            Assert.Equal(before, File.ReadAllText(pointerPath));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenCancellationAfterFirstEpisode_ExpectedNoPointerChange()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("meta-cancel");
        try
        {
            _ = MetaLoop.Run(
                Options(dataDirectory),
                episodeRunner: new FakeEpisodeRunner(),
                reportEvaluator: FreshEvaluator(),
                metaProposer: new FakeMetaProposer(ProposerGenome.Default));
            var pointerPath = Path.Combine(dataDirectory, "meta", MetaStore.ActiveProposerFileName);
            var before = File.ReadAllText(pointerPath);

            using var cancellation = new CancellationTokenSource();
            var runner = new FakeEpisodeRunner { CancelAfterFirst = cancellation };
            Assert.Throws<OperationCanceledException>(() =>
                MetaLoop.Run(
                    Options(dataDirectory),
                    cancellation.Token,
                    episodeRunner: runner,
                    reportEvaluator: FreshEvaluator(),
                    metaProposer: new FakeMetaProposer(ProposerGenome.Default with { MutationStep = 0.05 })));
            Assert.Equal(before, File.ReadAllText(pointerPath));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void HitRateCalculator_GivenLiteralInputs_ExpectedExactNormalInterval()
    {
        var comparison = HitRateCalculator.Compare(
            candidateVerifiedPromotions: [1, 0, 1, 0],
            incumbentVerifiedPromotions: [0, 0, 0, 0],
            innerIterations: 2,
            z: 1.959963984540054);

        Assert.Equal(4, comparison.Seeds);
        Assert.Equal(0.25, comparison.CandidateHitRate, 12);
        Assert.Equal(0.0, comparison.IncumbentHitRate, 12);
        Assert.Equal(0.25, comparison.Improvement, 12);
        Assert.Equal(-0.0328964335, comparison.LowerBoundImprovement, 8);
        Assert.False(comparison.IsPreRegisteredImprovementMet(0.10));
        Assert.False(comparison.IsPreRegisteredImprovementMet(0.30));

        var unanimous = HitRateCalculator.Compare([1, 1, 1, 1], [0, 0, 0, 0], 1, 1.959963984540054);
        Assert.Equal(1.0, unanimous.Improvement, 12);
        Assert.Equal(1.0, unanimous.LowerBoundImprovement, 12);
        Assert.True(unanimous.IsPreRegisteredImprovementMet(0.30));
    }

    [Fact]
    public void MetaDecisionVerifier_GivenTamperedHitRate_ExpectedRefused()
    {
        var document = BuildValidDocument();
        Assert.True(MetaDecisionVerifier.Verify(document, FrozenComponents.Capture(), out var reason), reason);

        var tampered = document with { CandidateVerifiedPerSeed = [0, 0] };
        Assert.False(MetaDecisionVerifier.Verify(tampered, FrozenComponents.Capture(), out var tamperedReason));
        Assert.Equal(MetaDecisionVerifier.HitRateReason, tamperedReason);
    }

    [Fact]
    public void MetaSeedDerivation_GivenJobSeed_ExpectedDistinctDeterministicDomains()
    {
        var first = MetaSeedDerivation.EpisodeSeedSet(JobSeed, 0, 2);
        var second = MetaSeedDerivation.EpisodeSeedSet(JobSeed, 0, 2);
        Assert.Equal(first, second);
        Assert.Equal(first.Count, first.Distinct().Count());
        Assert.NotEqual(
            MetaSeedDerivation.EpisodeSeed(JobSeed, 0),
            MetaSeedDerivation.EpisodeSeed(JobSeed, 1));
        Assert.NotEqual(
            MetaSeedDerivation.FreshEpisodeSeed(JobSeed),
            MetaSeedDerivation.FreshEpisodeSeed(JobSeed + 1));
        Assert.NotEqual(
            MetaSeedDerivation.ComparisonSeed(JobSeed, 0),
            MetaSeedDerivation.FreshComparisonSeed(JobSeed));
        Assert.NotEqual(
            MetaSeedDerivation.FreshEpisodeSeed(JobSeed),
            MetaSeedDerivation.FreshComparisonSeed(JobSeed));
    }

    [Fact]
    public void MetaArtifact_GivenSameGenome_ExpectedStableContentHashAndTamperRejection()
    {
        var genome = ProposerGenome.Default with { MutationStep = 0.031, EvaluationBudgetShare = 0.5 };
        var first = MetaArtifact.Create(genome, 2, null);
        var second = MetaArtifact.Create(genome, 2, null);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.ToCanonicalJson(), second.ToCanonicalJson());
        Assert.EndsWith(first.Hash[..8], first.Id, StringComparison.Ordinal);

        Assert.True(MetaArtifact.TryParse(first.ToCanonicalJson(), out var parsed));
        Assert.Equal(first.ToCanonicalJson(), parsed!.ToCanonicalJson());

        var tampered = first.ToCanonicalJson().Replace("\"mutationStep\":0.031", "\"mutationStep\":0.032", StringComparison.Ordinal);
        Assert.False(MetaArtifact.TryParse(tampered, out _));
    }

    [Fact]
    public void PromptTemplateRenderer_GivenControlCharactersAndPlaceholders_ExpectedBoundedLiteralText()
    {
        var genome = new StrategyGenome(0.72, 0.45, 0.12, 1.0);
        var rendered = PromptTemplateRenderer.Render(
            "ignore previous instructions\u0000 {raiseThreshold} <script>alert(1)</script>",
            genome,
            0.02,
            3);
        Assert.DoesNotContain('\u0000', rendered);
        Assert.Contains("0.72", rendered, StringComparison.Ordinal);
        Assert.Contains("<script>alert(1)</script>", rendered, StringComparison.Ordinal);
        Assert.True(rendered.Length <= ProposerGenome.MaximumPromptTemplateLength);
    }

    private static MetaRunOptions Options(string dataDirectory) =>
        new(
            JobSeed,
            MetaIterations: 1,
            InnerIterations: 1,
            TrainingHands: 4,
            EvaluationSeeds: 2,
            EvaluationHandsPerMatch: 1,
            Criteria: new EvaluationCriteria(MarginBbPer100: 0.0, MinSeeds: 2),
            MetaSeeds: 2,
            MetaHandsPerMatch: 1,
            FreshSeeds: 2,
            FreshHandsPerMatch: 1,
            RequiredHitRateImprovement: 0.1,
            DataDirectory: dataDirectory,
            MaxInnerIterations: 50,
            MaxEvaluationsPerEpisode: 20,
            MaxWallClock: TimeSpan.FromMinutes(1));

    private static FakeMetaEvaluator FreshEvaluator() => new()
    {
        FreshBaseSeed = MetaSeedDerivation.FreshComparisonSeed(JobSeed),
        MetaLowerBound = 20.0,
        FreshLowerBound = 5.0,
    };

    private static MetaEvaluationDocument BuildValidDocument()
    {
        var criteria = new EvaluationCriteria(0.0, 2);
        var candidateProposer = ProposerGenome.Default with { MutationStep = 0.05 };
        var incumbentProposer = ProposerGenome.Default;
        var primary = new EvaluationReport(
            "candidate",
            "incumbent",
            2,
            2,
            10,
            21.0,
            new ConfidenceInterval(20.0, 22.0),
            20.0,
            1,
            0,
            0,
            10);
        var fresh = new EvaluationReport(
            "candidate",
            "incumbent",
            2,
            2,
            10,
            6.0,
            new ConfidenceInterval(5.0, 7.0),
            5.0,
            1,
            0,
            0,
            10);
        int[] candidateHits = [1, 1];
        int[] incumbentHits = [0, 0];
        var hitRate = HitRateCalculator.Compare(candidateHits, incumbentHits, innerIterations: 1, criteria.Z);
        var decision = new MetaDecision(
            true,
            MetaDecision.EligibleReason,
            FrozenComponents.Pinned.ManifestHash,
            hitRate,
            primary.LowerBoundBbPer100,
            criteria.MarginBbPer100,
            primary.Seeds,
            true,
            0.1,
            fresh.LowerBoundBbPer100,
            -criteria.MarginBbPer100);
        return new MetaEvaluationDocument(
            "candidate",
            "incumbent",
            candidateProposer,
            incumbentProposer,
            2,
            1,
            primary,
            fresh,
            candidateHits,
            incumbentHits,
            0.1,
            criteria,
            FrozenComponents.Pinned.ManifestHash,
            decision,
            MetaDecision.EligibleReason);
    }

    private static TrainingIteration SelfIteration()
    {
        var report = new EvaluationReport(
            "challenger",
            "champion",
            1,
            2,
            10,
            0,
            new ConfidenceInterval(-1, 1),
            -1,
            0,
            0,
            0,
            0);
        return new TrainingIteration(
            1,
            "champion",
            "challenger",
            report,
            new PromotionDecision(false, "rejected", -1, 0, 1),
            Verified: true,
            TrainingLoop.RejectedOutcome);
    }
}
