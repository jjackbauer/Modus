using System.Diagnostics;
using Wip.Shell.E2E.Compliance;
using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

public sealed class WipTestBaselineGateTests
{
    private const string ChecklistItem =
        "Restore full green Wip.* test baseline by fixing the three currently failing tests (Modus contributor readme path drift; E2E compliance `EachItem` vs `EveryItem` assertion) before any new checklist transitions [mandatory - baseline gate]";

    private static readonly ComplianceTestProject[] WipTestProjects =
    [
        new("WIP/Wip.Abstractions.Tests", "WIP/Wip.Abstractions.Tests/Wip.Abstractions.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Artifacts.Local.Tests", "WIP/Wip.Artifacts.Local.Tests/Wip.Artifacts.Local.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Builder.Tests", "WIP/Wip.Builder.Tests/Wip.Builder.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Modus.Tests", "WIP/Wip.Modus.Tests/Wip.Modus.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Policy.LocalSafe.Tests", "WIP/Wip.Policy.LocalSafe.Tests/Wip.Policy.LocalSafe.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Runtime.Tests", "WIP/Wip.Runtime.Tests/Wip.Runtime.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Shell.Tests", "WIP/Wip.Shell.Tests/Wip.Shell.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Shell.E2E.Tests", "WIP/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj", UseNoBuild: true),
        new("WIP/Wip.ShellHost.Tests", "WIP/Wip.ShellHost.Tests/Wip.ShellHost.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Tools.Shell.Tests", "WIP/Wip.Tools.Shell.Tests/Wip.Tools.Shell.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Validation.DotNet.Tests", "WIP/Wip.Validation.DotNet.Tests/Wip.Validation.DotNet.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Workspaces.Git.Tests", "WIP/Wip.Workspaces.Git.Tests/Wip.Workspaces.Git.Tests.csproj", UseNoBuild: false),
    ];

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task WipTestBaseline_GivenFullWipTestMatrix_ExpectedAllProjectsPassBeforeRemediationTransitions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var failures = new List<string>();

        foreach (var project in WipTestProjects)
        {
            var projectPath = Path.Combine(repositoryRoot, project.ProjectFilePath);
            if (!File.Exists(projectPath))
            {
                continue;
            }

            var result = await ExecuteDotnetTestAsync(repositoryRoot, project, filter: null);
            if (result.ExitCode != 0)
            {
                failures.Add($"Project '{project.ProjectFilePath}' exitCode={result.ExitCode}{Environment.NewLine}{result.Output}");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"All WIP test projects must pass before remediation checklist transitions.{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failures)}");
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task WipTestBaseline_GivenKnownFailuresFromAdversarialAudit_ExpectedModusReadmeAndE2EComplianceTestsPassAfterFix()
    {
        var repositoryRoot = FindRepositoryRoot();
        var modusProject = new ComplianceTestProject(
            "WIP/Wip.Modus.Tests",
            "WIP/Wip.Modus.Tests/Wip.Modus.Tests.csproj",
            UseNoBuild: false);
        var e2eProject = new ComplianceTestProject(
            "WIP/Wip.Shell.E2E.Tests",
            "WIP/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj",
            UseNoBuild: true);

        var modusResult = await ExecuteDotnetTestAsync(
            repositoryRoot,
            modusProject,
            "FullyQualifiedName~WipContributorArchitectureReadme");
        var e2eResult = await ExecuteDotnetTestAsync(
            repositoryRoot,
            e2eProject,
            "FullyQualifiedName~BehaviorProofCompliance_GivenCrossProjectPlan_RecognizesRuntimeAndShellTestsOutsideExecutingAssembly|FullyQualifiedName~BehaviorProofCompliance_GivenLegacyEveryItemAlias_ExpectedRemovedOrRedirectedToCanonicalEachItemTest");

        var failures = new List<string>();
        if (modusResult.ExitCode != 0)
        {
            failures.Add($"Modus contributor readme tests failed.{Environment.NewLine}{modusResult.Output}");
        }

        if (e2eResult.ExitCode != 0)
        {
            failures.Add($"E2E compliance naming tests failed.{Environment.NewLine}{e2eResult.Output}");
        }

        Assert.True(
            failures.Count == 0,
            string.Join(Environment.NewLine + Environment.NewLine, failures));
    }

    private static async Task<TestExecutionResult> ExecuteDotnetTestAsync(
        string repositoryRoot,
        ComplianceTestProject project,
        string? filter)
    {
        var projectPath = Path.Combine(repositoryRoot, project.ProjectFilePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(projectPath);
        if (project.UseNoBuild)
        {
            startInfo.ArgumentList.Add("--no-build");
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            startInfo.ArgumentList.Add("--filter");
            startInfo.ArgumentList.Add(filter);
        }

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("minimal");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start dotnet test for '{projectPath}'.");

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = (await standardOutput) + (await standardError);
        return new TestExecutionResult(project.ProjectFilePath, process.ExitCode, output);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Modus.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root from test execution directory.");
    }

    private sealed record TestExecutionResult(string ProjectFilePath, int ExitCode, string Output);
}