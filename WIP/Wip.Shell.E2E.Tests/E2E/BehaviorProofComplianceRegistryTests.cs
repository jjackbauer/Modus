using Wip.Shell.E2E.Compliance;
using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

[Collection(E2EComplianceSequentialCollection.Name)]
public sealed class BehaviorProofComplianceRegistryTests
{
    private const string ChecklistItem =
        "Introduce `BehaviorProofComplianceRegistry` enumerating every active WIP requirements document, its owning test assembly, and checklist-to-test binding rules [depends on standardized compliance naming]";

    private const string AggregatedGateChecklistItem =
        "Add aggregated compliance gate that fails when any registered requirements document contains unchecked checklist items or any mapped test fails execution [depends on compliance registry]";

    private static readonly string[] RequiredDocumentDisplayNames =
    [
        "Bootstrap",
        "Next-Steps",
        "DotNet-Development-Flows",
        "Contributor-Readmes",
        "Dynamic-System-Prompt",
        "Validation.DotNet",
        "ShellHost",
        "Integrity-Remediation",
        "Bones",
    ];

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofComplianceRegistry_GivenAllWipRequirementDocuments_ExpectedEachDocHasOwningTestAssembly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var documents = BehaviorProofComplianceRegistry.RegisteredDocuments;

        Assert.Equal(RequiredDocumentDisplayNames.Length, documents.Count);
        Assert.Equal(
            RequiredDocumentDisplayNames.OrderBy(static name => name, StringComparer.Ordinal),
            documents.Select(static document => document.DisplayName).OrderBy(static name => name, StringComparer.Ordinal));

        var registrationErrors = BehaviorProofComplianceRegistry.ValidateRegistrations(repositoryRoot);
        Assert.True(
            registrationErrors.Count == 0,
            $"Every registered document must resolve to an existing requirements file and owning test assembly.{Environment.NewLine}{string.Join(Environment.NewLine, registrationErrors)}");

        foreach (var document in documents)
        {
            Assert.False(string.IsNullOrWhiteSpace(document.OwningTestProjectDirectory));
            Assert.False(string.IsNullOrWhiteSpace(document.OwningTestProjectFile));
            Assert.True(
                Directory.Exists(Path.Combine(repositoryRoot, document.OwningTestProjectDirectory)),
                $"Owning test directory must exist for '{document.DisplayName}'.");
            Assert.True(
                File.Exists(Path.Combine(repositoryRoot, document.OwningTestProjectFile)),
                $"Owning test project must exist for '{document.DisplayName}'.");
            Assert.True(
                File.Exists(Path.Combine(repositoryRoot, document.RelativePath.Replace('/', Path.DirectorySeparatorChar))),
                $"Requirements document must exist for '{document.DisplayName}'.");
        }

        var bindingRule = BehaviorProofComplianceRegistry.DefaultBindingRule;
        Assert.Equal(BehaviorProofComplianceRegistry.ChecklistItemTraitName, bindingRule.TraitName);
        Assert.Equal(BehaviorProofComplianceRegistry.CanonicalComplianceTestName, bindingRule.CanonicalComplianceTestName);
        Assert.Contains("Trait", bindingRule.BindingDescription, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", AggregatedGateChecklistItem)]
    public void BehaviorProofComplianceRegistry_GivenUncheckedItemInRegisteredDoc_ExpectedAggregatedGateFailsWithDocPathAndItemText()
    {
        const string syntheticChecklistItem = "Synthetic unchecked checklist item for registry gate verification";
        const string syntheticRequirements = """
## Completeness Checklist

- [ ] Synthetic unchecked checklist item for registry gate verification

## Test Plan

### Behavior-Proof Compliance Registry

1. `BehaviorProofComplianceRegistry_GivenUncheckedItemInRegisteredDoc_ExpectedAggregatedGateFailsWithDocPathAndItemText`
   *Assumption*: Aggregated gate fails deterministically when any registered document still contains `- [ ]` checklist entries.
""";

        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.IntegrityRemediation);

        var violations = BehaviorProofComplianceRegistry.EvaluateUncheckedChecklistItemsForMarkdown(
            document,
            syntheticRequirements);

        var violation = Assert.Single(violations);
        Assert.Equal(document.RelativePath, violation.DocumentRelativePath);
        Assert.Equal(syntheticChecklistItem, violation.ChecklistItemText);

        var gateResult = BehaviorProofComplianceRegistry.EvaluateUncheckedViolations(violations);
        Assert.False(gateResult.IsCompliant);
        Assert.Contains(
            gateResult.Messages,
            message => message.Contains(document.RelativePath, StringComparison.Ordinal)
                && message.Contains(syntheticChecklistItem, StringComparison.Ordinal));

        var repositoryRoot = FindRepositoryRoot();
        var repositoryUncheckedViolations = BehaviorProofComplianceRegistry.EvaluateUncheckedChecklistItems(
            repositoryRoot,
            [document]);

        Assert.True(
            repositoryUncheckedViolations.Count == 0,
            $"Integrity-Remediation must have no unchecked checklist items after behavior-proof remediation completes. Found: {string.Join("; ", repositoryUncheckedViolations.Select(static violation => violation.ChecklistItemText))}");

        var aggregatedGateResult = BehaviorProofComplianceRegistry.CreateAggregatedGateResult(
            registrationErrors: [],
            uncheckedViolations: violations,
            mappedTestFailures: []);

        Assert.False(aggregatedGateResult.IsCompliant);
        Assert.Single(aggregatedGateResult.UncheckedViolations);
        Assert.Contains(
            aggregatedGateResult.Messages,
            message => message.Contains("Unchecked checklist item", StringComparison.Ordinal)
                && message.Contains(document.RelativePath, StringComparison.Ordinal)
                && message.Contains(syntheticChecklistItem, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", AggregatedGateChecklistItem)]
    public async Task BehaviorProofComplianceRegistry_GivenMappedTestFailure_ExpectedAggregatedGateReportsFailingTestAndDocBinding()
    {
        var document = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.IntegrityRemediation);
        const string boundChecklistItem =
            "Introduce `BehaviorProofComplianceRegistry` enumerating every active WIP requirements document, its owning test assembly, and checklist-to-test binding rules [depends on standardized compliance naming]";

        var executionResult = new BehaviorProofTestExecutionResult(
            TestProjectPath: document.OwningTestProjectFile,
            TestFilter: "FullyQualifiedName~BehaviorProofComplianceRegistry_GivenSyntheticFailure_ExpectedGateReportsBinding",
            ExitCode: 1,
            Output: "Failed!  - Failed:     1, Passed:     0");

        var mappedFailure = BehaviorProofComplianceRegistry.CreateMappedTestFailureViolation(
            document,
            boundChecklistItem,
            executionResult);

        Assert.Equal(document.RelativePath, mappedFailure.DocumentRelativePath);
        Assert.Equal(boundChecklistItem, mappedFailure.ChecklistItemText);
        Assert.Equal(document.OwningTestProjectFile, mappedFailure.TestProjectPath);
        Assert.Contains("BehaviorProofComplianceRegistry_GivenSyntheticFailure", mappedFailure.TestFilter, StringComparison.Ordinal);

        var gateResult = BehaviorProofComplianceRegistry.EvaluateMappedTestFailures([mappedFailure]);
        Assert.False(gateResult.IsCompliant);

        var message = Assert.Single(gateResult.Messages);
        Assert.Contains(document.RelativePath, message, StringComparison.Ordinal);
        Assert.Contains(boundChecklistItem, message, StringComparison.Ordinal);
        Assert.Contains(mappedFailure.TestFilter, message, StringComparison.Ordinal);
        Assert.Contains(mappedFailure.TestProjectPath, message, StringComparison.Ordinal);

        var repositoryRoot = FindRepositoryRoot();
        var completeDocument = BehaviorProofComplianceRegistry.RegisteredDocuments.Single(
            static registration => registration.Kind == WipRequirementsDocumentKind.ShellHost);
        var scopedExecutionResult = executionResult with
        {
            TestProjectPath = completeDocument.OwningTestProjectFile,
        };
        var failingExecutor = new StaticBehaviorProofTestExecutor(scopedExecutionResult);
        var aggregatedGateResult = await BehaviorProofComplianceRegistry.VerifyAllAsync(
            repositoryRoot,
            failingExecutor,
            [completeDocument]);

        Assert.False(aggregatedGateResult.IsCompliant);
        Assert.Empty(aggregatedGateResult.UncheckedViolations);
        var aggregatedFailure = Assert.Single(aggregatedGateResult.MappedTestFailures);
        Assert.Equal(completeDocument.RelativePath, aggregatedFailure.DocumentRelativePath);
        Assert.Contains(scopedExecutionResult.TestFilter, aggregatedFailure.TestFilter, StringComparison.Ordinal);

        var mappedFailureMessage = Assert.Single(
            aggregatedGateResult.Messages,
            message => message.Contains("Mapped test failure", StringComparison.Ordinal));
        Assert.Contains(completeDocument.RelativePath, mappedFailureMessage, StringComparison.Ordinal);
        Assert.Contains(scopedExecutionResult.TestFilter, mappedFailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", AggregatedGateChecklistItem)]
    public async Task BehaviorProofComplianceRegistry_GivenAllRegisteredDocuments_ExpectedAggregatedGatePassesWhenChecklistsCompleteAndMappedTestsPass()
    {
        var repositoryRoot = FindRepositoryRoot();
        var documentsWithCompleteChecklists =
            BehaviorProofComplianceRegistry.SelectDocumentsWithCompleteChecklists(repositoryRoot);

        Assert.NotEmpty(documentsWithCompleteChecklists);

        IBehaviorProofTestExecutor? executor = BehaviorProofComplianceRegistry.ShouldSkipMappedTestExecution()
            ? null
            : new DotNetBehaviorProofTestExecutor();

        var gateResult = await BehaviorProofComplianceRegistry.VerifyAllAsync(
            repositoryRoot,
            executor,
            documentsWithCompleteChecklists);

        Assert.True(
            gateResult.IsCompliant,
            $"Aggregated compliance gate must pass for documents with complete checklists and passing mapped tests.{Environment.NewLine}{string.Join(Environment.NewLine, gateResult.Messages)}");
        Assert.Empty(gateResult.UncheckedViolations);
        Assert.Empty(gateResult.MappedTestFailures);
        Assert.Empty(gateResult.Messages);
    }

    private sealed class StaticBehaviorProofTestExecutor(BehaviorProofTestExecutionResult result) : IBehaviorProofTestExecutor
    {
        public Task<BehaviorProofTestExecutionResult> ExecuteAsync(
            string repositoryRoot,
            WipRequirementsDocumentRegistration document,
            IReadOnlyList<string> testNames,
            CancellationToken cancellationToken = default)
            => Task.FromResult(result);
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
