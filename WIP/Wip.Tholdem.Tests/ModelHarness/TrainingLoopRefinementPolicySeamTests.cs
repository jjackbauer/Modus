using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Agents.Simulation;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// WP-1: the optional refinement-policy seam. A supplied policy must be the policy used for every
/// iteration, and a null argument must leave the deterministic artifacts byte-identical.
/// </summary>
public sealed class TrainingLoopRefinementPolicySeamTests
{
    private const ulong Seed = 20_261_109UL;

    [Fact]
    public void TrainingLoop_GivenSuppliedRefinementPolicy_ExpectedPolicyUsedForEveryIteration()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-policy-seam");
        try
        {
            var policy = new KnownGenomeRefinementPolicy();
            var options = new TrainingOptions(
                Seed: Seed,
                Iterations: 2,
                TrainingHands: 12,
                EvaluationSeeds: 2,
                EvaluationHandsPerMatch: 2,
                DataDirectory: dataDirectory,
                HarnessRefinement: true);

            var run = TrainingLoop.Run(options, refinementPolicy: policy);

            Assert.Equal(2, policy.InvocationCount);
            Assert.Equal(new[] { 1, 2 }, policy.Iterations.ToArray());
            Assert.Equal(2, run.Iterations.Count);
            foreach (var iteration in run.Iterations)
            {
                var path = Path.Combine(dataDirectory, "strategies", iteration.ChallengerId + ".json");
                Assert.True(File.Exists(path), "The challenger artifact was not persisted: " + path);
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                var parameters = document.RootElement.GetProperty("parameters");
                Assert.Equal(0.61, parameters.GetProperty("raiseThreshold").GetDouble());
                Assert.Equal(0.37, parameters.GetProperty("callThreshold").GetDouble());
                Assert.Equal(0.22, parameters.GetProperty("bluffFrequency").GetDouble());
                Assert.Equal(1.75, parameters.GetProperty("sizingMultiplier").GetDouble());
            }
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void TrainingLoop_GivenNullPolicy_ExpectedSameArtifactsAsExplicitDeterministicPolicy()
    {
        var nullDirectory = AgentTestSupport.CreateTempDirectory("tholdem-policy-null");
        var explicitDirectory = AgentTestSupport.CreateTempDirectory("tholdem-policy-explicit");
        try
        {
            var options = new TrainingOptions(
                Seed: Seed,
                Iterations: 2,
                TrainingHands: 12,
                EvaluationSeeds: 2,
                EvaluationHandsPerMatch: 2,
                HarnessRefinement: true);

            TrainingLoop.Run(options with { DataDirectory = nullDirectory });
            TrainingLoop.Run(
                options with { DataDirectory = explicitDirectory },
                refinementPolicy: new SignatureDrivenRefinementPolicy());

            var expected = ReadArtifacts(nullDirectory);
            var actual = ReadArtifacts(explicitDirectory);
            Assert.Equal(
                expected.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray(),
                actual.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray());
            foreach (var artifact in expected)
            {
                Assert.Equal(artifact.Value, actual[artifact.Key]);
            }
        }
        finally
        {
            Directory.Delete(nullDirectory, recursive: true);
            Directory.Delete(explicitDirectory, recursive: true);
        }
    }

    private static Dictionary<string, byte[]> ReadArtifacts(string directory) =>
        Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                static path => path,
                static path => File.ReadAllBytes(path),
                StringComparer.Ordinal)
            .ToDictionary(
                pair => Path.GetRelativePath(directory, pair.Key).Replace('\\', '/'),
                static pair => pair.Value,
                StringComparer.Ordinal);

    private sealed class KnownGenomeRefinementPolicy : IRefinementPolicy
    {
        public static readonly StrategyGenome KnownGenome = new(0.61, 0.37, 0.22, 1.75);

        public int InvocationCount { get; private set; }

        public List<int> Iterations { get; } = [];

        public RefinementProposal Propose(RefinementContext context)
        {
            InvocationCount++;
            Iterations.Add(context.Iteration);
            return new RefinementProposal(KnownGenome, RefinementRationaleCodes.LegacySessionDriven);
        }
    }
}
