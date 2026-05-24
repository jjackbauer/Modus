using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class TestsCiWorkflowMigrationRegressionSuiteWiringTests
{
    private const string ChecklistItem = "Add migration regression suite wiring so behavior-proof tests execute in CI and fail fast on runtime regressions [depends on all preceding migration items]";
    private const string MigrationRegressionCommand = "dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --configuration Release --no-build --filter \"MigrationRegression=true\" --logger \"trx;LogFileName=MigrationRegressionSuite.trx\"";
    private const string CurlGateCommand = "dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --configuration Release --no-build --filter \"FullyQualifiedName~PluginEndpointCurlGateTests\" --logger \"trx;LogFileName=PluginEndpointCurlGateTests.trx\"";
    private const string FullIntegrationCommand = "dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --configuration Release --no-build --logger \"trx;LogFileName=Modus.Host.IntegrationTests.trx\"";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenTestsJobDefinition_ExpectedDedicatedMigrationRegressionGateCommandPresent()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var runCommands = ParseRunCommands(workflow);

        Assert.Contains(MigrationRegressionCommand, runCommands);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenMigrationRegressionGate_ExpectedGateRunsBeforeCurlProbeAndFullIntegrationSuite()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var runCommands = ParseRunCommands(workflow);

        var migrationIndex = Array.IndexOf(runCommands, MigrationRegressionCommand);
        var curlIndex = Array.IndexOf(runCommands, CurlGateCommand);
        var fullIntegrationIndex = Array.IndexOf(runCommands, FullIntegrationCommand);

        Assert.True(migrationIndex >= 0);
        Assert.True(curlIndex > migrationIndex);
        Assert.True(fullIntegrationIndex > migrationIndex);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenMigrationRegressionFailure_ExpectedLaterTestCommandsNotExecuted()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var runCommands = ParseRunCommands(workflow);

        var simulation = SimulateJobExecution(
            runCommands,
            command => !string.Equals(command, MigrationRegressionCommand, StringComparison.Ordinal));

        Assert.Equal(MigrationRegressionCommand, simulation.FailedCommand);
        Assert.DoesNotContain(CurlGateCommand, simulation.ExecutedCommandsAfterFailure);
        Assert.DoesNotContain(FullIntegrationCommand, simulation.ExecutedCommandsAfterFailure);
    }

    private static JobExecutionSimulation SimulateJobExecution(
        IReadOnlyCollection<string> runCommands,
        Func<string, bool> commandOutcome)
    {
        var executedCommands = new List<string>();
        string? failedCommand = null;

        foreach (var command in runCommands.Where(static command => command.StartsWith("dotnet test ", StringComparison.Ordinal)))
        {
            executedCommands.Add(command);

            if (!commandOutcome(command))
            {
                failedCommand = command;
                break;
            }
        }

        return new JobExecutionSimulation(failedCommand, executedCommands.ToArray());
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

    private sealed record JobExecutionSimulation(string? FailedCommand, IReadOnlyList<string> ExecutedCommandsAfterFailure);
}