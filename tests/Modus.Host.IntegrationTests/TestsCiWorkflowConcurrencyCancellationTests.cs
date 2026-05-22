using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class TestsCiWorkflowConcurrencyCancellationTests
{
    [Fact]
    [Trait("ChecklistItem", "Add workflow-level concurrency cancellation to prevent stale commits from masking current behavior")]
    public void TestsWorkflow_GivenWorkflowLevelConcurrency_ExpectedCancellationEnabledForSupersededRuns()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var policy = ParseWorkflowLevelConcurrency(workflow);

        Assert.NotNull(policy);
        Assert.Equal("${{ github.workflow }}-${{ github.ref }}", policy!.Group);
        Assert.True(policy.CancelInProgress, "Expected workflow-level concurrency to cancel superseded runs.");
    }

    [Fact]
    [Trait("ChecklistItem", "Add workflow-level concurrency cancellation to prevent stale commits from masking current behavior")]
    public void TestsWorkflow_GivenSupersededCommitOnSameBranch_ExpectedOlderRunCancelledAndLatestRunContinues()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var policy = ParseWorkflowLevelConcurrency(workflow);

        Assert.NotNull(policy);

        var runStates = new List<RunState>();
        ScheduleRun(policy!, workflowName: "Tests CI", gitRef: "refs/heads/main", runId: "run-1", runStates);
        ScheduleRun(policy!, workflowName: "Tests CI", gitRef: "refs/heads/main", runId: "run-2", runStates);

        var run1 = Assert.Single(runStates, static state => state.RunId == "run-1");
        var run2 = Assert.Single(runStates, static state => state.RunId == "run-2");

        Assert.Equal("cancelled", run1.Conclusion);
        Assert.Equal("in_progress", run2.Conclusion);
        Assert.Equal(run1.ConcurrencyGroup, run2.ConcurrencyGroup);
    }

    private static ConcurrencyPolicy? ParseWorkflowLevelConcurrency(string workflow)
    {
        var normalized = workflow.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var inConcurrencyBlock = false;
        string? group = null;
        bool? cancelInProgress = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var indent = rawLine.Length - rawLine.TrimStart().Length;
            var trimmed = line.TrimStart();

            if (!inConcurrencyBlock)
            {
                if (indent == 0 && string.Equals(trimmed, "concurrency:", StringComparison.Ordinal))
                {
                    inConcurrencyBlock = true;
                }

                continue;
            }

            if (indent == 0)
            {
                break;
            }

            if (indent == 2 && trimmed.StartsWith("group:", StringComparison.Ordinal))
            {
                group = trimmed["group:".Length..].Trim();
                continue;
            }

            if (indent == 2 && trimmed.StartsWith("cancel-in-progress:", StringComparison.Ordinal))
            {
                var rawValue = trimmed["cancel-in-progress:".Length..].Trim();
                cancelInProgress = string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (group is null || cancelInProgress is null)
        {
            return null;
        }

        return new ConcurrencyPolicy(group, cancelInProgress.Value);
    }

    private static void ScheduleRun(
        ConcurrencyPolicy policy,
        string workflowName,
        string gitRef,
        string runId,
        ICollection<RunState> runStates)
    {
        var group = policy.Group
            .Replace("${{ github.workflow }}", workflowName, StringComparison.Ordinal)
            .Replace("${{ github.ref }}", gitRef, StringComparison.Ordinal);

        if (policy.CancelInProgress)
        {
            foreach (var state in runStates)
            {
                if (string.Equals(state.ConcurrencyGroup, group, StringComparison.Ordinal)
                    && string.Equals(state.Conclusion, "in_progress", StringComparison.Ordinal))
                {
                    state.Conclusion = "cancelled";
                }
            }
        }

        runStates.Add(new RunState(runId, group, "in_progress"));
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

    private sealed record ConcurrencyPolicy(string Group, bool CancelInProgress);

    private sealed class RunState
    {
        public RunState(string runId, string concurrencyGroup, string conclusion)
        {
            RunId = runId;
            ConcurrencyGroup = concurrencyGroup;
            Conclusion = conclusion;
        }

        public string RunId { get; }

        public string ConcurrencyGroup { get; }

        public string Conclusion { get; set; }
    }
}
