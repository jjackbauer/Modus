using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Agents.Learning.Skills;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// R-09 and R-10 runtime behaviour: the optional skill set must be inert at its defaults, preserve
/// the legacy artifact bytes when absent, and only refine skills behind the explicit opt-in flag.
/// </summary>
public sealed class HarnessSkillsTests
{
    private const ulong SeededSessionSeed = 20_261_112UL;

    private const int SeededSessionHands = 240;

    // Recorded from the pre-change implementation (240 hands, seed 20261112). Both values are the
    // regression pin for the legacy decision path.
    private const string DefaultManifestHash =
        "689e0953320890e0ba0c20af0d69ee07515f8f127552995471917086b807f6bd";

    private const string DefaultDecisionDigest =
        "54da607ff6055e9347b66e434d1991160b3961cb9a50d79faf447f942189d5c1";

    private const string CustomManifestHash =
        "f566937c2ecb2b98d8dfdac59cca1b11b533d6f0a620a53e86e12b49d2a9bf49";

    private const string CustomDecisionDigest =
        "f7bd208e615dfc2f9767cbd2fda5fb03950ea4b06b497a6e82988b39a0ec7b32";

    private static readonly StrategyGenome CustomGenome = new(0.61, 0.33, 0.27, 1.65);

    [Fact]
    public void Agent_GivenSkillsAbsent_ExpectedByteIdenticalDecisionsAcrossSeededCorpus()
    {
        var defaultRun = RunSession(new StrategyGenome());
        var customRun = RunSession(CustomGenome);

        Assert.Equal(SeededSessionHands, defaultRun.Report.Hands);
        Assert.True(defaultRun.Probe.Turns.Count >= 400, "The seeded corpus must exercise real turns.");
        Assert.Equal(DefaultManifestHash, defaultRun.Report.ManifestHash);
        Assert.Equal(DefaultDecisionDigest, DecisionDigest(defaultRun.Probe.Turns));
        Assert.Equal(CustomManifestHash, customRun.Report.ManifestHash);
        Assert.Equal(CustomDecisionDigest, DecisionDigest(customRun.Probe.Turns));

        foreach (var turn in defaultRun.Probe.Turns)
        {
            LearningTestSupport.AssertLegalAction(turn.Observation, turn.Action);
        }

        Assert.Null(StrategyArtifact.Create(new StrategyGenome(), 0, null).Genome.Skills);
    }

    [Fact]
    public void Agent_GivenDefaultSkills_ExpectedByteIdenticalDecisionsAcrossSeededCorpus()
    {
        Assert.True(StrategySkillSet.Default.BehaviorEquals(StrategySkillSet.FromGenome(new StrategyGenome())));

        var absent = RunSession(CustomGenome);
        var withSkills = RunSession(CustomGenome with { Skills = StrategySkillSet.FromGenome(CustomGenome) });

        Assert.Equal(absent.Report.SessionId, withSkills.Report.SessionId);
        Assert.Equal(absent.Report.ManifestHash, withSkills.Report.ManifestHash);
        Assert.Equal(DecisionDigest(absent.Probe.Turns), DecisionDigest(withSkills.Probe.Turns));
        Assert.Equal(CustomManifestHash, withSkills.Report.ManifestHash);
        Assert.Equal(CustomDecisionDigest, DecisionDigest(withSkills.Probe.Turns));
        Assert.Equal(absent.Probe.Turns.Count, withSkills.Probe.Turns.Count);

        for (var index = 0; index < absent.Probe.Turns.Count; index++)
        {
            Assert.Equal(absent.Probe.Turns[index].Action, withSkills.Probe.Turns[index].Action);
            Assert.Equal(
                absent.Probe.Turns[index].Observation.ToCanonicalJson(),
                withSkills.Probe.Turns[index].Observation.ToCanonicalJson());
        }
    }

    [Fact]
    public async Task Agent_GivenPreflopSkillParameter_ExpectedOnlyPreflopDecisionsChange()
    {
        var baselineRun = RunSession(CustomGenome);
        var derived = StrategySkillSet.FromGenome(CustomGenome);
        var modifiedGenome = CustomGenome with
        {
            Skills = derived with { Preflop = new PreflopSkillParameters(0.10, 0.10) },
        };
        var baselineAgent = new StrategyAgent(CustomGenome, "strategy");
        var modifiedAgent = new StrategyAgent(modifiedGenome, "strategy");

        var preflopTurns = 0;
        var preflopChanges = 0;
        var postflopTurns = 0;
        foreach (var turn in baselineRun.Probe.Turns)
        {
            var before = await baselineAgent.ActAsync(turn.Observation, turn.Context, CancellationToken.None);
            var after = await modifiedAgent.ActAsync(turn.Observation, turn.Context, CancellationToken.None);
            Assert.Equal(turn.Action, before);

            if (turn.Observation.Street == Street.Preflop)
            {
                preflopTurns++;
                if (before != after)
                {
                    preflopChanges++;
                }
            }
            else
            {
                postflopTurns++;
                Assert.Equal(before, after);
            }
        }

        Assert.True(preflopTurns > 0, "The corpus must contain preflop decisions.");
        Assert.True(postflopTurns > 0, "The corpus must contain postflop decisions.");
        Assert.True(preflopChanges > 0, "Changing the preflop skill parameter must change preflop decisions.");
    }

    [Fact]
    public async Task Agent_GivenIllegalSkillAction_ExpectedLegalFallback()
    {
        var checkOnly = new PlayerObservation
        {
            Street = Street.Preflop,
            LegalActionKinds = new[] { ActionKind.Check, ActionKind.Fold },
            AmountToCall = 0,
            Pot = 30,
            MinRaiseTo = 20,
            MaxRaiseTo = 100,
        };

        var illegalKind = StrategyAgent.EnsureLegalSkillAction(checkOnly, PokerAction.BetTo(500));
        Assert.Equal(PokerAction.Check(), illegalKind);
        Assert.Contains(illegalKind.Kind, checkOnly.LegalActionKinds);

        var callOnly = new PlayerObservation
        {
            Street = Street.Flop,
            LegalActionKinds = new[] { ActionKind.Call, ActionKind.Fold },
            AmountToCall = 20,
            Pot = 60,
            MinRaiseTo = 40,
            MaxRaiseTo = 200,
        };

        var illegalRaise = StrategyAgent.EnsureLegalSkillAction(callOnly, PokerAction.RaiseTo(60));
        Assert.Equal(PokerAction.Call(), illegalRaise);
        Assert.Contains(illegalRaise.Kind, callOnly.LegalActionKinds);

        var betAllowed = new PlayerObservation
        {
            Street = Street.Flop,
            LegalActionKinds = new[] { ActionKind.Bet, ActionKind.Check, ActionKind.Fold },
            AmountToCall = 0,
            Pot = 40,
            MinRaiseTo = 20,
            MaxRaiseTo = 100,
        };

        var outOfRange = StrategyAgent.EnsureLegalSkillAction(betAllowed, PokerAction.BetTo(500));
        Assert.Equal(PokerAction.Check(), outOfRange);
        Assert.Contains(outOfRange.Kind, betAllowed.LegalActionKinds);

        var defaultSkills = new StrategyGenome(Skills: StrategySkillSet.Default);
        var probe = new RecordingAgent(new StrategyAgent(defaultSkills, "strategy"));
        MatchSimulator.Run(
            probe,
            new TightAggressiveAgent(name: "baseline"),
            new SimulationOptions(SeededSessionSeed, 40));
        Assert.NotEmpty(probe.Turns);
        foreach (var turn in probe.Turns)
        {
            LearningTestSupport.AssertLegalAction(turn.Observation, turn.Action);
        }
    }

    [Fact]
    public void Artifact_GivenSkillsAbsent_ExpectedLegacyCanonicalBytesAndHashPreserved()
    {
        var artifact = StrategyArtifact.Create(new StrategyGenome(), 0, null);

        const string ExpectedJson =
            "{\"id\":\"tholdem-strategy-v1-f100e793\",\"version\":1,\"parentId\":null,\"createdAtIteration\":0,"
            + "\"parameters\":{\"raiseThreshold\":0.72,\"callThreshold\":0.45,\"bluffFrequency\":0.12,\"sizingMultiplier\":1},"
            + "\"promptText\":\"Play tight-aggressive heads-up: raise strong hands, fold weak hands to large bets, and size value bets by the sizing multiplier.\"}";

        Assert.Equal("tholdem-strategy-v1-f100e793", artifact.Id);
        Assert.Equal("f100e793f3023b324cbc91514e31294d39043e6cbb6d55b37c339bfbbda202c5", artifact.Hash);
        Assert.Equal(ExpectedJson, artifact.ToCanonicalJson());
        Assert.Equal(
            "6675d7975b69eafd7cd64f334e70ad5adbfa09fbcc32176a6098aa9649124c14",
            AgentTestSupport.Sha256Hex(LearningTestSupport.Utf8(artifact.ToCanonicalJson())));
        Assert.DoesNotContain("\"skills\"", artifact.ToCanonicalJson(), StringComparison.Ordinal);
        Assert.Equal(artifact.Hash, StrategyArtifact.ComputeHash(artifact.ToHashInputJson()));
    }

    [Fact]
    public void Artifact_GivenSkillsPresent_ExpectedCanonicalRoundTrip()
    {
        var skills = new StrategySkillSet(
            new PreflopSkillParameters(0.5, 0.3),
            new PostflopSkillParameters(0.55, 0.35),
            new SizingSkillParameters(1.25),
            new BluffSkillParameters(0.18));
        var genome = new StrategyGenome(0.68, 0.41, 0.2, 1.5, "raise the top of the range", skills);
        var artifact = StrategyArtifact.Create(genome, 3, null);
        var withoutSkills = StrategyArtifact.Create(genome with { Skills = null }, 3, null);

        Assert.NotEqual(withoutSkills.Hash, artifact.Hash);
        Assert.NotEqual(withoutSkills.Id, artifact.Id);
        Assert.Contains(
            "\"skills\":{\"preflop\":{\"raiseThreshold\":0.5,\"callThreshold\":0.3},"
            + "\"postflop\":{\"raiseThreshold\":0.55,\"callThreshold\":0.35},"
            + "\"sizing\":{\"multiplier\":1.25},\"bluff\":{\"frequency\":0.18}}",
            artifact.ToCanonicalJson(),
            StringComparison.Ordinal);

        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-skills-artifact");
        try
        {
            Directory.CreateDirectory(Path.Combine(dataDirectory, "strategies"));
            var path = Path.Combine(dataDirectory, "strategies", artifact.Id + ".json");
            File.WriteAllText(path, artifact.ToCanonicalJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var read = TrainingLoop.ReadPersistedStrategy(dataDirectory, artifact.Id);
            Assert.Equal(artifact, read);
            Assert.Equal(artifact.Hash, read.Hash);
            Assert.Equal(artifact.ToCanonicalJson(), read.ToCanonicalJson());
            Assert.NotNull(read.Genome.Skills);
            Assert.Equal(skills, read.Genome.Skills);
            Assert.Equal(0.5, read.Genome.Skills!.Preflop.RaiseThreshold, 12);
            Assert.Equal(0.18, read.Genome.Skills.Bluff.Frequency, 12);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Artifact_GivenMalformedSkills_ExpectedStableRefusal()
    {
        var skills = StrategySkillSet.FromGenome(new StrategyGenome());
        var artifact = StrategyArtifact.Create(new StrategyGenome(Skills: skills), 2, null);
        var valid = artifact.ToCanonicalJson();
        var malformed = valid.Replace(",\"bluff\":{\"frequency\":0.12}", string.Empty, StringComparison.Ordinal);
        Assert.NotEqual(valid, malformed);

        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-skills-malformed");
        try
        {
            Directory.CreateDirectory(Path.Combine(dataDirectory, "strategies"));
            var path = Path.Combine(dataDirectory, "strategies", artifact.Id + ".json");
            File.WriteAllText(path, malformed, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var exception = Assert.Throws<FormatException>(
                () => TrainingLoop.ReadPersistedStrategy(dataDirectory, artifact.Id));
            Assert.Contains("strategy-skills-invalid", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenSkillRefinementOff_ExpectedPinnedChallengersUnchanged()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-skills-off");
        try
        {
            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: 20_261_111UL,
                    Iterations: 3,
                    TrainingHands: 24,
                    EvaluationSeeds: 2,
                    EvaluationHandsPerMatch: 2,
                    DataDirectory: dataDirectory,
                    HarnessRefinement: true));

            Assert.Equal(
                new[]
                {
                    "tholdem-strategy-v1-f100e793",
                    "tholdem-strategy-v1-f100e793",
                    "tholdem-strategy-v1-f100e793",
                },
                run.Iterations.Select(static iteration => iteration.ChampionId).ToArray());
            Assert.Equal(
                new[]
                {
                    "tholdem-strategy-v2-d24dcf6d",
                    "tholdem-strategy-v2-c34333fe",
                    "tholdem-strategy-v2-86f42856",
                },
                run.Iterations.Select(static iteration => iteration.ChallengerId).ToArray());
            Assert.All(run.Iterations, static iteration => Assert.Equal(TrainingLoop.RejectedOutcome, iteration.Outcome));
            Assert.Equal("tholdem-strategy-v1-f100e793", run.FinalChampionId);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Policy_GivenSkillEditsEnabled_ExpectedSkillParameterEdit()
    {
        var incumbent = new StrategyGenome();
        var analysis = new SessionAnalysis(
            Hands: 120,
            WinRate: 0.42,
            BbPer100: -3.5,
            AggressionFactor: 1.2,
            ShowdownRate: 0.4,
            Vpip: 0.0);
        var low = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.AggressionByStreet, 4, 10, 0.4, FailureSignalDirections.Low),
            new FailureSignal(FailureSignalNames.FoldToRaise, 1, 10, 0.1, FailureSignalDirections.High),
        ]);

        var lowProposal = new SignatureDrivenRefinementPolicy(enableSkillEdits: true)
            .Propose(new RefinementContext(incumbent, analysis, low, Iteration: 1));

        Assert.Null(incumbent.Skills);
        Assert.NotNull(lowProposal.Genome.Skills);
        var derived = StrategySkillSet.FromGenome(incumbent);
        Assert.Equal(derived.Preflop, lowProposal.Genome.Skills!.Preflop);
        Assert.Equal(derived.Postflop, lowProposal.Genome.Skills.Postflop);
        Assert.Equal(derived.Sizing, lowProposal.Genome.Skills.Sizing);
        Assert.Equal(0.20, lowProposal.Genome.Skills.Bluff.Frequency, 12);
        Assert.Equal(RefinementRationaleCodes.AggressionLow, lowProposal.RationaleCode);
        Assert.Equal(incumbent.RaiseThreshold, lowProposal.Genome.RaiseThreshold, 12);
        Assert.Equal(incumbent.CallThreshold, lowProposal.Genome.CallThreshold, 12);
        Assert.Equal(incumbent.BluffFrequency, lowProposal.Genome.BluffFrequency, 12);
        Assert.Equal(incumbent.SizingMultiplier, lowProposal.Genome.SizingMultiplier, 12);
        Assert.Equal(incumbent.PromptText, lowProposal.Genome.PromptText);

        var high = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.AggressionByStreet, 4, 10, 0.4, FailureSignalDirections.High),
        ]);
        var highProposal = new SignatureDrivenRefinementPolicy(enableSkillEdits: true)
            .Propose(new RefinementContext(incumbent, analysis, high, Iteration: 1));
        Assert.NotNull(highProposal.Genome.Skills);
        Assert.Equal(0.04, highProposal.Genome.Skills!.Bluff.Frequency, 12);
        Assert.Equal(RefinementRationaleCodes.AggressionHigh, highProposal.RationaleCode);
    }

    [Fact]
    public void Policy_GivenSkillEditsDisabled_ExpectedLegacyNumericProposal()
    {
        var incumbent = new StrategyGenome();
        var analysis = new SessionAnalysis(
            Hands: 120,
            WinRate: 0.42,
            BbPer100: -3.5,
            AggressionFactor: 1.2,
            ShowdownRate: 0.4,
            Vpip: 0.0);
        var low = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.AggressionByStreet, 4, 10, 0.4, FailureSignalDirections.Low),
        ]);

        var proposal = new SignatureDrivenRefinementPolicy()
            .Propose(new RefinementContext(incumbent, analysis, low, Iteration: 1));

        Assert.Null(proposal.Genome.Skills);
        Assert.Equal(0.20, proposal.Genome.BluffFrequency, 12);
        Assert.Equal(RefinementRationaleCodes.AggressionLow, proposal.RationaleCode);
    }

    [Fact]
    public void Guard_GivenUnclampedSkillEdit_ExpectedRefusal()
    {
        var incumbent = new StrategyGenome();
        var unclamped = incumbent with
        {
            Skills = new StrategySkillSet(
                new PreflopSkillParameters(1.5, 0.45),
                new PostflopSkillParameters(),
                new SizingSkillParameters(),
                new BluffSkillParameters()),
        };

        Assert.NotEqual(unclamped, unclamped.Clamp());
        var guard = new RefinementGuard();
        var accepted = guard.IsAcceptable(incumbent, unclamped, out var reason);

        Assert.False(accepted);
        Assert.Equal(RefinementGuard.OutOfDomainCode, reason);
    }

    [Fact]
    public void Guard_GivenMultipleSkillEdits_ExpectedRefusal()
    {
        var incumbent = new StrategyGenome();
        var derived = StrategySkillSet.FromGenome(incumbent);
        var candidate = incumbent with
        {
            Skills = derived with
            {
                Preflop = new PreflopSkillParameters(0.5, derived.Preflop.CallThreshold),
                Bluff = new BluffSkillParameters(0.2),
            },
        };

        var guard = new RefinementGuard();
        var accepted = guard.IsAcceptable(incumbent, candidate, out var reason);

        Assert.False(accepted);
        Assert.Equal(RefinementGuard.MultipleSkillEditsCode, reason);
        Assert.Equal(2, derived.BehavioralChangeCount(candidate.Skills!));
    }

    [Fact]
    public void Guard_GivenNonSkillProposal_ExpectedAcceptedUnchanged()
    {
        var incumbent = new StrategyGenome();
        var numeric = incumbent with { RaiseThreshold = incumbent.RaiseThreshold - 0.08 };

        var guard = new RefinementGuard();
        Assert.True(guard.IsAcceptable(incumbent, numeric, out var acceptedReason));
        Assert.Equal(TrainingLoop.ProposalAcceptedReason, acceptedReason);

        var promptOnly = incumbent with { PromptText = "different wording only" };
        var delegating = new RefinementGuard(ProposalGuards.RejectBehaviorallyInert);
        Assert.False(delegating.IsAcceptable(incumbent, promptOnly, out var delegatedReason));
        Assert.Equal(TrainingLoop.InertProposalOutcome, delegatedReason);
    }

    [Fact]
    public void Guard_GivenInertSkillEdit_ExpectedRefusalBeforeEvaluation()
    {
        var incumbent = new StrategyGenome();
        var inert = incumbent with { Skills = StrategySkillSet.FromGenome(incumbent) };

        var guard = new RefinementGuard();
        Assert.False(guard.IsAcceptable(incumbent, inert, out var reason));
        Assert.Equal(RefinementGuard.InertSkillCode, reason);

        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-skills-inert");
        try
        {
            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: 20_261_116UL,
                    Iterations: 1,
                    TrainingHands: 4,
                    EvaluationSeeds: 1,
                    EvaluationHandsPerMatch: 1,
                    DataDirectory: dataDirectory,
                    HarnessRefinement: true,
                    SkillRefinement: true),
                proposer: new DefaultSkillsProposer());

            var iteration = Assert.Single(run.Iterations);
            Assert.Equal(TrainingLoop.InertProposalOutcome, iteration.Outcome);
            Assert.Null(iteration.Report);
            Assert.Empty(Directory.GetFiles(Path.Combine(dataDirectory, "evaluations"), "*.json"));
            Assert.True(File.Exists(Path.Combine(dataDirectory, "strategies", iteration.ChallengerId + ".json")));

            using var ledger = JsonDocument.Parse("[" + string.Join(",", run.LedgerJson) + "]");
            var entry = Assert.Single(ledger.RootElement.EnumerateArray().ToArray());
            Assert.Equal(RefinementGuard.InertSkillCode, entry.GetProperty("reason").GetString());
            Assert.Equal(TrainingLoop.InertProposalOutcome, entry.GetProperty("outcome").GetString());
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenSkillRefinementOn_ExpectedGateAndVerifierUnchanged()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-skills-on");
        try
        {
            var gateBefore = FrozenComponents.Capture();
            var options = new TrainingOptions(
                Seed: 20_261_111UL,
                Iterations: 1,
                TrainingHands: 24,
                EvaluationSeeds: 2,
                EvaluationHandsPerMatch: 2,
                DataDirectory: dataDirectory,
                HarnessRefinement: true,
                SkillRefinement: true);

            var run = TrainingLoop.Run(options);
            var gateAfter = FrozenComponents.Capture();
            var iteration = Assert.Single(run.Iterations);
            Assert.NotNull(iteration.Report);

            var champion = TrainingLoop.ReadPersistedStrategy(dataDirectory, iteration.ChampionId);
            var challenger = TrainingLoop.ReadPersistedStrategy(dataDirectory, iteration.ChallengerId);
            Assert.NotNull(challenger.Genome.Skills);

            var derived = StrategySkillSet.FromGenome(champion.Genome);
            var expectedSkills = derived with
            {
                Bluff = new BluffSkillParameters(derived.Bluff.Frequency + SignatureDrivenRefinementPolicy.Step),
            };
            Assert.Equal(expectedSkills, challenger.Genome.Skills);
            Assert.Equal(champion.Genome.RaiseThreshold, challenger.Genome.RaiseThreshold, 12);
            Assert.Equal(champion.Genome.CallThreshold, challenger.Genome.CallThreshold, 12);
            Assert.Equal(champion.Genome.BluffFrequency, challenger.Genome.BluffFrequency, 12);
            Assert.Equal(champion.Genome.SizingMultiplier, challenger.Genome.SizingMultiplier, 12);
            Assert.Equal(champion.Genome.PromptText, challenger.Genome.PromptText);

            var evaluationSeed = SeedDerivation.Derive(
                options.Seed,
                TrainingLoop.EvaluationSeedHandBase + 1,
                0,
                0,
                TrainingLoop.EvaluationSeedStream);
            var expectedReport = MatchEvaluator.Evaluate(
                new StrategyAgent(challenger.Genome, "challenger"),
                challenger.Id,
                new StrategyAgent(champion.Genome, "champion"),
                champion.Id,
                evaluationSeed,
                options.EvaluationSeeds,
                options.EvaluationHandsPerMatch);

            Assert.Equal(expectedReport.ToCanonicalJson(), iteration.Report!.ToCanonicalJson());
            Assert.Equal(expectedReport.ReportHash, iteration.Report!.ReportHash);
            Assert.Equal(
                expectedReport.ToCanonicalJson(),
                File.ReadAllText(
                    Path.Combine(dataDirectory, "evaluations", challenger.Id + "-vs-" + champion.Id + ".json")));

            var criteria = new EvaluationCriteria();
            var replayDecision = new StatisticalPromotionPolicy().Decide(expectedReport, criteria);
            var replayVerified = PromotionVerifier.Verify(
                replayDecision,
                expectedReport,
                criteria,
                out var replayReason);
            Assert.Equal(replayVerified, iteration.Verified);
            if (replayVerified)
            {
                Assert.Equal(replayDecision, iteration.Decision);
                Assert.Equal(
                    replayDecision.Promoted ? TrainingLoop.PromotedOutcome : TrainingLoop.RejectedOutcome,
                    iteration.Outcome);
            }
            else
            {
                Assert.False(iteration.Decision.Promoted);
                Assert.Equal(replayReason, iteration.Decision.Reason);
                Assert.Equal(TrainingLoop.VerificationFailedOutcome, iteration.Outcome);
            }

            Assert.Equal(gateBefore.ManifestHash, gateAfter.ManifestHash);
            Assert.True(FrozenComponents.MatchesPinned(gateAfter, out var frozenReason), frozenReason);
            Assert.Equal(5, gateAfter.Components.Count);

            using var pointer = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(dataDirectory, TrainingLoop.ChampionFileName)));
            var pointerId = pointer.RootElement.GetProperty("strategyId").GetString();
            if (replayVerified && replayDecision.Promoted)
            {
                Assert.Equal(challenger.Id, pointerId);
                Assert.Equal(1, pointer.RootElement.GetProperty("iteration").GetInt32());
                Assert.Equal(
                    expectedReport.ReportHash,
                    pointer.RootElement.GetProperty("evaluationReportHash").GetString());
            }
            else
            {
                Assert.Equal(champion.Id, pointerId);
                Assert.Equal(0, pointer.RootElement.GetProperty("iteration").GetInt32());
                Assert.False(pointer.RootElement.TryGetProperty("evaluationReportHash", out _));
            }
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static (RecordingAgent Probe, SimulationReport Report) RunSession(StrategyGenome genome)
    {
        var probe = new RecordingAgent(new StrategyAgent(genome, "strategy"));
        var report = MatchSimulator.Run(
            probe,
            new TightAggressiveAgent(name: "baseline"),
            new SimulationOptions(SeededSessionSeed, SeededSessionHands));
        return (probe, report);
    }

    private static string DecisionDigest(IReadOnlyList<RecordedTurn> turns)
    {
        var builder = new StringBuilder();
        foreach (var turn in turns)
        {
            builder.Append(turn.Observation.ToCanonicalJson())
                .Append('|')
                .Append(turn.Context.HandNumber)
                .Append('|')
                .Append(turn.Context.Seat)
                .Append('|')
                .Append(turn.Context.ActionIndex)
                .Append('|')
                .Append(turn.Action.Kind)
                .Append(':')
                .Append(turn.Action.Amount)
                .Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(LearningTestSupport.Utf8(builder.ToString())))
            .ToLowerInvariant();
    }

    /// <summary>Proposes the behaviourally inert skill-enable edit used by the refusal test.</summary>
    private sealed class DefaultSkillsProposer : IStrategyProposer
    {
        public StrategyGenome Propose(StrategyGenome incumbent, SessionAnalysis analysis, int iteration) =>
            incumbent with { Skills = StrategySkillSet.FromGenome(incumbent) };
    }
}