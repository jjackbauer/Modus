using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class TestsCiWorkflowTriggerPolicyTests
{
    [Theory]
    [InlineData("push", "main")]
    [InlineData("pull_request", "main")]
    [Trait("ChecklistItem", "Configure workflow triggers for `push` and `pull_request` to ensure test execution on mainline and review paths")]
    public void TestsWorkflow_GivenMainlineOrReviewPathEvent_ExpectedPolicySchedulesTestsJob(string eventName, string targetBranch)
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var policy = ParseTriggerPolicy(workflow);

        var shouldRun = ShouldRunForEvent(policy, eventName, targetBranch);

        Assert.True(
            shouldRun,
            $"Expected workflow trigger policy to run for event '{eventName}' targeting branch '{targetBranch}'.");
    }

    [Theory]
    [InlineData("push", "feature/foo")]
    [InlineData("pull_request", "release/1.0")]
    [InlineData("workflow_dispatch", "main")]
    [Trait("ChecklistItem", "Configure workflow triggers for `push` and `pull_request` to ensure test execution on mainline and review paths")]
    public void TestsWorkflow_GivenNonConfiguredPathEvent_ExpectedPolicyDoesNotScheduleTestsJob(string eventName, string targetBranch)
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var policy = ParseTriggerPolicy(workflow);

        var shouldRun = ShouldRunForEvent(policy, eventName, targetBranch);

        Assert.False(
            shouldRun,
            $"Expected workflow trigger policy not to run for event '{eventName}' targeting branch '{targetBranch}'.");
    }

    [Fact]
    [Trait("ChecklistItem", "Configure workflow triggers for `push` and `pull_request` to ensure test execution on mainline and review paths")]
    public void TestsWorkflow_GivenTriggerPolicy_ExpectedOnlyPushAndPullRequestTargetMainline()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var policy = ParseTriggerPolicy(workflow);

        Assert.Equal(new[] { "pull_request", "push" }, policy.Keys.OrderBy(static key => key, StringComparer.Ordinal));
        Assert.Equal(new[] { "main" }, policy["push"]);
        Assert.Equal(new[] { "main" }, policy["pull_request"]);
    }

    private static Dictionary<string, IReadOnlyList<string>> ParseTriggerPolicy(string workflow)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var normalized = workflow.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var inOnBlock = false;
        string? currentEvent = null;
        var collectingBranches = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var indent = rawLine.Length - rawLine.TrimStart().Length;
            var trimmed = line.TrimStart();

            if (!inOnBlock)
            {
                if (indent == 0 && string.Equals(trimmed, "on:", StringComparison.Ordinal))
                {
                    inOnBlock = true;
                }

                continue;
            }

            if (indent == 0)
            {
                break;
            }

            if (indent == 2 && trimmed.EndsWith(':'))
            {
                currentEvent = trimmed[..^1];
                collectingBranches = false;
                result[currentEvent] = Array.Empty<string>();
                continue;
            }

            if (indent == 4 && string.Equals(trimmed, "branches:", StringComparison.Ordinal))
            {
                collectingBranches = true;
                continue;
            }

            if (collectingBranches && indent == 6 && trimmed.StartsWith("- ", StringComparison.Ordinal) && currentEvent is not null)
            {
                var branch = trimmed[2..].Trim();
                var existing = result[currentEvent];
                result[currentEvent] = existing.Concat(new[] { branch }).ToArray();
            }
        }

        return result
            .Where(static pair => pair.Key is "push" or "pull_request")
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
    }

    private static bool ShouldRunForEvent(Dictionary<string, IReadOnlyList<string>> policy, string eventName, string targetBranch)
    {
        if (!policy.TryGetValue(eventName, out var branches))
        {
            return false;
        }

        if (branches.Count == 0)
        {
            return true;
        }

        return branches.Contains(targetBranch, StringComparer.Ordinal);
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
}