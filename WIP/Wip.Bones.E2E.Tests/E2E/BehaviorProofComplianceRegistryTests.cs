using Wip.Shell.E2E.Compliance;
using Xunit;

namespace Wip.Bones.E2E.Tests.E2E;

public sealed class BehaviorProofComplianceRegistryTests
{
    private const string ComplianceRegistryChecklistItem =
        "Register `Wip.Bones` requirements document in `BehaviorProofComplianceRegistry` with owning test assembly bindings [depends on E2E harness]";

    [Fact]
    [Trait("ChecklistItem", ComplianceRegistryChecklistItem)]
    public void BehaviorProofComplianceRegistry_GivenWipBonesRequirements_ExpectedDocRegisteredWithOwningAssembly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.Bones);

        Assert.Equal("Bones", document.DisplayName);
        Assert.Equal("harness/requirements/Wip.Bones.md", document.RelativePath);
        Assert.Equal("WIP/Wip.Bones.E2E.Tests", document.OwningTestProjectDirectory);
        Assert.Equal("WIP/Wip.Bones.E2E.Tests/Wip.Bones.E2E.Tests.csproj", document.OwningTestProjectFile);
        Assert.True(document.UseNoBuildForComplianceExecution);

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot, [document]);
        Assert.True(
            registrationErrors.Count == 0,
            $"Bones requirements registration must resolve to existing paths and owning test assembly.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");

        Assert.True(
            Directory.Exists(Path.Combine(repositoryRoot, document.OwningTestProjectDirectory)),
            $"Owning test directory must exist for '{document.DisplayName}'.");
        Assert.True(
            File.Exists(Path.Combine(repositoryRoot, document.OwningTestProjectFile)),
            $"Owning test project must exist for '{document.DisplayName}'.");
        Assert.True(
            File.Exists(Path.Combine(repositoryRoot, document.RelativePath.Replace('/', Path.DirectorySeparatorChar))),
            $"Requirements document must exist for '{document.DisplayName}'.");

        var bindingRule = BehaviorProofComplianceRegistry.DefaultBindingRule;
        Assert.Equal(BehaviorProofComplianceRegistry.ChecklistItemTraitName, bindingRule.TraitName);
        Assert.Equal(BehaviorProofComplianceRegistry.CanonicalComplianceTestName, bindingRule.CanonicalComplianceTestName);
        Assert.Contains("Trait", bindingRule.BindingDescription, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Modus.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root from test execution directory.");
    }
}
