using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class TestsCiWorkflowDotnetSdkProvisioningTests
{
    [Fact]
    [Trait("ChecklistItem", "Add `actions/setup-dotnet` for `net10.0.x` to guarantee runtime-compatible SDK provisioning before test commands")]
    public void TestsWorkflow_GivenRunnerWithoutDotnet10_ExpectedSetupDotnetProvisioningEnablesDotnetCommands()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var steps = ParseWorkflowSteps(workflow);

        var setupStep = Assert.Single(steps, IsSetupDotnetStep);
        Assert.Equal("net10.0.x", setupStep.DotnetVersion);

        var simulation = SimulateRunnerExecution(
            steps,
            startsWithNet10Sdk: false,
            injectedCommands: Array.Empty<string>());

        Assert.True(
            simulation.Success,
            $"Expected setup-dotnet provisioning to enable dotnet command execution, but failed at '{simulation.FailedCommand}'.");
    }

    [Fact]
    [Trait("ChecklistItem", "Add `actions/setup-dotnet` for `net10.0.x` to guarantee runtime-compatible SDK provisioning before test commands")]
    public void TestsWorkflow_GivenSdkVersionDowngraded_ExpectedDotnetCommandSimulationFails()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var steps = ParseWorkflowSteps(workflow);

        var downgraded = steps
            .Select(static step => IsSetupDotnetStep(step)
                ? step with { DotnetVersion = "net9.0.x" }
                : step)
            .ToArray();

        var simulation = SimulateRunnerExecution(
            downgraded,
            startsWithNet10Sdk: false,
            injectedCommands: new[] { "dotnet --version" });

        Assert.False(simulation.Success);
        Assert.Equal("dotnet --version", simulation.FailedCommand);
    }

    [Fact]
    [Trait("ChecklistItem", "Add `actions/setup-dotnet` for `net10.0.x` to guarantee runtime-compatible SDK provisioning before test commands")]
    public void TestsWorkflow_GivenConfiguredDotnetRunSteps_ExpectedSetupDotnetAppearsBeforeFirstDotnetCommand()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var steps = ParseWorkflowSteps(workflow);

        var setupIndex = Array.FindIndex(steps, IsSetupDotnetStep);
        Assert.True(setupIndex >= 0, "Expected workflow to include an actions/setup-dotnet step.");

        var firstDotnetCommandIndex = Array.FindIndex(steps, static step => IsDotnetRunCommand(step.Run));
        Assert.True(firstDotnetCommandIndex >= 0, "Expected workflow to include at least one dotnet command step.");
        Assert.True(
            setupIndex < firstDotnetCommandIndex,
            "Expected actions/setup-dotnet to run before the first dotnet command step.");
    }

    private static SimulationResult SimulateRunnerExecution(
        IReadOnlyCollection<WorkflowStep> steps,
        bool startsWithNet10Sdk,
        IReadOnlyCollection<string> injectedCommands)
    {
        var hasNet10Sdk = startsWithNet10Sdk;

        foreach (var step in steps)
        {
            if (IsSetupDotnetStep(step) && string.Equals(step.DotnetVersion, "net10.0.x", StringComparison.Ordinal))
            {
                hasNet10Sdk = true;
            }

            if (IsDotnetRunCommand(step.Run) && !hasNet10Sdk)
            {
                return new SimulationResult(false, step.Run);
            }
        }

        foreach (var command in injectedCommands)
        {
            if (IsDotnetRunCommand(command) && !hasNet10Sdk)
            {
                return new SimulationResult(false, command);
            }
        }

        return new SimulationResult(true, null);
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

                current = new WorkflowStep(Name: null, Uses: null, DotnetVersion: null, Run: null);
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
                step = step with { Name = value };
                break;
            case "uses":
                step = step with { Uses = value };
                break;
            case "run":
                step = step with { Run = value };
                break;
            case "dotnet-version":
                step = step with { DotnetVersion = value };
                break;
        }
    }

    private static bool IsSetupDotnetStep(WorkflowStep step)
    {
        return step.Uses is not null
            && step.Uses.StartsWith("actions/setup-dotnet@", StringComparison.Ordinal);
    }

    private static bool IsDotnetRunCommand(string? command)
    {
        return command is not null
            && command.StartsWith("dotnet ", StringComparison.Ordinal);
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

    private sealed record WorkflowStep(string? Name, string? Uses, string? DotnetVersion, string? Run);

    private sealed record SimulationResult(bool Success, string? FailedCommand);
}
