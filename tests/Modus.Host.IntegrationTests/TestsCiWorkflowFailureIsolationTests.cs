using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class TestsCiWorkflowFailureIsolationTests
{
    private const string ChecklistItem = "Verify CI failure isolation: when tests fail, the workflow reports failed conclusion and does not publish success-only artifacts/flags";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenFailingDotnetTest_ExpectedFailedConclusionAndNoSuccessOnlyPublications()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var steps = ParseWorkflowSteps(workflow);

        var simulation = SimulateWorkflow(
            steps,
            static runCommand => !string.Equals(
                runCommand,
                "dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --configuration Release --no-build --logger \"trx;LogFileName=Modus.Host.IntegrationTests.trx\"",
                StringComparison.Ordinal));

        Assert.Equal("failure", simulation.Conclusion);
        Assert.False(simulation.SuccessFlagPublished);
        Assert.False(simulation.SuccessArtifactUploaded);
        Assert.True(simulation.TestResultsArtifactUploaded);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenPassingRun_ExpectedSuccessOnlyFlagAndArtifactPublish()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var steps = ParseWorkflowSteps(workflow);

        var simulation = SimulateWorkflow(
            steps,
            static _ => true);

        Assert.Equal("success", simulation.Conclusion);
        Assert.True(simulation.SuccessFlagPublished);
        Assert.True(simulation.SuccessArtifactUploaded);
        Assert.True(simulation.TestResultsArtifactUploaded);
    }

    private static WorkflowSimulationResult SimulateWorkflow(
        IReadOnlyCollection<WorkflowStep> steps,
        Func<string, bool> runStepOutcome)
    {
        var priorStepsSucceeded = true;
        var conclusion = "success";
        var successFlagPublished = false;
        var successArtifactUploaded = false;
        var testResultsArtifactUploaded = false;

        foreach (var step in steps)
        {
            if (!ShouldRun(step.If, priorStepsSucceeded))
            {
                continue;
            }

            if (step.Run is not null)
            {
                var succeeded = runStepOutcome(step.Run);
                if (string.Equals(step.Name, "Publish CI success flag", StringComparison.Ordinal))
                {
                    successFlagPublished = succeeded;
                }

                if (!succeeded)
                {
                    priorStepsSucceeded = false;
                    conclusion = "failure";
                }

                continue;
            }

            if (IsUploadArtifactStep(step) && step.ArtifactName is not null)
            {
                if (string.Equals(step.ArtifactName, "ci-test-results-${{ github.run_id }}", StringComparison.Ordinal))
                {
                    testResultsArtifactUploaded = true;
                }

                if (string.Equals(step.ArtifactName, "ci-tests-success-${{ github.run_id }}", StringComparison.Ordinal))
                {
                    successArtifactUploaded = true;
                }
            }
        }

        return new WorkflowSimulationResult(conclusion, successFlagPublished, successArtifactUploaded, testResultsArtifactUploaded);
    }

    private static bool ShouldRun(string? condition, bool priorSuccess)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return priorSuccess;
        }

        if (string.Equals(condition, "always()", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(condition, "success()", StringComparison.Ordinal))
        {
            return priorSuccess;
        }

        return false;
    }

    private static bool IsUploadArtifactStep(WorkflowStep step)
    {
        return step.Uses is not null
            && step.Uses.StartsWith("actions/upload-artifact@", StringComparison.Ordinal);
    }

    private static WorkflowStep[] ParseWorkflowSteps(string workflow)
    {
        var normalized = workflow.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var steps = new List<WorkflowStep>();

        var inTestsJob = false;
        var inSteps = false;
        WorkflowStep? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var indent = rawLine.Length - rawLine.TrimStart().Length;
            var trimmed = line.TrimStart();

            if (!inTestsJob)
            {
                if (indent == 2 && string.Equals(trimmed, "tests:", StringComparison.Ordinal))
                {
                    inTestsJob = true;
                }

                continue;
            }

            if (!inSteps)
            {
                if (indent == 4 && string.Equals(trimmed, "steps:", StringComparison.Ordinal))
                {
                    inSteps = true;
                }

                continue;
            }

            if (indent <= 4)
            {
                if (current is not null)
                {
                    steps.Add(current);
                }

                break;
            }

            if (indent == 6 && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    steps.Add(current);
                }

                current = new WorkflowStep(Name: null, If: null, Uses: null, Run: null, ArtifactName: null, InWithBlock: false);
                var remainder = trimmed[2..];
                TryApplyStepProperty(ref current, remainder);
                continue;
            }

            if (current is not null)
            {
                TryApplyStepProperty(ref current, trimmed);
            }
        }

        if (current is not null)
        {
            steps.Add(current);
        }

        return steps.ToArray();
    }

    private static void TryApplyStepProperty(ref WorkflowStep step, string propertyLine)
    {
        var separatorIndex = propertyLine.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return;
        }

        var key = propertyLine[..separatorIndex].Trim();
        var value = propertyLine[(separatorIndex + 1)..].Trim();

        switch (key)
        {
            case "name":
                if (step.InWithBlock && IsUploadArtifactStep(step))
                {
                    step = step with { ArtifactName = value };
                }
                else
                {
                    step = step with { Name = value, InWithBlock = false };
                }

                break;
            case "if":
                step = step with { If = value, InWithBlock = false };
                break;
            case "uses":
                step = step with { Uses = value, InWithBlock = false };
                break;
            case "run":
                step = step with { Run = value, InWithBlock = false };
                break;
            case "with":
                step = step with { InWithBlock = true };
                break;
            default:
                break;
        }
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "Modus.slnx");
            var filePath = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(solutionPath) && File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate repository root containing Modus.slnx and {relativePath}.");
    }

    private sealed record WorkflowStep(
        string? Name,
        string? If,
        string? Uses,
        string? Run,
        string? ArtifactName,
        bool InWithBlock);

    private sealed record WorkflowSimulationResult(
        string Conclusion,
        bool SuccessFlagPublished,
        bool SuccessArtifactUploaded,
        bool TestResultsArtifactUploaded);
}
