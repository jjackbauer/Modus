using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class TestsCiWorkflowLinuxFirstWipGateTests
{
    private const string ChecklistItem = "Add Linux-first CI execution gate for Wip.* build/test matrix to satisfy cross-platform MVP baseline with deterministic artifact retention on failures [depends on NFR cross-platform support]";

    private static readonly string[] ExpectedWipTestProjects =
    {
        "tests/Wip.Abstractions.Tests/Wip.Abstractions.Tests.csproj",
        "tests/Wip.Artifacts.Local.Tests/Wip.Artifacts.Local.Tests.csproj",
        "tests/Wip.Builder.Tests/Wip.Builder.Tests.csproj",
        "tests/Wip.Modus.Tests/Wip.Modus.Tests.csproj",
        "tests/Wip.Policy.LocalSafe.Tests/Wip.Policy.LocalSafe.Tests.csproj",
        "tests/Wip.Runtime.Tests/Wip.Runtime.Tests.csproj",
        "tests/Wip.Shell.Tests/Wip.Shell.Tests.csproj",
        "tests/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj",
        "tests/Wip.ShellHost.Tests/Wip.ShellHost.Tests.csproj",
        "tests/Wip.Tools.Shell.Tests/Wip.Tools.Shell.Tests.csproj",
        "tests/Wip.Validation.DotNet.Tests/Wip.Validation.DotNet.Tests.csproj",
        "tests/Wip.Workspaces.Git.Tests/Wip.Workspaces.Git.Tests.csproj"
    };

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenLinuxGateJob_ExpectedUbuntuMatrixCoversWipBuildAndTestTargets()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var linuxGate = ParseJob(workflow, "wip_linux_gate");

        Assert.NotNull(linuxGate);
        Assert.Equal("ubuntu-latest", linuxGate!.RunsOn);
        Assert.Equal("tests/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj", linuxGate.MatrixProjects.Single(static value => value.Contains("Wip.Shell.E2E.Tests", StringComparison.Ordinal)));

        foreach (var expectedProject in ExpectedWipTestProjects)
        {
            Assert.Contains(expectedProject, linuxGate.MatrixProjects);
        }

        var buildStep = Assert.Single(linuxGate.Steps, static step => string.Equals(step.Name, "Build WIP solution (Release, no restore)", StringComparison.Ordinal));
        Assert.Equal("dotnet build Modus.slnx --configuration Release --no-restore", buildStep.RunTemplate);

        var testStep = Assert.Single(linuxGate.Steps, static step => string.Equals(step.Name, "Test WIP matrix project (Release, no build, TRX)", StringComparison.Ordinal));
        Assert.Equal("dotnet test ${{ matrix.test_project }} --configuration Release --no-build --logger \"trx;LogFileName=${{ matrix.test_project_slug }}.trx\"", testStep.RunTemplate);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenDefaultTestsJob_ExpectedLinuxGateCompletesBeforeLegacyTestsJobStarts()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var testsJob = ParseJob(workflow, "tests");

        Assert.NotNull(testsJob);

        var ordering = SimulateJobGateOrdering(testsJob!);

        Assert.True(ordering.LinuxGateIsRequired, "Expected 'tests' job to declare dependency on 'wip_linux_gate'.");
        Assert.Equal("wip_linux_gate", ordering.RequiredJob);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenFailingLinuxMatrixEntry_ExpectedDeterministicArtifactRetentionStillRuns()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));
        var linuxGate = ParseJob(workflow, "wip_linux_gate");

        Assert.NotNull(linuxGate);

        var simulation = SimulateLinuxMatrixEntryExecution(
            linuxGate!,
            testProject: "tests/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj",
            runOutcome: static runCommand => !runCommand.StartsWith("dotnet test tests/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj", StringComparison.Ordinal));

        Assert.False(simulation.JobSucceeded);
        Assert.Equal("dotnet test tests/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj --configuration Release --no-build --logger \"trx;LogFileName=Wip.Shell.E2E.Tests.trx\"", simulation.FailedCommand);
        Assert.True(simulation.ArtifactUploaded);
        Assert.Equal("wip-linux-first-Wip.Shell.E2E.Tests-${{ github.run_id }}", simulation.ArtifactName);
        Assert.Equal("artifacts/ci/wip/linux/Wip.Shell.E2E.Tests", simulation.ArtifactPath);
    }

    private static LinuxGateOrderingResult SimulateJobGateOrdering(WorkflowJob testsJob)
    {
        var linuxGateRequired = testsJob.Needs.Any(static dependency => string.Equals(dependency, "wip_linux_gate", StringComparison.Ordinal));
        var requiredJob = linuxGateRequired ? "wip_linux_gate" : string.Empty;
        return new LinuxGateOrderingResult(linuxGateRequired, requiredJob);
    }

    private static LinuxMatrixSimulationResult SimulateLinuxMatrixEntryExecution(
        WorkflowJob linuxGate,
        string testProject,
        Func<string, bool> runOutcome)
    {
        var testProjectSlug = GetProjectSlug(testProject);
        var priorSuccess = true;
        var artifactUploaded = false;
        string? failedCommand = null;
        string? artifactName = null;
        string? artifactPath = null;

        foreach (var step in linuxGate.Steps)
        {
            if (!ShouldRunStep(step.IfCondition, priorSuccess))
            {
                continue;
            }

            if (step.RunTemplate is not null)
            {
                var resolvedCommand = ResolveTemplate(step.RunTemplate, testProject, testProjectSlug);
                var succeeded = runOutcome(resolvedCommand);
                if (!succeeded)
                {
                    priorSuccess = false;
                    failedCommand ??= resolvedCommand;
                }

                continue;
            }

            if (step.UsesArtifactUpload)
            {
                artifactUploaded = true;
                artifactName = ResolveTemplate(step.ArtifactNameTemplate ?? string.Empty, testProject, testProjectSlug);
                artifactPath = ResolveTemplate(step.ArtifactPathTemplate ?? string.Empty, testProject, testProjectSlug);
            }
        }

        return new LinuxMatrixSimulationResult(
            JobSucceeded: priorSuccess,
            FailedCommand: failedCommand,
            ArtifactUploaded: artifactUploaded,
            ArtifactName: artifactName,
            ArtifactPath: artifactPath);
    }

    private static bool ShouldRunStep(string? condition, bool priorSuccess)
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

    private static string ResolveTemplate(string input, string testProject, string testProjectSlug)
    {
        return input
            .Replace("${{ matrix.test_project }}", testProject, StringComparison.Ordinal)
            .Replace("${{ matrix.test_project_slug }}", testProjectSlug, StringComparison.Ordinal);
    }

    private static string GetProjectSlug(string testProject)
    {
        var fileName = Path.GetFileNameWithoutExtension(testProject);
        return fileName;
    }

    private static WorkflowJob? ParseJob(string workflow, string jobName)
    {
        var normalized = workflow.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);

        var inJobs = false;
        var inTargetJob = false;
        var inSteps = false;
        var inMatrix = false;
        var inTestProjectMatrix = false;

        var runsOn = string.Empty;
        var needs = new List<string>();
        var matrixProjects = new List<string>();
        var steps = new List<WorkflowStep>();
        WorkflowStep? currentStep = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var indent = rawLine.Length - rawLine.TrimStart().Length;
            var trimmed = line.TrimStart();

            if (!inJobs)
            {
                if (indent == 0 && string.Equals(trimmed, "jobs:", StringComparison.Ordinal))
                {
                    inJobs = true;
                }

                continue;
            }

            if (!inTargetJob)
            {
                if (indent == 2 && string.Equals(trimmed, jobName + ":", StringComparison.Ordinal))
                {
                    inTargetJob = true;
                }

                continue;
            }

            if (indent == 2)
            {
                if (currentStep is not null)
                {
                    steps.Add(currentStep);
                }

                break;
            }

            if (indent == 4 && trimmed.StartsWith("runs-on:", StringComparison.Ordinal))
            {
                runsOn = trimmed["runs-on:".Length..].Trim();
                continue;
            }

            if (indent == 4 && trimmed.StartsWith("needs:", StringComparison.Ordinal))
            {
                var value = trimmed["needs:".Length..].Trim();
                if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal) && value.Length > 2)
                {
                    var rawDependencies = value[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var dependency in rawDependencies)
                    {
                        needs.Add(dependency.Trim());
                    }
                }

                continue;
            }

            if (indent == 4 && string.Equals(trimmed, "strategy:", StringComparison.Ordinal))
            {
                inMatrix = false;
                inTestProjectMatrix = false;
                continue;
            }

            if (indent == 6 && string.Equals(trimmed, "matrix:", StringComparison.Ordinal))
            {
                inMatrix = true;
                inTestProjectMatrix = false;
                continue;
            }

            if (inMatrix && indent == 8 && string.Equals(trimmed, "test_project:", StringComparison.Ordinal))
            {
                inTestProjectMatrix = true;
                continue;
            }

            if (inMatrix && indent == 8 && string.Equals(trimmed, "include:", StringComparison.Ordinal))
            {
                inTestProjectMatrix = false;
                continue;
            }

            if (inMatrix
                && inTestProjectMatrix
                && indent == 10
                && trimmed.StartsWith("- tests/", StringComparison.Ordinal))
            {
                matrixProjects.Add(trimmed[2..].Trim());
                continue;
            }

            if (indent == 4 && string.Equals(trimmed, "steps:", StringComparison.Ordinal))
            {
                inSteps = true;
                continue;
            }

            if (inSteps && indent == 6 && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                if (currentStep is not null)
                {
                    steps.Add(currentStep);
                }

                currentStep = new WorkflowStep(Name: null, IfCondition: null, RunTemplate: null, UsesArtifactUpload: false, ArtifactNameTemplate: null, ArtifactPathTemplate: null, InPathBlock: false);
                ApplyStepProperty(ref currentStep, trimmed[2..]);
                continue;
            }

            if (inSteps && currentStep is not null && indent >= 8)
            {
                if (currentStep.InPathBlock && !trimmed.Contains(':', StringComparison.Ordinal))
                {
                    currentStep = currentStep with
                    {
                        ArtifactPathTemplate = string.IsNullOrEmpty(currentStep.ArtifactPathTemplate)
                            ? trimmed
                            : currentStep.ArtifactPathTemplate + "\n" + trimmed,
                    };

                    continue;
                }

                ApplyStepProperty(ref currentStep, trimmed);
            }
        }

        if (!inTargetJob)
        {
            return null;
        }

        if (currentStep is not null)
        {
            steps.Add(currentStep);
        }

        return new WorkflowJob(jobName, runsOn, needs.ToArray(), matrixProjects.ToArray(), steps.ToArray());
    }

    private static void ApplyStepProperty(ref WorkflowStep step, string propertyLine)
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
                step = step with { Name = value, InPathBlock = false };
                if (step.UsesArtifactUpload)
                {
                    step = step with { ArtifactNameTemplate = value };
                }

                break;
            case "if":
                step = step with { IfCondition = value, InPathBlock = false };
                break;
            case "run":
                step = step with { RunTemplate = value, InPathBlock = false };
                break;
            case "uses":
                step = step with
                {
                    UsesArtifactUpload = value.StartsWith("actions/upload-artifact@", StringComparison.Ordinal),
                    InPathBlock = false,
                };
                break;
            case "path":
                step = step with
                {
                    ArtifactPathTemplate = string.Equals(value, "|", StringComparison.Ordinal) ? string.Empty : value,
                    InPathBlock = string.Equals(value, "|", StringComparison.Ordinal),
                };
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

    private sealed record LinuxGateOrderingResult(bool LinuxGateIsRequired, string RequiredJob);

    private sealed record LinuxMatrixSimulationResult(
        bool JobSucceeded,
        string? FailedCommand,
        bool ArtifactUploaded,
        string? ArtifactName,
        string? ArtifactPath);

    private sealed record WorkflowJob(string Name, string RunsOn, string[] Needs, string[] MatrixProjects, WorkflowStep[] Steps);

    private sealed record WorkflowStep(
        string? Name,
        string? IfCondition,
        string? RunTemplate,
        bool UsesArtifactUpload,
        string? ArtifactNameTemplate,
        string? ArtifactPathTemplate,
        bool InPathBlock);
}
