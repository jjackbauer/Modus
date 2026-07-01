using Wip.Shell.E2E.Compliance;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BehaviorProofComplianceRegistryTests
{
    private const string ViewerVisualizationComplianceRegistryItem =
        BonesViewerVisualizationRequirementsChecklistItems.ComplianceRegistry;

    private const string ViewerReplayScalingComplianceRegistryItem =
        BonesViewerReplayScalingRequirementsChecklistItems.ComplianceRegistry;

    private const string ViewerBoardChainLayoutComplianceRegistryItem =
        BonesViewerBoardChainLayoutRequirementsChecklistItems.ComplianceRegistry;

    private const string ViewerDominoRenderingComplianceRegistryItem =
        BonesViewerDominoRenderingRequirementsChecklistItems.ComplianceRegistry;

    private const string ViewerLayoutAndStabilityComplianceRegistryItem =
        BonesViewerLayoutAndStabilityRequirementsChecklistItems.ComplianceRegistry;

    private const string ViewerLearningLoopStatusComplianceRegistryItem =
        BonesViewerLearningLoopStatusRequirementsChecklistItems.ComplianceRegistry;

    [Fact]
    [Trait("ChecklistItem", ViewerVisualizationComplianceRegistryItem)]
    public void BehaviorProofComplianceRegistry_GivenViewerVisualizationRequirements_ExpectedMapsChecklistRowsToWebTests()
    {
        var repositoryRoot = BonesWebTestPaths.FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesViewerVisualization);

        Assert.Equal("BonesViewerVisualization", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.ViewerVisualization.md", document.RelativePath);
        Assert.Equal("WIP/Wip.Bones.Web.Tests", document.OwningTestProjectDirectory);
        Assert.Equal("WIP/Wip.Bones.Web.Tests/Wip.Bones.Web.Tests.csproj", document.OwningTestProjectFile);
        Assert.True(document.UseNoBuildForComplianceExecution);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Viewer visualization requirements registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }

    [Fact]
    [Trait("ChecklistItem", ViewerReplayScalingComplianceRegistryItem)]
    public void BehaviorProofComplianceRegistry_GivenViewerReplayScalingRequirements_ExpectedMapsChecklistRowsToWebTests()
    {
        var repositoryRoot = BonesWebTestPaths.FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesViewerReplayScaling);

        Assert.Equal("BonesViewerReplayScaling", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.ViewerReplayScaling.md", document.RelativePath);
        Assert.Equal("WIP/Wip.Bones.Web.Tests", document.OwningTestProjectDirectory);
        Assert.Equal("WIP/Wip.Bones.Web.Tests/Wip.Bones.Web.Tests.csproj", document.OwningTestProjectFile);
        Assert.True(document.UseNoBuildForComplianceExecution);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Viewer replay scaling requirements registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }

    [Fact]
    [Trait("ChecklistItem", ViewerBoardChainLayoutComplianceRegistryItem)]
    public void BehaviorProofComplianceRegistry_GivenViewerBoardChainLayoutRequirements_ExpectedMapsChecklistRowsToWebTests()
    {
        var repositoryRoot = BonesWebTestPaths.FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesViewerBoardChainLayout);

        Assert.Equal("BonesViewerBoardChainLayout", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.ViewerBoardChainLayout.md", document.RelativePath);
        Assert.Equal("WIP/Wip.Bones.Web.Tests", document.OwningTestProjectDirectory);
        Assert.Equal("WIP/Wip.Bones.Web.Tests/Wip.Bones.Web.Tests.csproj", document.OwningTestProjectFile);
        Assert.True(document.UseNoBuildForComplianceExecution);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Viewer board chain layout requirements registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }

    [Fact]
    [Trait("ChecklistItem", ViewerDominoRenderingComplianceRegistryItem)]
    public void BehaviorProofComplianceRegistry_GivenViewerDominoRenderingRequirements_ExpectedMapsRegressionChecklistRowsToWebTests()
    {
        var repositoryRoot = BonesWebTestPaths.FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesViewerDominoRendering);

        Assert.Equal("BonesViewerDominoRendering", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.ViewerDominoRendering.md", document.RelativePath);
        Assert.Equal("WIP/Wip.Bones.Web.Tests", document.OwningTestProjectDirectory);
        Assert.Equal("WIP/Wip.Bones.Web.Tests/Wip.Bones.Web.Tests.csproj", document.OwningTestProjectFile);
        Assert.True(document.UseNoBuildForComplianceExecution);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Viewer domino rendering requirements registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }

    [Fact]
    [Trait("ChecklistItem", ViewerLayoutAndStabilityComplianceRegistryItem)]
    public void BehaviorProofComplianceRegistry_GivenViewerLayoutAndStabilityRequirements_ExpectedMapsChecklistRowsToWebTests()
    {
        var repositoryRoot = BonesWebTestPaths.FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesViewerLayoutAndStability);

        Assert.Equal("BonesViewerLayoutAndStability", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.ViewerLayoutAndStability.md", document.RelativePath);
        Assert.Equal("WIP/Wip.Bones.Web.Tests", document.OwningTestProjectDirectory);
        Assert.Equal("WIP/Wip.Bones.Web.Tests/Wip.Bones.Web.Tests.csproj", document.OwningTestProjectFile);
        Assert.True(document.UseNoBuildForComplianceExecution);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Viewer layout and stability requirements registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }

    [Fact]
    [Trait("ChecklistItem", ViewerLearningLoopStatusComplianceRegistryItem)]
    public void BehaviorProofComplianceRegistry_GivenViewerLearningLoopStatusRequirements_ExpectedMapsChecklistRowsToWebTests()
    {
        var repositoryRoot = BonesWebTestPaths.FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesViewerLearningLoopStatus);

        Assert.Equal("BonesViewerLearningLoopStatus", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.ViewerLearningLoopStatus.md", document.RelativePath);
        Assert.Equal("WIP/Wip.Bones.Web.Tests", document.OwningTestProjectDirectory);
        Assert.Equal("WIP/Wip.Bones.Web.Tests/Wip.Bones.Web.Tests.csproj", document.OwningTestProjectFile);
        Assert.True(document.UseNoBuildForComplianceExecution);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Viewer learning loop status requirements registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }
}