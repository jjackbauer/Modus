using Wip.Shell.E2E.Compliance;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesLearningLoopReliabilityReadmeTests
{
    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.OperatorDiagnosticsReadme)]
    public void BonesHostReadme_GivenOperatorDiagnosticsSection_ExpectedDocumentsStatusFieldsAndArtifacts()
    {
        var repositoryRoot = BonesHostTestPaths.FindRepositoryRoot();
        var readmePath = Path.Combine(repositoryRoot, "WIP", "Wip.Bones.Host", "README.md");
        var readme = File.ReadAllText(readmePath);

        Assert.Contains("## Operator diagnostics", readme, StringComparison.Ordinal);
        Assert.Contains("lastIterationError", readme, StringComparison.Ordinal);
        Assert.Contains("lastPromotionOutcome", readme, StringComparison.Ordinal);
        Assert.Contains("learningPlayerStatus", readme, StringComparison.Ordinal);
        Assert.Contains("CompileAttempts", readme, StringComparison.Ordinal);
        Assert.Contains("bones-workflow-stage-failure", readme, StringComparison.Ordinal);
        Assert.Contains("bones-strategy-promotion", readme, StringComparison.Ordinal);
    }
}