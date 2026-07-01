using Wip.Shell.E2E.Compliance;
using Xunit;

namespace Wip.Bones.ModelProviders.DeepSeek.Tests;

public sealed class BehaviorProofComplianceRegistryTests
{
    private const string ComplianceRegistryItem = BonesDeepSeekRequirementsChecklistItems.ComplianceRegistry;

    [Fact]
    [Trait("ChecklistItem", ComplianceRegistryItem)]
    public void BehaviorProofComplianceRegistry_GivenWipBonesDeepSeekRequirements_ExpectedDocRegisteredWithOwningAssembly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.BonesDeepSeek);

        Assert.Equal("BonesDeepSeek", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.DeepSeek.md", document.RelativePath);
        Assert.Equal("WIP/Wip.Bones.ModelProviders.DeepSeek.Tests", document.OwningTestProjectDirectory);
        Assert.Equal(
            "WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/Wip.Bones.ModelProviders.DeepSeek.Tests.csproj",
            document.OwningTestProjectFile);
        Assert.True(document.UseNoBuildForComplianceExecution);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Bones DeepSeek requirements registration must resolve to existing paths.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Modus.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root from test execution directory.");
    }
}
