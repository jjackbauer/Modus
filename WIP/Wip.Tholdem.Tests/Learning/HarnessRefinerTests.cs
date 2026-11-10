using System.Text;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// R-12 refiner-policy evolution: the default genome keeps its recorded meta-artifact bytes, the
/// optional refinement strategy serializes only when it is not the default, the policy selection
/// delegates or edits as documented, and the meta-loop runs its unchanged frozen gate, hit-rate
/// criterion and fresh-holdout ratchet with the opt-in refinement flag.
/// </summary>
public sealed class HarnessRefinerTests
{
    /// <summary>The recorded pre-change default meta artifact (probe: .tmp/goal-refinement/round8-meta-baseline.txt).</summary>
    private const string DefaultMetaArtifactJson = """{"id":"tholdem-proposer-v1-24c2109a","version":1,"parentId":null,"createdAtIteration":0,"proposerGenome":{"mutationStep":0.02,"parameterSelectionPolicy":"sessionDriven","evaluationBudgetShare":1,"promptTemplate":"Play tight-aggressive heads-up: raise strong hands, fold weak hands to large bets, and size value bets by the sizing multiplier."}}""";

    private const string DefaultMetaArtifactHash = "24c2109afefc9d9abe3c9f932639663193323e7594151ab3ce05fe98aa18b8b7";

    private const string PinnedFrozenHash = "round8-frozen-baseline";

    private const ulong JobSeed = 1_099_511_627_901UL;

    private const ulong TrainingSeed = 20_261_109UL;

    [Fact]
    public void Genome_GivenDefaultStrategy_ExpectedMetaArtifactBytesUnchanged()
    {
        var artifact = MetaArtifact.Create(ProposerGenome.Default, 0, null);

        Assert.Equal("tholdem-proposer-v1-24c2109a", artifact.Id);
        Assert.Equal(DefaultMetaArtifactHash, artifact.Hash);
        Assert.Equal(DefaultMetaArtifactJson, artifact.ToCanonicalJson());
        Assert.Equal(344, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetByteCount(artifact.ToCanonicalJson()));
        Assert.DoesNotContain("\"refinementStrategy\"", artifact.ToCanonicalJson(), StringComparison.Ordinal);
        Assert.Equal(RefinementStrategy.SessionDriven, ProposerGenome.Default.RefinementStrategy);
        Assert.Equal(RefinementStrategy.SessionDriven, new ProposerGenome().RefinementStrategy);

        Assert.True(MetaArtifact.TryParse(DefaultMetaArtifactJson, out var parsed));
        Assert.Equal(ProposerGenome.Default, parsed!.Proposer);
        Assert.Equal(RefinementStrategy.SessionDriven, parsed.Proposer.RefinementStrategy);
        Assert.Equal(artifact.ToCanonicalJson(), parsed.ToCanonicalJson());
    }

    [Fact]
    public void Genome_GivenSignatureDrivenStrategy_ExpectedBehaviorInequalityAndEmittedField()
    {
        var signature = ProposerGenome.Default with { RefinementStrategy = RefinementStrategy.SignatureDriven };

        Assert.False(ProposerGenome.Default.BehaviorEquals(signature));
        Assert.False(signature.BehaviorEquals(ProposerGenome.Default));
        Assert.Equal(RefinementStrategy.SignatureDriven, signature.Clamp().RefinementStrategy);

        var artifact = MetaArtifact.Create(signature, 0, null);
        var json = artifact.ToCanonicalJson();
        Assert.Contains("\"refinementStrategy\":\"signature-driven\"", json, StringComparison.Ordinal);
        Assert.NotEqual(MetaArtifact.Create(ProposerGenome.Default, 0, null).Hash, artifact.Hash);
        Assert.NotEqual(MetaArtifact.Create(ProposerGenome.Default, 0, null).Id, artifact.Id);

        Assert.True(MetaArtifact.TryParse(json, out var parsed));
        Assert.Equal(RefinementStrategy.SignatureDriven, parsed!.Proposer.RefinementStrategy);
        Assert.Equal(json, parsed.ToCanonicalJson());

        var unknown = json.Replace("signature-driven", "memory-driven", StringComparison.Ordinal);
        Assert.False(MetaArtifact.TryParse(unknown, out _));
    }

    [Fact]
    public void Document_GivenDefaultStrategy_ExpectedCanonicalBytesAndHashUnchanged()
    {
        var document = BuildStrategyDocument(ProposerGenome.Default with { MutationStep = 0.05 }, ProposerGenome.Default);

        Assert.Equal("2f2888d5fcd33c52057c8994b1a5656f8cc441bd5a8d43dcc010f5e241aa3bdd", document.DocumentHash);
        Assert.Equal(1545, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetByteCount(document.ToCanonicalJson()));
        Assert.DoesNotContain("\"refinementStrategy\"", document.ToCanonicalJson(), StringComparison.Ordinal);

        Assert.True(MetaEvaluationJson.TryParse(document.ToCanonicalJson(), out var parsed, out var reason), reason);
        Assert.Equal(document.DocumentHash, parsed!.DocumentHash);
        Assert.Equal(RefinementStrategy.SessionDriven, parsed.CandidateProposer.RefinementStrategy);
        Assert.Equal(RefinementStrategy.SessionDriven, parsed.IncumbentProposer.RefinementStrategy);
    }

    [Fact]
    public void Document_GivenSignatureDrivenStrategy_ExpectedOptionalEmissionAndStableRejection()
    {
        var document = BuildStrategyDocument(
            ProposerGenome.Default with { MutationStep = 0.05, RefinementStrategy = RefinementStrategy.SignatureDriven },
            ProposerGenome.Default);

        var json = document.ToCanonicalJson();
        Assert.Contains("\"refinementStrategy\":\"signature-driven\"", json, StringComparison.Ordinal);
        Assert.NotEqual("2f2888d5fcd33c52057c8994b1a5656f8cc441bd5a8d43dcc010f5e241aa3bdd", document.DocumentHash);

        Assert.True(MetaEvaluationJson.TryParse(json, out var parsed, out var reason), reason);
        Assert.Equal(RefinementStrategy.SignatureDriven, parsed!.CandidateProposer.RefinementStrategy);
        Assert.Equal(document.DocumentHash, parsed.DocumentHash);

        var unknown = json.Replace("signature-driven", "memory-driven", StringComparison.Ordinal);
        Assert.False(MetaEvaluationJson.TryParse(unknown, out _, out var unknownReason));
        Assert.Equal(MetaEvaluationJson.ShapeReason, unknownReason);
    }

    [Fact]
    public void Policy_GivenSessionDrivenStrategy_ExpectedLegacyProposal()
    {
        var incumbent = new StrategyGenome();
        var analysis = new SessionAnalysis(
            Hands: 120,
            WinRate: 0.58,
            BbPer100: 14.5,
            AggressionFactor: 1.8,
            ShowdownRate: 0.3,
            Vpip: 0.0);
        var signatures = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.FoldToRaise, 10, 10, 1.0, FailureSignalDirections.High),
        ]);
        var legacyProposer = new DeterministicStrategyProposer(ProposerGenome.Default with { MutationStep = 0.08 });
        var context = new RefinementContext(incumbent, analysis, signatures, Iteration: 1);

        var expected = legacyProposer.Propose(incumbent, analysis, iteration: 1);
        var proposal = new SignatureDrivenRefinementPolicy(legacyProposer, strategy: RefinementStrategy.SessionDriven)
            .Propose(context);

        Assert.Equal(expected, proposal.Genome);
        Assert.Equal(RefinementRationaleCodes.LegacySessionDriven, proposal.RationaleCode);
        Assert.Equal(incumbent.CallThreshold, expected.CallThreshold, 12);
        Assert.Equal(incumbent.SizingMultiplier + 0.08, expected.SizingMultiplier, 12);
        Assert.NotEqual(expected, new SignatureDrivenRefinementPolicy(legacyProposer).Propose(context).Genome);
    }

    [Fact]
    public void Policy_GivenSignatureDrivenStrategy_ExpectedSignatureEdit()
    {
        var incumbent = new StrategyGenome();
        var analysis = new SessionAnalysis(
            Hands: 120,
            WinRate: 0.42,
            BbPer100: -3.5,
            AggressionFactor: 1.2,
            ShowdownRate: 0.4,
            Vpip: 0.0);
        var signatures = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.FoldToRaise, 10, 10, 1.0, FailureSignalDirections.High),
        ]);
        var explicitSignature = new SignatureDrivenRefinementPolicy(
                legacyProposer: null,
                enableSkillEdits: false,
                strategy: RefinementStrategy.SignatureDriven)
            .Propose(new RefinementContext(incumbent, analysis, signatures, Iteration: 1));

        Assert.Equal(incumbent.CallThreshold - SignatureDrivenRefinementPolicy.Step, explicitSignature.Genome.CallThreshold, 12);
        Assert.Equal(incumbent.RaiseThreshold, explicitSignature.Genome.RaiseThreshold, 12);
        Assert.Equal(incumbent.BluffFrequency, explicitSignature.Genome.BluffFrequency, 12);
        Assert.Equal(incumbent.SizingMultiplier, explicitSignature.Genome.SizingMultiplier, 12);
        Assert.Equal(RefinementRationaleCodes.FoldToRaise, explicitSignature.RationaleCode);

        var legacyOnEmpty = new DeterministicStrategyProposer().Propose(incumbent, analysis, iteration: 1);
        var emptyContext = new SignatureDrivenRefinementPolicy(strategy: RefinementStrategy.SignatureDriven)
            .Propose(new RefinementContext(incumbent, analysis, SessionSignatures.Empty, Iteration: 1));
        Assert.Equal(legacyOnEmpty, emptyContext.Genome);
        Assert.Equal(RefinementRationaleCodes.LegacySessionDriven, emptyContext.RationaleCode);
    }

    [Fact]
    public void MetaProposer_GivenDefaultIncumbent_ExpectedBoundedStrategyToggle()
    {
        var proposer = new DeterministicMetaProposer();
        var candidates = Enumerable.Range(1, 5)
            .Select(iteration => proposer.Propose(ProposerGenome.Default, iteration))
            .ToArray();

        Assert.All(candidates, candidate => Assert.False(candidate.BehaviorEquals(ProposerGenome.Default)));
        var toggle = candidates.Single(candidate => candidate.RefinementStrategy == RefinementStrategy.SignatureDriven);
        Assert.Equal(ProposerGenome.Default.MutationStep, toggle.MutationStep);
        Assert.Equal(ProposerGenome.Default.ParameterSelectionPolicy, toggle.ParameterSelectionPolicy);
        Assert.Equal(ProposerGenome.Default.EvaluationBudgetShare, toggle.EvaluationBudgetShare);
        Assert.Equal(ProposerGenome.Default.PromptTemplate, toggle.PromptTemplate);

        var back = proposer.Propose(toggle, 4);
        Assert.Equal(RefinementStrategy.SessionDriven, back.RefinementStrategy);
        Assert.False(back.BehaviorEquals(toggle));
    }

    [Fact]
    public void MetaLoop_GivenUseHarnessRefinement_ExpectedSameFrozenManifestGateAndFreshHoldout()
    {
        var plainDirectory = AgentTestSupport.CreateTempDirectory("round8-refiner-plain");
        var refinedDirectory = AgentTestSupport.CreateTempDirectory("round8-refiner-on");
        try
        {
            var plainEvaluator = FreshEvaluator();
            var refinedEvaluator = FreshEvaluator();
            var plain = MetaLoop.Run(
                MetaOptions(plainDirectory, useHarnessRefinement: false),
                episodeRunner: new FakeEpisodeRunner(),
                reportEvaluator: plainEvaluator,
                metaProposer: RefinerCandidateProposer());
            var refined = MetaLoop.Run(
                MetaOptions(refinedDirectory, useHarnessRefinement: true),
                episodeRunner: new FakeEpisodeRunner(),
                reportEvaluator: refinedEvaluator,
                metaProposer: RefinerCandidateProposer());

            var plainIteration = Assert.Single(plain.Iterations);
            var refinedIteration = Assert.Single(refined.Iterations);
            Assert.Equal(MetaDecision.EligibleReason, plainIteration.Outcome);
            Assert.Equal(MetaDecision.EligibleReason, refinedIteration.Outcome);
            var plainDocument = Assert.IsType<MetaEvaluationDocument>(plainIteration.Document);
            var refinedDocument = Assert.IsType<MetaEvaluationDocument>(refinedIteration.Document);

            var pinned = FrozenComponents.Pinned;
            Assert.Equal(pinned.ManifestHash, plain.FrozenManifest.ManifestHash);
            Assert.Equal(pinned.ManifestHash, refined.FrozenManifest.ManifestHash);
            Assert.Equal(pinned.ManifestHash, refinedDocument.FrozenManifestHash);
            Assert.Equal(pinned.ManifestHash, refinedIteration.Decision!.FrozenManifestHash);

            Assert.Equal(plainIteration.Decision, refinedIteration.Decision);
            Assert.Equal(plainDocument.DocumentHash, refinedDocument.DocumentHash);
            Assert.Equal(0.1, refinedIteration.Decision.RequiredHitRateImprovement, 12);
            Assert.Equal(0.0, refinedIteration.Decision.MarginBbPer100, 12);
            Assert.Equal(2, refinedIteration.Decision.PrimarySeeds);
            Assert.Equal(2, refinedIteration.Decision.HitRate.Seeds);
            Assert.Equal(0.0, refinedIteration.Decision.FreshNonInferiorityFloor, 12);
            Assert.True(refinedIteration.Decision.PrimaryVerified);

            Assert.NotNull(refinedDocument.FreshReport);
            Assert.Equal(2, refinedDocument.FreshReport!.Seeds);
            Assert.Equal(5.0, refinedDocument.Decision.FreshLowerBound, 12);
            Assert.Equal(3, refinedEvaluator.Calls);
            Assert.True(MetaDecisionVerifier.Verify(refinedDocument, FrozenComponents.Capture(), out var verifyReason), verifyReason);
            Assert.True(MetaStore.VerifyLedger(refinedDirectory).Verified);
        }
        finally
        {
            Directory.Delete(plainDirectory, recursive: true);
            Directory.Delete(refinedDirectory, recursive: true);
        }
    }

    [Fact]
    public void MetaLoop_GivenCandidateWinsMetaLosesFresh_ExpectedRefusal()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("round8-refiner-overfit");
        try
        {
            var evaluator = FreshEvaluator();
            evaluator.FreshLowerBound = -50.0;
            var result = MetaLoop.Run(
                MetaOptions(dataDirectory, useHarnessRefinement: true),
                episodeRunner: new FakeEpisodeRunner(),
                reportEvaluator: evaluator,
                metaProposer: RefinerCandidateProposer());

            var iteration = Assert.Single(result.Iterations);
            Assert.Equal("rejected:" + MetaDecision.FreshRegressionReason, iteration.Outcome);
            Assert.False(iteration.Eligible);
            Assert.Null(result.EligibleProposerId);
            var document = Assert.IsType<MetaEvaluationDocument>(iteration.Document);
            Assert.NotNull(document.FreshReport);
            Assert.Equal(-50.0, document.Decision.FreshLowerBound, 12);
            Assert.False(MetaDecisionVerifier.Verify(document, FrozenComponents.Capture(), out var reason));
            Assert.Equal(MetaDecisionVerifier.FreshReason, reason);
            Assert.Equal(result.PersistedActiveProposerId, MetaStore.ReadActiveProposer(dataDirectory).Id);
            Assert.NotEqual(iteration.Candidate.Id, result.PersistedActiveProposerId);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenSessionDrivenOption_ExpectedRefinementPolicyDelegates()
    {
        var legacyDirectory = AgentTestSupport.CreateTempDirectory("round8-refiner-legacy");
        var sessionDirectory = AgentTestSupport.CreateTempDirectory("round8-refiner-session");
        var signatureDirectory = AgentTestSupport.CreateTempDirectory("round8-refiner-signature");
        try
        {
            var options = new TrainingOptions(
                Seed: TrainingSeed,
                Iterations: 1,
                TrainingHands: 120,
                EvaluationSeeds: 2,
                EvaluationHandsPerMatch: 2);

            var legacy = TrainingLoop.Run(options with { DataDirectory = legacyDirectory });
            var session = TrainingLoop.Run(options with
            {
                DataDirectory = sessionDirectory,
                HarnessRefinement = true,
                RefinementStrategy = RefinementStrategy.SessionDriven,
            });
            var signature = TrainingLoop.Run(options with
            {
                DataDirectory = signatureDirectory,
                HarnessRefinement = true,
            });

            var legacyIteration = Assert.Single(legacy.Iterations);
            var sessionIteration = Assert.Single(session.Iterations);
            var signatureIteration = Assert.Single(signature.Iterations);

            Assert.Equal(legacyIteration.ChampionId, sessionIteration.ChampionId);
            Assert.Equal(legacyIteration.ChallengerId, sessionIteration.ChallengerId);
            Assert.NotEqual(legacyIteration.ChallengerId, signatureIteration.ChallengerId);

            var legacyChallenger = TrainingLoop.ReadPersistedStrategy(legacyDirectory, legacyIteration.ChallengerId);
            var sessionChallenger = TrainingLoop.ReadPersistedStrategy(sessionDirectory, sessionIteration.ChallengerId);
            Assert.Equal(legacyChallenger.Genome, sessionChallenger.Genome);
        }
        finally
        {
            Directory.Delete(legacyDirectory, recursive: true);
            Directory.Delete(sessionDirectory, recursive: true);
            Directory.Delete(signatureDirectory, recursive: true);
        }
    }

    [Fact]
    public void TrainingLoopEpisodeRunner_GivenUseHarnessRefinement_ExpectedFlagsReachTrainingLoop()
    {
        var root = AgentTestSupport.CreateTempDirectory("round8-refiner-episode-runner");
        try
        {
            var runner = new TrainingLoopEpisodeRunner();
            var options = MetaOptions(root, useHarnessRefinement: false) with
            {
                TrainingHands = 120,
                EvaluationHandsPerMatch = 2,
            };

            var legacy = runner.Run(
                ProposerGenome.Default,
                TrainingSeed,
                options,
                Path.Combine(root, "legacy"),
                CancellationToken.None);
            var session = runner.Run(
                ProposerGenome.Default with { RefinementStrategy = RefinementStrategy.SessionDriven },
                TrainingSeed,
                options with { UseHarnessRefinement = true },
                Path.Combine(root, "session"),
                CancellationToken.None);
            var signature = runner.Run(
                ProposerGenome.Default with { RefinementStrategy = RefinementStrategy.SignatureDriven },
                TrainingSeed,
                options with { UseHarnessRefinement = true },
                Path.Combine(root, "signature"),
                CancellationToken.None);

            Assert.Equal(legacy.Iterations[0].ChallengerId, session.Iterations[0].ChallengerId);
            Assert.NotEqual(legacy.Iterations[0].ChallengerId, signature.Iterations[0].ChallengerId);
            Assert.Equal(legacy.FinalChampionId, session.FinalChampionId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MetaEvaluationDocument BuildStrategyDocument(
        ProposerGenome candidateProposer,
        ProposerGenome incumbentProposer)
    {
        var criteria = new EvaluationCriteria(0.0, 2);
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
            PinnedFrozenHash,
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
            PinnedFrozenHash,
            decision,
            MetaDecision.EligibleReason);
    }

    private static MetaRunOptions MetaOptions(string dataDirectory, bool useHarnessRefinement) =>
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
            MaxWallClock: TimeSpan.FromMinutes(1),
            UseHarnessRefinement: useHarnessRefinement);

    private static FakeMetaProposer RefinerCandidateProposer() =>
        new(ProposerGenome.Default with
        {
            MutationStep = 0.05,
            RefinementStrategy = RefinementStrategy.SignatureDriven,
        });

    private static FakeMetaEvaluator FreshEvaluator() => new()
    {
        FreshBaseSeed = MetaSeedDerivation.FreshComparisonSeed(JobSeed),
        MetaLowerBound = 20.0,
        FreshLowerBound = 5.0,
    };
}