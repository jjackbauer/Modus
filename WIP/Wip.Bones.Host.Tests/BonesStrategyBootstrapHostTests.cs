using System.Text.Json;
using Wip.Abstractions.Identifiers;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesStrategyBootstrapHostTests
{
    private const string BootstrapperItem = BonesHostRequirementsChecklistItems.StrategyBootstrapper;
    private const string OperatorSetupItem = BonesHostRequirementsChecklistItems.OperatorSetupDocumentation;

    [Fact]
    [Trait("ChecklistItem", BootstrapperItem)]
    public void BonesHostReadme_GivenOperatorSetupSection_ExpectedDocumentsRunCapAndResumeFlags()
    {
        var readmePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "Wip.Bones.Host", "README.md"));

        if (!File.Exists(readmePath))
        {
            readmePath = Path.Combine(BonesHostTestPaths.FindRepositoryRoot(), "WIP", "Wip.Bones.Host", "README.md");
        }

        var readme = File.ReadAllText(readmePath);

        Assert.Contains("BONES_MAX_GAMES_PER_RUN=50", readme, StringComparison.Ordinal);
        Assert.Contains("BONES_RESUME_FROM_BEST=true", readme, StringComparison.Ordinal);
        Assert.Contains("BONES_RESUME_FROM_BEST=false", readme, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", OperatorSetupItem)]
    public void BonesHostReadme_GivenOperatorSetupSection_ExpectedExplainsBoundedRunsAndColdStart()
    {
        var readmePath = Path.Combine(BonesHostTestPaths.FindRepositoryRoot(), "WIP", "Wip.Bones.Host", "README.md");
        var readme = File.ReadAllText(readmePath);

        Assert.Contains("bounded runs", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cold-start", readme, StringComparison.OrdinalIgnoreCase);
    }
}
