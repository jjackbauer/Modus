using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class TestsCiWorkflowRestoreBuildGateTests
{
    [Fact]
    [Trait("ChecklistItem", "Execute `dotnet restore` and `dotnet build --configuration Release --no-restore` against `Modus.slnx` before tests")]
    public void TestsWorkflow_GivenTestsJobDefinition_ExpectedRestoreAndReleaseBuildAgainstSolutionBeforeDotnetTest()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var runCommands = ParseRunCommands(workflow);

        var restoreIndex = FindCommandIndex(runCommands, "dotnet restore Modus.slnx");
        Assert.True(restoreIndex >= 0, "Expected tests workflow to include 'dotnet restore Modus.slnx'.");

        var buildIndex = FindCommandIndex(runCommands, "dotnet build Modus.slnx --configuration Release --no-restore");
        Assert.True(buildIndex >= 0, "Expected tests workflow to include release build command with --no-restore.");

        var firstTestIndex = FindFirstTestCommandIndex(runCommands);
        Assert.True(firstTestIndex >= 0, "Expected tests workflow to include a dotnet test command.");

        Assert.True(
            restoreIndex < buildIndex,
            "Expected restore to execute before build in tests workflow.");
        Assert.True(
            buildIndex < firstTestIndex,
            "Expected release build to execute before test command in tests workflow.");
    }

    [Fact]
    [Trait("ChecklistItem", "Execute `dotnet restore` and `dotnet build --configuration Release --no-restore` against `Modus.slnx` before tests")]
    public void TestsWorkflow_GivenRestoreAndBuildCommandChain_ExpectedStageBoundarySimulationPasses()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var runCommands = ParseRunCommands(workflow);

        var simulation = SimulateStageBoundaries(runCommands);

        Assert.True(
            simulation.Success,
            $"Expected restore/build stage boundaries to hold before tests, but failed at '{simulation.FailedCommand}'.");
    }

    [Fact]
    [Trait("ChecklistItem", "Execute `dotnet restore` and `dotnet build --configuration Release --no-restore` against `Modus.slnx` before tests")]
    public void TestsWorkflow_GivenWorkflowRunCommands_ExpectedExecutionEvidenceShowsRestoreThenBuildBeforeTestPhase()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var runCommands = ParseRunCommands(workflow);

        var evidence = CreateExecutionEvidence(runCommands);

        Assert.Contains("dotnet restore Modus.slnx", evidence.ExecutedCommands);
        Assert.Contains("dotnet build Modus.slnx --configuration Release --no-restore", evidence.ExecutedCommands);

        var releaseNoBuildTestCommands = evidence.ExecutedCommands
            .Where(static command => command.StartsWith("dotnet test ", StringComparison.Ordinal)
                && command.Contains(" --configuration Release", StringComparison.Ordinal)
                && command.Contains(" --no-build", StringComparison.Ordinal)
                && command.Contains(" --logger \"trx;LogFileName=", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains(
            releaseNoBuildTestCommands,
            static command => command.Contains("tests/Modus.Core.Tests/Modus.Core.Tests.csproj", StringComparison.Ordinal));
        Assert.Contains(
            releaseNoBuildTestCommands,
            static command => command.Contains("tests/Modus.Architecture.Tests/Modus.Architecture.Tests.csproj", StringComparison.Ordinal));
        Assert.Contains(
            releaseNoBuildTestCommands,
            static command => command.Contains("tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj", StringComparison.Ordinal));
        Assert.Equal("restore", evidence.Phases[0]);
        Assert.Equal("build", evidence.Phases[1]);
        Assert.Equal("test", evidence.Phases[2]);
    }

    [Fact]
    [Trait("ChecklistItem", "Execute `dotnet restore` and `dotnet build --configuration Release --no-restore` against `Modus.slnx` before tests")]
    public void TestsWorkflow_GivenRestoreCommandRemoved_ExpectedReleaseBuildNoRestoreSimulationFails()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var runCommands = ParseRunCommands(workflow)
            .Where(static command => !string.Equals(command, "dotnet restore Modus.slnx", StringComparison.Ordinal))
            .ToArray();

        var simulation = SimulateStageBoundaries(runCommands);

        Assert.False(simulation.Success);
        Assert.Equal("dotnet build Modus.slnx --configuration Release --no-restore", simulation.FailedCommand);
    }

    private static StageSimulationResult SimulateStageBoundaries(IReadOnlyCollection<string> runCommands)
    {
        var restored = false;
        var builtReleaseNoRestore = false;

        foreach (var command in runCommands)
        {
            if (string.Equals(command, "dotnet restore Modus.slnx", StringComparison.Ordinal))
            {
                restored = true;
                continue;
            }

            if (command.StartsWith("dotnet build ", StringComparison.Ordinal))
            {
                if (!string.Equals(command, "dotnet build Modus.slnx --configuration Release --no-restore", StringComparison.Ordinal))
                {
                    return new StageSimulationResult(false, command);
                }

                if (!restored)
                {
                    return new StageSimulationResult(false, command);
                }

                builtReleaseNoRestore = true;
                continue;
            }

            if (command.StartsWith("dotnet test ", StringComparison.Ordinal)
                && command.Contains(" --no-build", StringComparison.Ordinal)
                && !builtReleaseNoRestore)
            {
                return new StageSimulationResult(false, command);
            }
        }

        return new StageSimulationResult(true, null);
    }

    private static ExecutionEvidence CreateExecutionEvidence(IReadOnlyCollection<string> runCommands)
    {
        var phases = new List<string>();
        var executedCommands = new List<string>();

        foreach (var command in runCommands)
        {
            if (string.Equals(command, "dotnet restore Modus.slnx", StringComparison.Ordinal))
            {
                phases.Add("restore");
                executedCommands.Add(command);
                continue;
            }

            if (string.Equals(command, "dotnet build Modus.slnx --configuration Release --no-restore", StringComparison.Ordinal))
            {
                phases.Add("build");
                executedCommands.Add(command);
                continue;
            }

            if (command.StartsWith("dotnet test ", StringComparison.Ordinal)
                && command.Contains(" --configuration Release", StringComparison.Ordinal)
                && command.Contains(" --no-build", StringComparison.Ordinal))
            {
                phases.Add("test");
                executedCommands.Add(command);
            }
        }

        return new ExecutionEvidence(phases.ToArray(), executedCommands.ToArray());
    }

    private static int FindCommandIndex(IReadOnlyList<string> runCommands, string expectedCommand)
    {
        for (var index = 0; index < runCommands.Count; index++)
        {
            if (string.Equals(runCommands[index], expectedCommand, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindFirstTestCommandIndex(IReadOnlyList<string> runCommands)
    {
        for (var index = 0; index < runCommands.Count; index++)
        {
            if (runCommands[index].StartsWith("dotnet test ", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string[] ParseRunCommands(string workflow)
    {
        var steps = ParseWorkflowSteps(workflow);
        return steps
            .Select(static step => step.Run)
            .Where(static run => !string.IsNullOrWhiteSpace(run))
            .Select(static run => run!)
            .ToArray();
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

    private sealed record StageSimulationResult(bool Success, string? FailedCommand);

    private sealed record ExecutionEvidence(string[] Phases, string[] ExecutedCommands);
}
