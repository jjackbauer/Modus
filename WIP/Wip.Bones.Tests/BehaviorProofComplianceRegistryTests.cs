using Wip.Shell.E2E.Compliance;
using Xunit;

namespace Wip.Bones.Tests;

public sealed class BehaviorProofComplianceRegistryTests
{
    private const string ScriptStrategiesComplianceRegistryItem =
        BonesScriptStrategiesRequirementsChecklistItems.ComplianceRegistry;

    private const string LearningLoopComplianceRegistryItem =
        BonesLearningLoopReliabilityRequirementsChecklistItems.ComplianceRegistry;

    private const string BoardOrientationComplianceRegistryItem =
        BonesBoardOrientationRequirementsChecklistItems.ComplianceRegistry;

    [Fact]
    [Trait("ChecklistItem", ScriptStrategiesComplianceRegistryItem)]
    public void BehaviorProofComplianceRegistry_GivenScriptStrategiesRequirements_ExpectedMapsChecklistRowToBonesTests()
    {
        var repositoryRoot = BonesTestPaths.FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesScriptStrategies);

        Assert.Equal("BonesScriptStrategies", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.ScriptStrategies.md", document.RelativePath);
        Assert.Equal("WIP/Wip.Bones.Tests", document.OwningTestProjectDirectory);
        Assert.Equal("WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj", document.OwningTestProjectFile);
        Assert.True(document.UseNoBuildForComplianceExecution);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Script strategies requirements registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }

    [Fact]
    [Trait("ChecklistItem", LearningLoopComplianceRegistryItem)]
    public void BehaviorProofComplianceRegistry_GivenLearningLoopReliabilityRequirements_ExpectedMapsChecklistRowToBonesTests()
    {
        var repositoryRoot = BonesTestPaths.FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesLearningLoopReliability);

        Assert.Equal("BonesLearningLoopReliability", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.LearningLoopReliability.md", document.RelativePath);
        Assert.Equal("WIP/Wip.Bones.Tests", document.OwningTestProjectDirectory);
        Assert.Equal("WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj", document.OwningTestProjectFile);
        Assert.True(document.UseNoBuildForComplianceExecution);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Learning loop reliability requirements registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }

    [Fact]
    [Trait("ChecklistItem", BoardOrientationComplianceRegistryItem)]
    public void BehaviorProofComplianceRegistry_GivenBoardOrientationRequirements_ExpectedMapsChecklistRowsToBonesTests()
    {
        var repositoryRoot = BonesTestPaths.FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesBoardOrientation);

        Assert.Equal("BonesBoardOrientation", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.BoardOrientation.md", document.RelativePath);
        Assert.Equal("WIP/Wip.Bones.Tests", document.OwningTestProjectDirectory);
        Assert.Equal("WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj", document.OwningTestProjectFile);
        Assert.True(document.UseNoBuildForComplianceExecution);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Board orientation requirements registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }

    private const string PerStageModelRoutingComplianceRegistryItem =
        "Register `harness/requirements/Wip.Bones.PerStageModelRouting.md` in `BehaviorProofComplianceRegistry` with owning assemblies `Wip.Bones.Tests` and `Wip.Bones.Host.Tests` [depends on test coverage]";

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.ComplianceRegistry)]
    public void BehaviorProofComplianceRegistry_GivenAdversarialCoLearningRequirements_ExpectedRegisteredWithOwningAssemblies()
    {
        var repositoryRoot = BonesTestPaths.FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesAdversarialCoLearning);

        Assert.Equal("BonesAdversarialCoLearning", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.AdversarialCoLearning.md", document.RelativePath);
        Assert.Equal("WIP/Wip.Bones.Tests", document.OwningTestProjectDirectory);
        Assert.Equal("WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj", document.OwningTestProjectFile);
        Assert.True(document.UseNoBuildForComplianceExecution);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Adversarial co-learning requirements registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }

    [Fact]
    [Trait("ChecklistItem", PerStageModelRoutingComplianceRegistryItem)]
    public void BehaviorProofComplianceRegistry_GivenPerStageModelRoutingRequirements_ExpectedRegisteredWithOwningAssemblies()
    {
        var repositoryRoot = BonesTestPaths.FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesPerStageModelRouting);

        Assert.Equal("BonesPerStageModelRouting", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.PerStageModelRouting.md", document.RelativePath);
        Assert.Equal("WIP/Wip.Bones.Tests", document.OwningTestProjectDirectory);
        Assert.Equal("WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj", document.OwningTestProjectFile);
        Assert.True(document.UseNoBuildForComplianceExecution);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Per-stage model routing requirements registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }
}
