using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class TestsCiWorkflowArtifactUploadTests
{
    private const string ChecklistItem = "Upload test-result artifacts (TRX and relevant logs) using `actions/upload-artifact` so runtime evidence is retained for diagnosis";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenTestsJobDefinition_ExpectedUploadArtifactStepCollectsTrxAndRelevantLogs()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));

        Assert.Contains("- name: Upload test results and logs", workflow, StringComparison.Ordinal);
        Assert.Contains("if: always()", workflow, StringComparison.Ordinal);
        Assert.Contains("name: ci-test-results-${{ github.run_id }}", workflow, StringComparison.Ordinal);
        Assert.Contains("if-no-files-found: warn", workflow, StringComparison.Ordinal);
        Assert.Contains("tests/**/TestResults/**/*.trx", workflow, StringComparison.Ordinal);
        Assert.Contains("tests/**/TestResults/**/*.log", workflow, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenTestsAndArtifactSteps_ExpectedArtifactUploadRunsAfterDotnetTestCommands()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var normalized = workflow.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);

        var uploadLineIndex = Array.FindIndex(lines, static line =>
            string.Equals(line.Trim(), "- name: Upload test results and logs", StringComparison.Ordinal));
        Assert.True(uploadLineIndex >= 0, "Expected workflow to include actions/upload-artifact step for test results.");

        var lastTestLineIndex = FindLastIndex(lines, static line =>
            line.TrimStart().StartsWith("run: dotnet test ", StringComparison.Ordinal));

        Assert.True(lastTestLineIndex >= 0, "Expected workflow to include at least one dotnet test command.");
        Assert.True(uploadLineIndex > lastTestLineIndex, "Expected artifact upload step to run after all dotnet test commands.");
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenTestFailure_ExpectedArtifactUploadConditionStillEvaluatesTrue()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var steps = ParseWorkflowSteps(workflow);

        var simulation = SimulateArtifactUploadEligibility(
            steps,
            static step => !(step.Run?.Contains("Modus.Host.IntegrationTests", StringComparison.Ordinal) ?? false));

        Assert.True(simulation.UploadAttempted);
        Assert.False(simulation.JobSucceeded);
        Assert.Equal("dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --configuration Release --no-build --logger \"trx;LogFileName=Modus.Host.IntegrationTests.trx\"", simulation.FailedCommand);
    }

    private static SimulationResult SimulateArtifactUploadEligibility(
        IReadOnlyCollection<WorkflowStep> steps,
        Func<WorkflowStep, bool> runStepOutcome)
    {
        var jobSucceeded = true;
        string? failedCommand = null;
        var uploadAttempted = false;

        foreach (var step in steps)
        {
            if (step.Run is not null)
            {
                var success = runStepOutcome(step);
                if (!success && failedCommand is null)
                {
                    failedCommand = step.Run;
                }

                if (!success)
                {
                    jobSucceeded = false;
                }
            }

            if (IsUploadArtifactStep(step) && ShouldRun(step.If, jobSucceeded))
            {
                uploadAttempted = true;
            }
        }

        return new SimulationResult(uploadAttempted, jobSucceeded, failedCommand);
    }

    private static bool ShouldRun(string? condition, bool priorSuccess)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return priorSuccess;
        }

        return string.Equals(condition, "always()", StringComparison.Ordinal);
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

                current = new WorkflowStep(Name: null, If: null, Uses: null, Run: null, ArtifactName: null, IfNoFilesFound: null, PathGlobs: Array.Empty<string>(), InPathBlock: false);
                var remainder = trimmed[2..];
                TryApplyStepProperty(ref current, remainder);
                continue;
            }

            if (current is not null)
            {
                if (current.InPathBlock && !trimmed.Contains(':', StringComparison.Ordinal))
                {
                    var globs = current.PathGlobs.ToList();
                    globs.Add(trimmed);
                    current = current with { PathGlobs = globs.ToArray() };
                    continue;
                }

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
        if (propertyLine.StartsWith("- ", StringComparison.Ordinal))
        {
            var pathValue = propertyLine[2..].Trim();
            if (pathValue.Length > 0)
            {
                var globs = step.PathGlobs.ToList();
                globs.Add(pathValue);
                step = step with { PathGlobs = globs.ToArray() };
            }

            return;
        }

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
                step = step with { Name = value, InPathBlock = false };
                if (step.Uses is not null && step.Uses.StartsWith("actions/upload-artifact@", StringComparison.Ordinal))
                {
                    step = step with { ArtifactName = value };
                }

                break;
            case "if":
                step = step with { If = value, InPathBlock = false };
                break;
            case "uses":
                step = step with { Uses = value, InPathBlock = false };
                break;
            case "run":
                step = step with { Run = value, InPathBlock = false };
                break;
            case "path":
                step = step with { PathGlobs = Array.Empty<string>(), InPathBlock = string.Equals(value, "|", StringComparison.Ordinal) };
                break;
            case "if-no-files-found":
                step = step with { IfNoFilesFound = value, InPathBlock = false };
                break;
        }
    }

    private static bool IsUploadArtifactStep(WorkflowStep step)
    {
        return step.Uses is not null
            && step.Uses.StartsWith("actions/upload-artifact@", StringComparison.Ordinal);
    }

    private static int FindLastIndex(IReadOnlyList<string> lines, Predicate<string> predicate)
    {
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            if (predicate(lines[index]))
            {
                return index;
            }
        }

        return -1;
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
        string? IfNoFilesFound,
        string[] PathGlobs,
        bool InPathBlock);

    private sealed record SimulationResult(bool UploadAttempted, bool JobSucceeded, string? FailedCommand);
}