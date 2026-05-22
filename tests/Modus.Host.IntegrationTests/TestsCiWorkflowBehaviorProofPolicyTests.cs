using System.Reflection;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class TestsCiWorkflowBehaviorProofPolicyTests
{
    private const string ChecklistItem = "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";

    private static readonly PlannedScenarioProofSpec[] PlannedScenarioProofs =
    {
        new(
            "TestsWorkflow_GivenPushToMain_ExpectedSingleTestsJobExecutesDotnetRestoreBuildAndTest",
            typeof(TestsCiWorkflowTriggerPolicyTests),
            nameof(TestsCiWorkflowTriggerPolicyTests.TestsWorkflow_GivenMainlineOrReviewPathEvent_ExpectedPolicySchedulesTestsJob),
            "ShouldRunForEvent("),
        new(
            "TestsWorkflow_GivenPullRequestEvent_ExpectedTestsJobRunsBeforeMergeEligibility",
            typeof(TestsCiWorkflowTriggerPolicyTests),
            nameof(TestsCiWorkflowTriggerPolicyTests.TestsWorkflow_GivenMainlineOrReviewPathEvent_ExpectedPolicySchedulesTestsJob),
            "ShouldRunForEvent("),
        new(
            "TestsWorkflow_GivenRunnerWithoutDotnet10_ExpectedSetupDotnetInstallsNet10BeforeBuild",
            typeof(TestsCiWorkflowDotnetSdkProvisioningTests),
            nameof(TestsCiWorkflowDotnetSdkProvisioningTests.TestsWorkflow_GivenRunnerWithoutDotnet10_ExpectedSetupDotnetProvisioningEnablesDotnetCommands),
            "SimulateRunnerExecution("),
        new(
            "TestsWorkflow_GivenSdkSetupFailure_ExpectedWorkflowFailsBeforeTestPhase",
            typeof(TestsCiWorkflowDotnetSdkProvisioningTests),
            nameof(TestsCiWorkflowDotnetSdkProvisioningTests.TestsWorkflow_GivenSdkVersionDowngraded_ExpectedDotnetCommandSimulationFails),
            "SimulateRunnerExecution("),
        new(
            "TestsWorkflow_GivenValidSolution_ExpectedRestoreAndBuildSucceedThenTestsExecuteNoBuild",
            typeof(TestsCiWorkflowRestoreBuildGateTests),
            nameof(TestsCiWorkflowRestoreBuildGateTests.TestsWorkflow_GivenRestoreAndBuildCommandChain_ExpectedStageBoundarySimulationPasses),
            "SimulateStageBoundaries("),
        new(
            "TestsWorkflow_GivenFailingTest_ExpectedJobConclusionFailedAndExitCodeNonZero",
            typeof(TestsCiWorkflowDotnetTestExecutionGateTests),
            nameof(TestsCiWorkflowDotnetTestExecutionGateTests.TestsWorkflow_GivenSingleFailingTestCommand_ExpectedFailurePropagatesToJobResult),
            "SimulateJobResult("),
        new(
            "TestsWorkflow_GivenMultipleTestProjects_ExpectedAllUnitAndIntegrationSuitesRun",
            typeof(TestsCiWorkflowDotnetTestExecutionGateTests),
            nameof(TestsCiWorkflowDotnetTestExecutionGateTests.TestsWorkflow_GivenSingleFailingTestCommand_ExpectedFailurePropagatesToJobResult),
            "SimulateJobResult("),
        new(
            "TestsWorkflow_GivenCompletedRun_ExpectedTrxArtifactsUploadedWithRunIdAssociation",
            typeof(TestsCiWorkflowFailureIsolationTests),
            nameof(TestsCiWorkflowFailureIsolationTests.TestsWorkflow_GivenPassingRun_ExpectedSuccessOnlyFlagAndArtifactPublish),
            "SimulateWorkflow("),
        new(
            "TestsWorkflow_GivenTestFailure_ExpectedFailureLogsPersistedForRootCauseAnalysis",
            typeof(TestsCiWorkflowArtifactUploadTests),
            nameof(TestsCiWorkflowArtifactUploadTests.TestsWorkflow_GivenTestFailure_ExpectedArtifactUploadConditionStillEvaluatesTrue),
            "SimulateArtifactUploadEligibility("),
        new(
            "TestsWorkflow_GivenSupersededCommitOnSameBranch_ExpectedOlderRunCancelledAndLatestRunContinues",
            typeof(TestsCiWorkflowConcurrencyCancellationTests),
            nameof(TestsCiWorkflowConcurrencyCancellationTests.TestsWorkflow_GivenSupersededCommitOnSameBranch_ExpectedOlderRunCancelledAndLatestRunContinues),
            "ScheduleRun("),
        new(
            "TestsWorkflow_GivenFailedRun_ExpectedNoSuccessStateLeakToSubsequentChecks",
            typeof(TestsCiWorkflowFailureIsolationTests),
            nameof(TestsCiWorkflowFailureIsolationTests.TestsWorkflow_GivenFailingDotnetTest_ExpectedFailedConclusionAndNoSuccessOnlyPublications),
            "SimulateWorkflow("),
    };

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void CiTestPlan_GivenEachChecklistItem_ExpectedAtLeastOneExecutableBehaviorProofAssertion()
    {
        foreach (var plannedScenario in PlannedScenarioProofs)
        {
            var method = plannedScenario.TestType.GetMethod(plannedScenario.TestMethodName, BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(method);

            var checklistTraits = method!.CustomAttributes
                .Where(static attribute => attribute.AttributeType == typeof(TraitAttribute))
                .Select(attribute => new
                {
                    Name = attribute.ConstructorArguments.Count > 0 ? attribute.ConstructorArguments[0].Value as string : null,
                    Value = attribute.ConstructorArguments.Count > 1 ? attribute.ConstructorArguments[1].Value as string : null,
                })
                .Where(static trait => string.Equals(trait.Name, "ChecklistItem", StringComparison.Ordinal))
                .ToArray();

            Assert.NotEmpty(checklistTraits);
            Assert.All(checklistTraits, static trait => Assert.False(string.IsNullOrWhiteSpace(trait.Value)));

            var sourceText = ReadSourceForTestType(plannedScenario.TestType);
            var methodBody = ExtractMethodBody(sourceText, plannedScenario.TestMethodName);

            Assert.Contains(
                plannedScenario.RuntimeEvidenceToken,
                methodBody,
                StringComparison.Ordinal);
            Assert.Contains("Assert.", methodBody, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void CiTestPlan_GivenIntegrationFocusedAssertions_ExpectedNoMetadataOnlyCoverage()
    {
        foreach (var plannedScenario in PlannedScenarioProofs)
        {
            var sourceText = ReadSourceForTestType(plannedScenario.TestType);
            var methodBody = ExtractMethodBody(sourceText, plannedScenario.TestMethodName);

            var hasMetadataRead = methodBody.Contains("ReadRepositoryFile(", StringComparison.Ordinal)
                || methodBody.Contains("Parse", StringComparison.Ordinal);
            var hasRuntimeProbe = methodBody.Contains(plannedScenario.RuntimeEvidenceToken, StringComparison.Ordinal);

            Assert.False(
                hasMetadataRead && !hasRuntimeProbe,
                $"Planned scenario '{plannedScenario.PlannedScenarioName}' regressed to metadata-only assertions.");
        }
    }

    private static string ReadSourceForTestType(Type testType)
    {
        var fileName = testType.Name + ".cs";
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "Modus.slnx");
            if (File.Exists(solutionPath))
            {
                var sourcePath = Path.Combine(directory.FullName, "tests", "Modus.Host.IntegrationTests", fileName);
                if (File.Exists(sourcePath))
                {
                    return File.ReadAllText(sourcePath);
                }

                throw new InvalidOperationException($"Could not locate source file '{sourcePath}'.");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Modus.slnx.");
    }

    private static string ExtractMethodBody(string sourceText, string methodName)
    {
        var signatureIndex = sourceText.IndexOf(methodName + "(", StringComparison.Ordinal);
        if (signatureIndex < 0)
        {
            throw new InvalidOperationException($"Could not locate method '{methodName}'.");
        }

        var openBraceIndex = sourceText.IndexOf('{', signatureIndex);
        if (openBraceIndex < 0)
        {
            throw new InvalidOperationException($"Could not locate opening brace for method '{methodName}'.");
        }

        var depth = 0;
        for (var index = openBraceIndex; index < sourceText.Length; index++)
        {
            if (sourceText[index] == '{')
            {
                depth++;
                continue;
            }

            if (sourceText[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return sourceText.Substring(openBraceIndex, index - openBraceIndex + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not locate closing brace for method '{methodName}'.");
    }

    private sealed record PlannedScenarioProofSpec(
        string PlannedScenarioName,
        Type TestType,
        string TestMethodName,
        string RuntimeEvidenceToken);
}