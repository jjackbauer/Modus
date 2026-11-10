using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// A completed meta iteration must persist its evaluation document at the path the audit path reads:
/// <c>&lt;data&gt;/meta/evaluations/&lt;candidate&gt;-vs-&lt;incumbent&gt;.json</c>.
/// </summary>
public sealed class MetaDocumentPathTests
{
    private const ulong JobSeed = 1_099_511_627_901UL;

    [Fact]
    public void MetaLoop_GivenCompletedIteration_ExpectedDocumentReadableByTryReadEvaluationDocument()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("meta-document-path");
        try
        {
            var result = MetaLoop.Run(
                Options(dataDirectory),
                episodeRunner: new FakeEpisodeRunner(),
                reportEvaluator: new FakeMetaEvaluator
                {
                    FreshBaseSeed = MetaSeedDerivation.FreshComparisonSeed(JobSeed),
                    MetaLowerBound = 20.0,
                    FreshLowerBound = 5.0,
                },
                metaProposer: new FakeMetaProposer(ProposerGenome.Default with { MutationStep = 0.05 }));

            var iteration = Assert.Single(result.Iterations);
            Assert.Equal(MetaDecision.EligibleReason, iteration.Outcome);
            Assert.True(iteration.Eligible);
            var document = Assert.IsType<MetaEvaluationDocument>(iteration.Document);
            var canonicalJson = document.ToCanonicalJson();

            var fileName = document.CandidateId + "-vs-" + document.IncumbentId + ".json";
            var evaluationsDirectory = Path.Combine(dataDirectory, "meta", "evaluations");
            var nestedDirectory = Path.Combine(evaluationsDirectory, "meta", "evaluations");
            var documentPath = Path.Combine(evaluationsDirectory, fileName);

            var readBack = MetaStore.TryReadEvaluationDocument(
                dataDirectory,
                document.CandidateId,
                document.IncumbentId);
            Assert.NotNull(readBack);
            Assert.Equal(canonicalJson, Assert.IsType<string>(readBack));

            Assert.False(Directory.Exists(nestedDirectory));
            Assert.False(File.Exists(Path.Combine(nestedDirectory, fileName)));
            Assert.True(File.Exists(documentPath));
            Assert.Equal(canonicalJson, File.ReadAllText(documentPath));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
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
}