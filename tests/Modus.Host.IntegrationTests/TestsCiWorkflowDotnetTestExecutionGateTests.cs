using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class TestsCiWorkflowDotnetTestExecutionGateTests
{
    private const string ChecklistItem = "Execute `dotnet test --configuration Release --no-build --logger trx` across unit and integration projects, and fail job on any failing test";

    private static readonly string[] ExpectedTestCommands =
    {
        "dotnet test tests/Modus.Core.Tests/Modus.Core.Tests.csproj --configuration Release --no-build --logger \"trx;LogFileName=Modus.Core.Tests.trx\"",
        "dotnet test tests/Modus.Architecture.Tests/Modus.Architecture.Tests.csproj --configuration Release --no-build --logger \"trx;LogFileName=Modus.Architecture.Tests.trx\"",
        "dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --configuration Release --no-build --logger \"trx;LogFileName=Modus.Host.IntegrationTests.trx\""
    };

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenTestsJobDefinition_ExpectedUnitAndIntegrationProjectsRunWithReleaseNoBuildAndTrxLogger()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var runCommands = ParseRunCommands(workflow);
        var testCommands = runCommands
            .Where(static command => command.StartsWith("dotnet test ", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(ExpectedTestCommands.Length, testCommands.Length);

        foreach (var expectedCommand in ExpectedTestCommands)
        {
            Assert.Contains(expectedCommand, testCommands);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenDotnetTestCommands_ExpectedEachCommandEnforcesReleaseNoBuildAndTrxContract()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var runCommands = ParseRunCommands(workflow);
        var testCommands = runCommands
            .Where(static command => command.StartsWith("dotnet test ", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(testCommands);

        foreach (var command in testCommands)
        {
            Assert.Contains(" --configuration Release", command, StringComparison.Ordinal);
            Assert.Contains(" --no-build", command, StringComparison.Ordinal);
            Assert.Contains(" --logger \"trx;LogFileName=", command, StringComparison.Ordinal);
            Assert.EndsWith(".trx\"", command, StringComparison.Ordinal);
            Assert.DoesNotContain("||", command, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenSingleFailingTestCommand_ExpectedFailurePropagatesToJobResult()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var runCommands = ParseRunCommands(workflow);

        var simulation = SimulateJobResult(
            runCommands,
            static command => !string.Equals(
                command,
                "dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --configuration Release --no-build --logger \"trx;LogFileName=Modus.Host.IntegrationTests.trx\"",
                StringComparison.Ordinal));

        Assert.False(simulation.Success);
        Assert.Equal(
            "dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --configuration Release --no-build --logger \"trx;LogFileName=Modus.Host.IntegrationTests.trx\"",
            simulation.FailedCommand);
    }

    private static JobSimulationResult SimulateJobResult(IReadOnlyCollection<string> runCommands, Func<string, bool> commandOutcome)
    {
        foreach (var command in runCommands)
        {
            if (command.StartsWith("dotnet test ", StringComparison.Ordinal) && !commandOutcome(command))
            {
                return new JobSimulationResult(false, command);
            }
        }

        return new JobSimulationResult(true, null);
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

                current = new WorkflowStep(Name: null, Run: null);
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
            case "run":
                step = step with { Run = value };
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

    private sealed record WorkflowStep(string? Name, string? Run);

    private sealed record JobSimulationResult(bool Success, string? FailedCommand);
}
