using Wip.Shell.E2E.Compliance;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BehaviorProofComplianceRegistryTests
{
    private const string ComplianceRegistryItem = BonesHostRequirementsChecklistItems.ComplianceRegistry;

    [Fact]
    [Trait("ChecklistItem", ComplianceRegistryItem)]
    public void BehaviorProofComplianceRegistry_GivenWipBonesHostRequirements_ExpectedDocRegisteredWithOwningAssembly()
    {
        var repositoryRoot = BonesHostTestPaths.FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesHost);

        Assert.Equal("BonesHost", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.Host.md", document.RelativePath);
        Assert.Equal("WIP/Wip.Bones.Host.Tests", document.OwningTestProjectDirectory);
        Assert.Equal("WIP/Wip.Bones.Host.Tests/Wip.Bones.Host.Tests.csproj", document.OwningTestProjectFile);
        Assert.True(document.UseNoBuildForComplianceExecution);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Bones host requirements registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }

    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.ComplianceRegistry)]
    public void BehaviorProofComplianceRegistry_GivenLearningLoopReliabilityRequirements_ExpectedRegisteredInComplianceRegistry()
    {
        var repositoryRoot = BonesHostTestPaths.FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesLearningLoopReliability);

        Assert.Equal("BonesLearningLoopReliability", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.LearningLoopReliability.md", document.RelativePath);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Learning loop reliability registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }

    [Fact]
    [Trait("ChecklistItem", BonesHostRequirementsChecklistItems.CoLearningConcurrencyProof)]
    public void BehaviorProofComplianceRegistry_GivenAdversarialCoLearningRequirements_ExpectedDocumentExistsAndIsValid()
    {
        var repositoryRoot = BonesHostTestPaths.FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesAdversarialCoLearning);

        Assert.Equal("BonesAdversarialCoLearning", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.AdversarialCoLearning.md", document.RelativePath);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Adversarial co-learning requirements registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }
}