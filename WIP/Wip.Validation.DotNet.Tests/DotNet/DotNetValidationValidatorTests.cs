using System.Diagnostics;
using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Artifacts.Local;
using Wip.Validation.DotNet.DotNet;
using Wip.Workspaces.Git;
using Xunit;

namespace Wip.Validation.DotNet.Tests.DotNet;

public sealed class DotNetValidationValidatorTests
{
    [Fact]
    public async Task ExecuteAsync_GivenSuccessfulBuildAndTest_ProducesPassingValidationReportWithCommandEvidence()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-success"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            TargetBranch: "main",
            SessionBranch: "main",
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Report.Restore.ExitCode);
        Assert.Equal(0, result.Report.Build.ExitCode);
        Assert.Equal(0, result.Report.Test.ExitCode);
        Assert.Contains("restore src/Example/Example.csproj", result.Report.Restore.Command, StringComparison.Ordinal);
        Assert.Contains("build src/Example/Example.csproj", result.Report.Build.Command, StringComparison.Ordinal);
        Assert.Contains("test tests/Example.Tests/Example.Tests.csproj --no-build", result.Report.Test.Command, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.Report.DiffHash));

        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(reportPath));

        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, CancellationToken.None));
        var root = persisted.RootElement;

        Assert.Equal(context.SessionId.Value, root.GetProperty("SessionId").GetProperty("Value").GetString());
        Assert.Equal(0, root.GetProperty("Restore").GetProperty("ExitCode").GetInt32());
        Assert.Equal(0, root.GetProperty("Build").GetProperty("ExitCode").GetInt32());
        Assert.Equal(0, root.GetProperty("Test").GetProperty("ExitCode").GetInt32());
    }

    [Fact]
    public void DotNetValidationRequest_GivenMultiProjectInputs_ExpectedTypedContractCarriesAllRuntimeExecutionParameters()
    {
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: "C:/repo/modus",
            AstProjectPath: "src/Example/Example.csproj",
            EnableAstValidation: true,
            AstTimeout: TimeSpan.FromSeconds(9),
            TargetBranch: "main",
            SessionBranch: "feature/session",
            CommandTimeout: TimeSpan.FromSeconds(42),
            DotNetExecutablePath: "dotnet");

        Assert.Equal("C:/repo/modus", request.ProjectGraph.RepositoryPath);
        Assert.Equal("src/Example/Example.csproj", request.ProjectGraph.BuildProjectPath);
        Assert.Equal("tests/Example.Tests/Example.Tests.csproj", request.ProjectGraph.TestProjectPath);
        Assert.Equal("src/Example/Example.csproj", request.ProjectGraph.AstProjectPath);

        Assert.Equal(TimeSpan.FromSeconds(42), request.TimeoutPolicy.CommandTimeout);
        Assert.Equal(TimeSpan.FromSeconds(9), request.TimeoutPolicy.AstTimeout);
        Assert.Equal("dotnet", request.DotNetExecutablePath);
        Assert.Equal(10, request.RequiredSdkMajorVersion);
        Assert.Equal("dotnet", request.ExecutableResolutionPolicy.ExecutablePath);
        Assert.Equal(10, request.ExecutableResolutionPolicy.RequiredMajorVersion);
    }

    [Fact]
    public void ValidationRequest_GivenConfiguredInputs_ExpectedTypedContractCarriesExecutableParameters()
    {
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: "C:/repo/modus",
            AstProjectPath: "src/Example/Example.csproj",
            EnableAstValidation: true,
            AstTimeout: TimeSpan.FromSeconds(9),
            TargetBranch: "main",
            SessionBranch: "feature/session",
            CommandTimeout: TimeSpan.FromSeconds(42),
            DotNetExecutablePath: "dotnet",
            RequiredSdkMajorVersion: 10);

        Assert.Equal("C:/repo/modus", request.RepositoryPath);
        Assert.Equal("src/Example/Example.csproj", request.BuildProjectPath);
        Assert.Equal("tests/Example.Tests/Example.Tests.csproj", request.TestProjectPath);
        Assert.Equal(TimeSpan.FromSeconds(42), request.TimeoutPolicy.CommandTimeout);
        Assert.Equal(TimeSpan.FromSeconds(9), request.TimeoutPolicy.AstTimeout);
        Assert.Equal("dotnet", request.ExecutableResolutionPolicy.ExecutablePath);
        Assert.Equal(10, request.ExecutableResolutionPolicy.RequiredMajorVersion);
        Assert.True(request.EnableAstValidation);
        Assert.Equal("main", request.TargetBranch);
        Assert.Equal("feature/session", request.SessionBranch);
    }

    [Fact]
    public async Task ExecuteAsync_GivenFailingTestCommand_ReturnsFailedResultAndPersistsFailureEvidence()
    {
        await using var fixture = await TempGitRepository.CreateAsync(testExitCode: 1, testStdErr: "simulated test failure");
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-failure"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            TargetBranch: "main",
            SessionBranch: "main",
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Report.Restore.ExitCode);
        Assert.Equal(0, result.Report.Build.ExitCode);
        Assert.Equal(1, result.Report.Test.ExitCode);
        Assert.Contains("simulated test failure", result.Report.Test.StandardError, StringComparison.Ordinal);

        var artifactDescriptors = await artifactStore.ListAsync(context.SessionId, CancellationToken.None);
        var descriptor = Assert.Single(artifactDescriptors);
        Assert.Equal(result.ReportArtifact.ArtifactId, descriptor.ArtifactId);

        Assert.Equal(ValidationProgressionOutcome.Failed, result.ProgressionPolicy.Outcome);
        Assert.False(result.ProgressionPolicy.ReleaseReady);
        Assert.False(result.ProgressionPolicy.DeploymentReady);
        Assert.Contains("Validation failed", result.ProgressionPolicy.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GivenBuildFailureWithSkippedTest_PolicyBlocksReleaseAndDeploymentReadiness()
    {
        await using var fixture = await TempGitRepository.CreateAsync(buildExitCode: 1);
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-policy-blocked"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.Report.Build.ExitCode);
        Assert.Equal(-2, result.Report.Test.ExitCode);
        Assert.Contains("Skipped because build failed or timed out.", result.Report.Test.StandardError, StringComparison.Ordinal);

        Assert.Equal(ValidationProgressionOutcome.Blocked, result.ProgressionPolicy.Outcome);
        Assert.False(result.ProgressionPolicy.ReleaseReady);
        Assert.False(result.ProgressionPolicy.DeploymentReady);
        Assert.Contains("blocked or skipped", result.ProgressionPolicy.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_GivenMissingDotNetExecutable_ThrowsDeterministicResolutionFailureBeforeBuildOrTestDispatch()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-missing-dotnet"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            DotNetExecutablePath: Path.Combine(fixture.RepositoryPath, "missing-dotnet.cmd"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await validator.ExecuteAsync(request, context, CancellationToken.None));

        Assert.Contains("Unable to resolve dotnet executable", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing-dotnet.cmd", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.GetCommandInvocationCount());
    }

    [Fact]
    public async Task ExecuteAsync_GivenUnsupportedSdkMajorVersion_ThrowsDeterministicResolutionFailureBeforeBuildOrTestDispatch()
    {
        await using var fixture = await TempGitRepository.CreateAsync(dotNetVersion: "9.0.100");
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-unsupported-sdk"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            DotNetExecutablePath: fixture.DotNetStubPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await validator.ExecuteAsync(request, context, CancellationToken.None));

        Assert.Contains("major version 10 is required", exception.Message, StringComparison.Ordinal);
        Assert.Contains("reports SDK major version 9", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.GetCommandInvocationCount());
    }

    [Fact]
    public async Task ExecuteAsync_GivenExplicitExecutablePath_ExpectedRuntimeUsesConfiguredToolchainAndPersistsEvidence()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-explicit-executable"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains(fixture.DotNetStubPath, result.Report.Restore.Command, StringComparison.Ordinal);
        Assert.Contains(fixture.DotNetStubPath, result.Report.Build.Command, StringComparison.Ordinal);
        Assert.Contains(fixture.DotNetStubPath, result.Report.Test.Command, StringComparison.Ordinal);

        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, CancellationToken.None));
        var root = persisted.RootElement;

        Assert.Contains(fixture.DotNetStubPath, root.GetProperty("Restore").GetProperty("Command").GetString(), StringComparison.Ordinal);
        Assert.Contains(fixture.DotNetStubPath, root.GetProperty("Build").GetProperty("Command").GetString(), StringComparison.Ordinal);
        Assert.Contains(fixture.DotNetStubPath, root.GetProperty("Test").GetProperty("Command").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GivenBuildCommandTimeout_ReturnsFailedResultAndPersistsTimeoutEvidence()
    {
        await using var fixture = await TempGitRepository.CreateAsync(buildDelayMilliseconds: 1500);
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-timeout"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            TargetBranch: "main",
            SessionBranch: "main",
            CommandTimeout: TimeSpan.FromMilliseconds(200),
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.Report.Restore.TimedOut);
        Assert.Equal(0, result.Report.Restore.ExitCode);
        Assert.True(result.Report.Build.TimedOut);
        Assert.Equal(-1, result.Report.Build.ExitCode);
        Assert.Contains("timed out", result.Report.Build.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.Report.Test.TimedOut);
        Assert.Equal(-2, result.Report.Test.ExitCode);
        Assert.Contains("Skipped because build failed or timed out.", result.Report.Test.StandardError, StringComparison.Ordinal);

        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, CancellationToken.None));
        var root = persisted.RootElement;
        Assert.True(root.GetProperty("Build").GetProperty("TimedOut").GetBoolean());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task DotNetValidationRequest_GivenInvalidTimeoutOrExecutable_ExpectedDeterministicGuardFailureBeforeExecution(long timeoutMilliseconds)
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-invalid-timeout"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            CommandTimeout: TimeSpan.FromMilliseconds(timeoutMilliseconds),
            DotNetExecutablePath: " ");

        var timeoutException = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await validator.ExecuteAsync(request with { DotNetExecutablePath = fixture.DotNetStubPath }, context, CancellationToken.None));
        Assert.Equal("CommandTimeout", timeoutException.ParamName);

        var executableException = await Assert.ThrowsAsync<ArgumentException>(
            async () => await validator.ExecuteAsync(request with { CommandTimeout = TimeSpan.FromSeconds(10) }, context, CancellationToken.None));
        Assert.Equal("DotNetExecutablePath", executableException.ParamName);

        Assert.Equal(0, fixture.GetCommandInvocationCount());
    }

    [Fact]
    public async Task ValidationContracts_GivenInvalidInputs_ExpectedGuardFailureBeforeProcessExecution()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);
        var context = new CapabilityContext(new SessionId("session-invalid-contracts-guard"), fixture.RepositoryPath);

        var timeoutException = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await validator.ExecuteAsync(
                new DotNetValidationRequest(
                    BuildProjectPath: "src/Example/Example.csproj",
                    TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
                    RepositoryPath: fixture.RepositoryPath,
                    CommandTimeout: TimeSpan.Zero,
                    DotNetExecutablePath: fixture.DotNetStubPath),
                context,
                CancellationToken.None));
        Assert.Equal("CommandTimeout", timeoutException.ParamName);

        var executableException = await Assert.ThrowsAsync<ArgumentException>(
            async () => await validator.ExecuteAsync(
                new DotNetValidationRequest(
                    BuildProjectPath: "src/Example/Example.csproj",
                    TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
                    RepositoryPath: fixture.RepositoryPath,
                    DotNetExecutablePath: " "),
                context,
                CancellationToken.None));
        Assert.Equal("DotNetExecutablePath", executableException.ParamName);

        Assert.Equal(0, fixture.GetCommandInvocationCount());
    }

    [Fact]
    public async Task ExecuteAsync_GivenEnableAstValidationWithoutAstProjectPath_ThrowsArgumentExceptionBeforeCommandExecution()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-ast-path-required"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: true,
            DotNetExecutablePath: fixture.DotNetStubPath);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await validator.ExecuteAsync(request, context, CancellationToken.None));

        Assert.Equal("AstProjectPath", exception.ParamName);
        Assert.Equal(0, fixture.GetCommandInvocationCount());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ExecuteAsync_GivenNonPositiveAstTimeout_ThrowsArgumentOutOfRangeException(long timeoutMilliseconds)
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-ast-timeout"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            AstTimeout: TimeSpan.FromMilliseconds(timeoutMilliseconds),
            DotNetExecutablePath: fixture.DotNetStubPath);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await validator.ExecuteAsync(request, context, CancellationToken.None));

        Assert.Equal("AstTimeout", exception.ParamName);
    }

    [Fact]
    public async Task ExecuteAsync_GivenAstValidationDisabled_PreservesExistingBuildAndTestBehavior()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-ast-disabled"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: false,
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Report.Restore.ExitCode);
        Assert.Equal(0, result.Report.Build.ExitCode);
        Assert.Equal(0, result.Report.Test.ExitCode);
        Assert.Equal(3, fixture.GetCommandInvocationCount());
        Assert.Null(result.Report.Ast);
    }

    [Fact]
    public async Task ExecuteAsync_GivenAstValidationEnabled_RunsBuildTestThenAstAndReturnsUnifiedReport()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-ast-enabled"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: true,
            AstProjectPath: "src/Example/Example.csproj",
            AstTimeout: TimeSpan.FromSeconds(10),
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.True(result.Succeeded);
        var ast = Assert.IsType<AstValidationResult>(result.Report.Ast);
        Assert.True(result.Report.Build.CompletedAtUtc <= ast.StartedAtUtc);
        Assert.True(result.Report.Test.CompletedAtUtc <= ast.StartedAtUtc);

        var invocations = fixture.GetCommandInvocations();
        Assert.Equal(3, invocations.Count);
        Assert.Equal("restore", invocations[0]);
        Assert.Equal("build", invocations[1]);
        Assert.Equal("test", invocations[2]);

        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, CancellationToken.None));
        var root = persisted.RootElement;
        var persistedAst = root.GetProperty("Ast");
        Assert.Equal((int)AstValidationOutcome.Succeeded, persistedAst.GetProperty("Outcome").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_GivenAnalyzerFailure_MarksOverallResultFailedAndPersistsAstFailureEvidence()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-ast-failure"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: true,
            AstProjectPath: "src/Example/Missing.csproj",
            AstTimeout: TimeSpan.FromSeconds(10),
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Report.Restore.ExitCode);
        Assert.Equal(0, result.Report.Build.ExitCode);
        Assert.Equal(0, result.Report.Test.ExitCode);
        var ast = Assert.IsType<AstValidationResult>(result.Report.Ast);
        Assert.False(ast.Succeeded);
        Assert.Contains("Project file not found", ast.FailureReason, StringComparison.Ordinal);

        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, CancellationToken.None));
        var root = persisted.RootElement;
        var persistedAst = root.GetProperty("Ast");
        Assert.Equal((int)AstValidationOutcome.Failed, persistedAst.GetProperty("Outcome").GetInt32());
        Assert.Contains("Project file not found", persistedAst.GetProperty("FailureReason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionValidationResult_GivenValidationCompletion_ExpectedTypedOutcomePreservesDiffAndArtifactSemantics()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-typed-outcome"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            TargetBranch: "main",
            SessionBranch: "main",
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.NotNull(result.SessionValidation);
        Assert.Equal(result.ReportArtifact.ArtifactId, result.SessionValidation.ReportArtifactId);
        Assert.Equal(result.Report.DiffHash, result.SessionValidation.DiffHash);
        Assert.True(result.SessionValidation.Restore.Succeeded);
        Assert.True(result.SessionValidation.Build.Succeeded);
        Assert.True(result.SessionValidation.Test.Succeeded);
        Assert.Equal(request.ProjectGraph, result.SessionValidation.ProjectGraph);
    }

    [Fact]
    public async Task ValidationResult_GivenExecutionCompleted_ExpectedTypedOutcomeContainsBuildTestAndCorrelationSemantics()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-typed-contracts-correlation"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Report.CorrelationId));
        Assert.Equal(context.SessionId.Value, result.Report.CorrelationId);
        Assert.Equal(0, result.Report.Restore.ExitCode);
        Assert.True(result.Report.Restore.Succeeded);
        Assert.Equal(0, result.Report.Build.ExitCode);
        Assert.True(result.Report.Build.Succeeded);
        Assert.Equal(0, result.Report.Test.ExitCode);
        Assert.True(result.Report.Test.Succeeded);

        Assert.Equal(ValidationProgressionOutcome.Allowed, result.ProgressionPolicy.Outcome);
        Assert.True(result.ProgressionPolicy.ReleaseReady);
        Assert.True(result.ProgressionPolicy.DeploymentReady);
        Assert.Contains("Validation succeeded", result.ProgressionPolicy.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_GivenRuntimeConfigured_ExpectedEffectiveConfigOutputsCommandCatalogAndPolicies()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-diagnostics-runtime-configured"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: true,
            AstProjectPath: "src/Example/Example.csproj",
            AstTimeout: TimeSpan.FromSeconds(10),
            CommandTimeout: TimeSpan.FromSeconds(30),
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        var diagnostics = Assert.IsType<ValidationOperationalDiagnostics>(result.Report.OperationalDiagnostics);
        Assert.Equal(fixture.RepositoryPath, diagnostics.EffectiveConfiguration.RepositoryPath);
        Assert.Equal(fixture.RepositoryPath, diagnostics.EffectiveConfiguration.WorktreePath);
        Assert.Equal("src/Example/Example.csproj", diagnostics.EffectiveConfiguration.BuildProjectPath);
        Assert.Equal("tests/Example.Tests/Example.Tests.csproj", diagnostics.EffectiveConfiguration.TestProjectPath);
        Assert.True(diagnostics.EffectiveConfiguration.AstValidationEnabled);
        Assert.Equal("src/Example/Example.csproj", diagnostics.EffectiveConfiguration.AstProjectPath);
        Assert.Equal(TimeSpan.FromSeconds(30), diagnostics.EffectiveConfiguration.CommandTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), diagnostics.EffectiveConfiguration.AstTimeout);

        Assert.Equal(4, diagnostics.CommandCatalog.Count);
        var restore = Assert.Single(diagnostics.CommandCatalog, static entry => entry.Name == "restore");
        var build = Assert.Single(diagnostics.CommandCatalog, static entry => entry.Name == "build");
        var test = Assert.Single(diagnostics.CommandCatalog, static entry => entry.Name == "test");
        var ast = Assert.Single(diagnostics.CommandCatalog, static entry => entry.Name == "ast");

        Assert.True(restore.Executed);
        Assert.True(build.Executed);
        Assert.True(test.Executed);
        Assert.True(ast.Enabled);
        Assert.Equal(-3, ast.ExitCode);

        var policyNames = diagnostics.ActiveRuntimePolicies.Select(static entry => entry.Policy).ToArray();
        Assert.Contains(ValidationProgressionPolicy.PolicyName, policyNames);
        Assert.Contains("dotnet-sdk-major-version-guard", policyNames);
        Assert.Contains("command-timeout", policyNames);
        Assert.Contains("ast-validation", policyNames);

        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, CancellationToken.None));
        var root = persisted.RootElement;
        var persistedDiagnostics = root.GetProperty("OperationalDiagnostics");
        _ = persistedDiagnostics.GetProperty("EffectiveConfiguration");
        _ = persistedDiagnostics.GetProperty("CommandCatalog");
        _ = persistedDiagnostics.GetProperty("ActiveRuntimePolicies");
        _ = persistedDiagnostics.GetProperty("Status");
    }

    [Fact]
    public async Task Diagnostics_GivenValidationExecution_ExpectedStatusIncludesCorrelationAndOutcomeSemantics()
    {
        await using var fixture = await TempGitRepository.CreateAsync(testExitCode: 1, testStdErr: "simulated test failure");
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-diagnostics-status"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        var diagnostics = Assert.IsType<ValidationOperationalDiagnostics>(result.Report.OperationalDiagnostics);
        Assert.Equal(context.SessionId.Value, diagnostics.Status.CorrelationId);
        Assert.False(diagnostics.Status.Succeeded);
        Assert.Equal(ValidationProgressionOutcome.Failed, diagnostics.Status.ProgressionOutcome);
        Assert.Contains("Validation failed", diagnostics.Status.ProgressionReason, StringComparison.Ordinal);
        Assert.Equal(result.Report.ProducedAtUtc, diagnostics.Status.ProducedAtUtc);
    }

    [Fact]
    public async Task Diagnostics_GivenPolicyViolation_ExpectedDeterministicReasonCodesAndRecoveryGuidance()
    {
        await using var fixture = await TempGitRepository.CreateAsync(buildExitCode: 1);
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-diagnostics-policy-violation"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.Equal(ValidationProgressionOutcome.Blocked, result.ProgressionPolicy.Outcome);
        var diagnostics = Assert.IsType<ValidationOperationalDiagnostics>(result.Report.OperationalDiagnostics);
        var isolationPolicy = Assert.Single(diagnostics.ActiveRuntimePolicies, static entry => entry.Policy == ValidationProgressionPolicy.PolicyName);

        Assert.Equal("Blocked", isolationPolicy.Status);
        Assert.Contains("blocked or skipped", isolationPolicy.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("release-ready", isolationPolicy.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deployment-ready", isolationPolicy.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_GivenAstTimeoutFailure_PersistsTimedOutAstEvidenceAlongsideBuildAndTestEvidence()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(
            artifactStore,
            workspaceProvider,
            new StubAstAnalyzer(
                AstValidationResult.Failure(
                    failureReason: "AST analysis timed out.",
                    diagnostics:
                    [
                        new AstDiagnostic("AST0001", "Error", "timed out", "src/Example/Program.cs", 1, 1, 1, 1)
                    ],
                    documentCount: 1,
                    syntaxTreeCount: 0,
                    startedAtUtc: DateTimeOffset.UnixEpoch,
                    completedAtUtc: DateTimeOffset.UnixEpoch.AddSeconds(1))));

        var context = new CapabilityContext(new SessionId("session-validate-ast-timeout-evidence"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: true,
            AstProjectPath: "src/Example/Example.csproj",
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Report.Restore.ExitCode);
        Assert.Equal(0, result.Report.Build.ExitCode);
        Assert.Equal(0, result.Report.Test.ExitCode);
        var ast = Assert.IsType<AstValidationResult>(result.Report.Ast);
        Assert.False(ast.Succeeded);
        Assert.Equal("AST analysis timed out.", ast.FailureReason);
        Assert.Equal(1, ast.DiagnosticCount);

        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, CancellationToken.None));
        var root = persisted.RootElement;
        Assert.Equal(0, root.GetProperty("Restore").GetProperty("ExitCode").GetInt32());
        Assert.Equal(0, root.GetProperty("Build").GetProperty("ExitCode").GetInt32());
        Assert.Equal(0, root.GetProperty("Test").GetProperty("ExitCode").GetInt32());
        Assert.Equal((int)AstValidationOutcome.Failed, root.GetProperty("Ast").GetProperty("Outcome").GetInt32());
        Assert.Equal("AST analysis timed out.", root.GetProperty("Ast").GetProperty("FailureReason").GetString());
        Assert.Equal(1, root.GetProperty("Ast").GetProperty("DiagnosticCount").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_GivenAstCanceledFailure_PersistsCanceledAstEvidenceAlongsideBuildAndTestEvidence()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(
            artifactStore,
            workspaceProvider,
            new StubAstAnalyzer(
                AstValidationResult.Failure(
                    failureReason: "AST analysis canceled.",
                    diagnostics: [],
                    documentCount: 0,
                    syntaxTreeCount: 0,
                    startedAtUtc: DateTimeOffset.UnixEpoch,
                    completedAtUtc: DateTimeOffset.UnixEpoch.AddSeconds(1))));

        var context = new CapabilityContext(new SessionId("session-validate-ast-canceled-evidence"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: true,
            AstProjectPath: "src/Example/Example.csproj",
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Report.Restore.ExitCode);
        Assert.Equal(0, result.Report.Build.ExitCode);
        Assert.Equal(0, result.Report.Test.ExitCode);
        var ast = Assert.IsType<AstValidationResult>(result.Report.Ast);
        Assert.False(ast.Succeeded);
        Assert.Equal("AST analysis canceled.", ast.FailureReason);

        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, CancellationToken.None));
        var root = persisted.RootElement;
        Assert.Equal(0, root.GetProperty("Restore").GetProperty("ExitCode").GetInt32());
        Assert.Equal(0, root.GetProperty("Build").GetProperty("ExitCode").GetInt32());
        Assert.Equal(0, root.GetProperty("Test").GetProperty("ExitCode").GetInt32());
        Assert.Equal((int)AstValidationOutcome.Failed, root.GetProperty("Ast").GetProperty("Outcome").GetInt32());
        Assert.Equal("AST analysis canceled.", root.GetProperty("Ast").GetProperty("FailureReason").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_GivenTestFailureAndAstEnabled_PersistsUnifiedBuildTestAndAstEvidenceFromSingleRun()
    {
        await using var fixture = await TempGitRepository.CreateAsync(testExitCode: 1, testStdErr: "simulated test failure");
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-unified-ast-with-test-failure"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: true,
            AstProjectPath: "src/Example/Example.csproj",
            AstTimeout: TimeSpan.FromSeconds(10),
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Report.Restore.ExitCode);
        Assert.Equal(0, result.Report.Build.ExitCode);
        Assert.Equal(1, result.Report.Test.ExitCode);
        Assert.Contains("simulated test failure", result.Report.Test.StandardError, StringComparison.Ordinal);

        var ast = Assert.IsType<AstValidationResult>(result.Report.Ast);
        Assert.True(ast.Succeeded);
        Assert.True(result.Report.Test.CompletedAtUtc <= ast.StartedAtUtc);

        var invocations = fixture.GetCommandInvocations();
        Assert.Equal(3, invocations.Count);
        Assert.Equal("restore", invocations[0]);
        Assert.Equal("build", invocations[1]);
        Assert.Equal("test", invocations[2]);

        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, CancellationToken.None));
        var root = persisted.RootElement;
        Assert.Equal(0, root.GetProperty("Restore").GetProperty("ExitCode").GetInt32());
        Assert.Equal(0, root.GetProperty("Build").GetProperty("ExitCode").GetInt32());
        Assert.Equal(1, root.GetProperty("Test").GetProperty("ExitCode").GetInt32());
        Assert.Equal((int)AstValidationOutcome.Succeeded, root.GetProperty("Ast").GetProperty("Outcome").GetInt32());
    }

    [Fact]
    public async Task ValidationReportArtifact_GivenAstExecution_PersistsAstSectionWithDeterministicJsonShape()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        await File.WriteAllTextAsync(
            Path.Combine(fixture.RepositoryPath, "src", "Example", "Program.cs"),
            "namespace Example;\n\npublic static class Program\n{\n    public static int Main() => 0;\n}\n",
            CancellationToken.None);

        var context = new CapabilityContext(new SessionId("session-validate-ast-json-shape"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: true,
            AstProjectPath: "src/Example/Example.csproj",
            AstTimeout: TimeSpan.FromSeconds(10),
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, CancellationToken.None));
        var ast = persisted.RootElement.GetProperty("Ast");

        var propertyNames = ast.EnumerateObject().Select(static property => property.Name).ToArray();
        Assert.Equal(
            ["Outcome", "DocumentCount", "SyntaxTreeCount", "Diagnostics", "DiagnosticCount", "FailureReason", "Succeeded", "StartedAtUtc", "CompletedAtUtc"],
            propertyNames);

        Assert.Equal((int)AstValidationOutcome.Succeeded, ast.GetProperty("Outcome").GetInt32());
        Assert.Equal(1, ast.GetProperty("DocumentCount").GetInt32());
        Assert.Equal(1, ast.GetProperty("SyntaxTreeCount").GetInt32());
        Assert.Equal(0, ast.GetProperty("DiagnosticCount").GetInt32());
        Assert.True(ast.GetProperty("Succeeded").GetBoolean());
        Assert.Equal(JsonValueKind.Null, ast.GetProperty("FailureReason").ValueKind);
        Assert.Empty(ast.GetProperty("Diagnostics").EnumerateArray());

        var persistedStartedAtUtc = ast.GetProperty("StartedAtUtc").GetDateTimeOffset();
        var persistedCompletedAtUtc = ast.GetProperty("CompletedAtUtc").GetDateTimeOffset();
        Assert.Equal(result.Report.Ast!.StartedAtUtc, persistedStartedAtUtc);
        Assert.Equal(result.Report.Ast.CompletedAtUtc, persistedCompletedAtUtc);
        Assert.True(persistedCompletedAtUtc >= persistedStartedAtUtc);
    }

    [Fact]
    public async Task ValidationReportArtifact_GivenKnownDiagnosticSet_RoundTripsDiagnosticsWithoutSemanticDrift()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        await File.WriteAllTextAsync(
            Path.Combine(fixture.RepositoryPath, "src", "Example", "Broken.cs"),
            "namespace Example;\n\npublic static class Broken\n{\n    public static int Compute()\n    {\n        return 42\n    }\n}\n",
            CancellationToken.None);

        var context = new CapabilityContext(new SessionId("session-validate-ast-roundtrip"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: true,
            AstProjectPath: "src/Example/Example.csproj",
            AstTimeout: TimeSpan.FromSeconds(10),
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var persistedJson = await File.ReadAllTextAsync(reportPath, CancellationToken.None);
        var persistedReport = JsonSerializer.Deserialize<ValidationReport>(persistedJson);

        Assert.NotNull(persistedReport);

        var expectedAst = Assert.IsType<AstValidationResult>(result.Report.Ast);
        var persistedAst = Assert.IsType<AstValidationResult>(persistedReport.Ast);
        Assert.False(expectedAst.Succeeded);
        Assert.False(persistedAst.Succeeded);
        Assert.Equal(expectedAst.Outcome, persistedAst.Outcome);
        Assert.Equal(expectedAst.DocumentCount, persistedAst.DocumentCount);
        Assert.Equal(expectedAst.SyntaxTreeCount, persistedAst.SyntaxTreeCount);
        Assert.Equal(expectedAst.DiagnosticCount, persistedAst.DiagnosticCount);
        Assert.Equal(expectedAst.StartedAtUtc, persistedAst.StartedAtUtc);
        Assert.Equal(expectedAst.CompletedAtUtc, persistedAst.CompletedAtUtc);

        var expectedDiagnostic = Assert.Single(expectedAst.Diagnostics);
        var persistedDiagnostic = Assert.Single(persistedAst.Diagnostics);
        Assert.Equal(expectedDiagnostic, persistedDiagnostic);
    }

    [Fact]
    public async Task ExecuteAsync_GivenTempRepositoryWithValidAndInvalidSources_ProducesExpectedAstDiagnosticSet()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        await File.WriteAllTextAsync(
            Path.Combine(fixture.RepositoryPath, "src", "Example", "Valid.cs"),
            "namespace Example;\n\npublic static class Valid\n{\n    public static int Value() => 42;\n}\n",
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.RepositoryPath, "src", "Example", "Invalid.cs"),
            "namespace Example;\n\npublic static class Invalid\n{\n    public static int Value()\n    {\n        return 42\n    }\n}\n",
            CancellationToken.None);

        var context = new CapabilityContext(new SessionId("session-validate-ast-temp-repo-diagnostics"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: true,
            AstProjectPath: "src/Example/Example.csproj",
            AstTimeout: TimeSpan.FromSeconds(10),
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.False(result.Succeeded);
        var ast = Assert.IsType<AstValidationResult>(result.Report.Ast);
        Assert.False(ast.Succeeded);
        Assert.Equal(AstValidationOutcome.Failed, ast.Outcome);
        Assert.Equal(2, ast.DocumentCount);
        Assert.Equal(2, ast.SyntaxTreeCount);
        Assert.Equal("AST analysis found one or more error diagnostics.", ast.FailureReason);

        var diagnostic = Assert.Single(ast.Diagnostics);
        Assert.Equal("CS1002", diagnostic.Id);
        Assert.Equal("Error", diagnostic.Severity);
        Assert.Equal("src/Example/Invalid.cs", diagnostic.FilePath);
    }

    [Fact]
    public async Task ExecuteAsync_GivenSequentialValidationRuns_ProducesStableAstResultsForEquivalentInputs()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        await File.WriteAllTextAsync(
            Path.Combine(fixture.RepositoryPath, "src", "Example", "Invalid.cs"),
            "namespace Example;\n\npublic static class Invalid\n{\n    public static int Value()\n    {\n        return 42\n    }\n}\n",
            CancellationToken.None);

        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: true,
            AstProjectPath: "src/Example/Example.csproj",
            AstTimeout: TimeSpan.FromSeconds(10),
            DotNetExecutablePath: fixture.DotNetStubPath);

        var firstContext = new CapabilityContext(new SessionId("session-validate-ast-stable-run-1"), fixture.RepositoryPath);
        var secondContext = new CapabilityContext(new SessionId("session-validate-ast-stable-run-2"), fixture.RepositoryPath);

        var firstResult = await validator.ExecuteAsync(request, firstContext, CancellationToken.None);
        var secondResult = await validator.ExecuteAsync(request, secondContext, CancellationToken.None);

        var firstAst = Assert.IsType<AstValidationResult>(firstResult.Report.Ast);
        var secondAst = Assert.IsType<AstValidationResult>(secondResult.Report.Ast);

        Assert.Equal(firstAst.Outcome, secondAst.Outcome);
        Assert.Equal(firstAst.DocumentCount, secondAst.DocumentCount);
        Assert.Equal(firstAst.SyntaxTreeCount, secondAst.SyntaxTreeCount);
        Assert.Equal(firstAst.DiagnosticCount, secondAst.DiagnosticCount);
        Assert.Equal(firstAst.FailureReason, secondAst.FailureReason);
        Assert.Equal(firstAst.Diagnostics, secondAst.Diagnostics);

        var invocations = fixture.GetCommandInvocations();
        Assert.Equal(6, invocations.Count);
        Assert.Equal("restore", invocations[0]);
        Assert.Equal("build", invocations[1]);
        Assert.Equal("test", invocations[2]);
        Assert.Equal("restore", invocations[3]);
        Assert.Equal("build", invocations[4]);
        Assert.Equal("test", invocations[5]);
    }

    [Fact]
    public async Task ExecuteAsync_GivenAstFailureOnSecondRun_PreservesIsolationAndDoesNotLeakPreviousRunDiagnostics()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var firstBrokenPath = Path.Combine(fixture.RepositoryPath, "src", "Example", "BrokenFirst.cs");
        await File.WriteAllTextAsync(
            firstBrokenPath,
            "namespace Example;\n\npublic static class BrokenFirst\n{\n    public static int Value()\n    {\n        return 42\n    }\n}\n",
            CancellationToken.None);

        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            EnableAstValidation: true,
            AstProjectPath: "src/Example/Example.csproj",
            AstTimeout: TimeSpan.FromSeconds(10),
            DotNetExecutablePath: fixture.DotNetStubPath);

        var firstContext = new CapabilityContext(new SessionId("session-validate-ast-isolation-run-1"), fixture.RepositoryPath);
        var firstResult = await validator.ExecuteAsync(request, firstContext, CancellationToken.None);
        var firstAst = Assert.IsType<AstValidationResult>(firstResult.Report.Ast);
        var firstDiagnostic = Assert.Single(firstAst.Diagnostics);
        Assert.Equal("src/Example/BrokenFirst.cs", firstDiagnostic.FilePath);

        await File.WriteAllTextAsync(
            firstBrokenPath,
            "namespace Example;\n\npublic static class BrokenFirst\n{\n    public static int Value() => 42;\n}\n",
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.RepositoryPath, "src", "Example", "BrokenSecond.cs"),
            "namespace Example;\n\npublic static class BrokenSecond\n{\n    public static int Value()\n    {\n        return 99\n    }\n}\n",
            CancellationToken.None);

        var secondContext = new CapabilityContext(new SessionId("session-validate-ast-isolation-run-2"), fixture.RepositoryPath);
        var secondResult = await validator.ExecuteAsync(request, secondContext, CancellationToken.None);
        var secondAst = Assert.IsType<AstValidationResult>(secondResult.Report.Ast);
        var secondDiagnostic = Assert.Single(secondAst.Diagnostics);

        Assert.Equal("src/Example/BrokenSecond.cs", secondDiagnostic.FilePath);
        Assert.NotEqual(firstDiagnostic.FilePath, secondDiagnostic.FilePath);
        Assert.DoesNotContain(
            secondAst.Diagnostics,
            diagnostic => string.Equals(diagnostic.FilePath, "src/Example/BrokenFirst.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidationArtifact_GivenSuccessfulExecution_ExpectedStableJsonShapeAndReplayableCommandEvidence()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-artifact-success-json"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);
        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(reportPath));
        var jsonText = await File.ReadAllTextAsync(reportPath, CancellationToken.None);

        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        _ = root.GetProperty("SessionId");
        _ = root.GetProperty("ProducedAtUtc");
        _ = root.GetProperty("WorktreePath");
        _ = root.GetProperty("Restore");
        _ = root.GetProperty("Build");
        _ = root.GetProperty("Test");
        _ = root.GetProperty("CorrelationId");

        var restoreCmd = root.GetProperty("Restore").GetProperty("Command").GetString();
        var buildCmd = root.GetProperty("Build").GetProperty("Command").GetString();
        var testCmd = root.GetProperty("Test").GetProperty("Command").GetString();

        Assert.NotNull(restoreCmd);
        Assert.NotNull(buildCmd);
        Assert.NotNull(testCmd);
        Assert.Contains("restore", restoreCmd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("build", buildCmd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test", testCmd, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, root.GetProperty("Restore").GetProperty("ExitCode").GetInt32());
        Assert.Equal(0, root.GetProperty("Build").GetProperty("ExitCode").GetInt32());
        Assert.Equal(0, root.GetProperty("Test").GetProperty("ExitCode").GetInt32());
        Assert.False(root.GetProperty("Restore").GetProperty("TimedOut").GetBoolean());
        Assert.False(root.GetProperty("Build").GetProperty("TimedOut").GetBoolean());
        Assert.False(root.GetProperty("Test").GetProperty("TimedOut").GetBoolean());
    }

    [Fact]
    public async Task ValidationArtifact_GivenFailedExecution_ExpectedErrorSemanticsAndCommandOutputsPersisted()
    {
        const string expectedFailureMessage = "test failure semantics";
        await using var fixture = await TempGitRepository.CreateAsync(testExitCode: 1, testStdErr: expectedFailureMessage);
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-artifact-failure-semantics"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);
        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(reportPath));
        var jsonText = await File.ReadAllTextAsync(reportPath, CancellationToken.None);

        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;
        var testElement = root.GetProperty("Test");

        Assert.Equal(1, testElement.GetProperty("ExitCode").GetInt32());

        var stdErr = testElement.GetProperty("StandardError").GetString();
        Assert.NotNull(stdErr);
        Assert.Contains(expectedFailureMessage, stdErr, StringComparison.Ordinal);

        Assert.False(testElement.GetProperty("Succeeded").GetBoolean());
        Assert.False(testElement.GetProperty("TimedOut").GetBoolean());

        Assert.Equal(0, root.GetProperty("Restore").GetProperty("ExitCode").GetInt32());
        Assert.Equal(0, root.GetProperty("Build").GetProperty("ExitCode").GetInt32());
    }

    [Fact]
    public async Task ValidationArtifact_GivenRoundTripSerialization_ExpectedSemanticsRemainEquivalent()
    {
        await using var fixture = await TempGitRepository.CreateAsync(testStdErr: "test output line 1\ntest output line 2");
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-artifact-roundtrip"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            DotNetExecutablePath: fixture.DotNetStubPath);

        var originalResult = await validator.ExecuteAsync(request, context, CancellationToken.None);
        var reportPath = Path.Combine(fixture.RepositoryPath, originalResult.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var jsonText = await File.ReadAllTextAsync(reportPath, CancellationToken.None);

        var deserializedReport = JsonSerializer.Deserialize<ValidationReport>(jsonText);
        Assert.NotNull(deserializedReport);

        Assert.Equal(originalResult.Report.Restore.Command, deserializedReport.Restore.Command);
        Assert.Equal(originalResult.Report.Restore.ExitCode, deserializedReport.Restore.ExitCode);
        Assert.Equal(originalResult.Report.Restore.TimedOut, deserializedReport.Restore.TimedOut);
        Assert.Equal(originalResult.Report.Restore.StandardOutput, deserializedReport.Restore.StandardOutput);
        Assert.Equal(originalResult.Report.Restore.StandardError, deserializedReport.Restore.StandardError);
        Assert.Equal(originalResult.Report.Restore.StartedAtUtc, deserializedReport.Restore.StartedAtUtc);
        Assert.Equal(originalResult.Report.Restore.CompletedAtUtc, deserializedReport.Restore.CompletedAtUtc);

        Assert.Equal(originalResult.Report.Build.Command, deserializedReport.Build.Command);
        Assert.Equal(originalResult.Report.Build.ExitCode, deserializedReport.Build.ExitCode);
        Assert.Equal(originalResult.Report.Build.Succeeded, deserializedReport.Build.Succeeded);

        Assert.Equal(originalResult.Report.Test.Command, deserializedReport.Test.Command);
        Assert.Equal(originalResult.Report.Test.ExitCode, deserializedReport.Test.ExitCode);
        Assert.Equal(originalResult.Report.Test.StandardError, deserializedReport.Test.StandardError);

        Assert.Equal(originalResult.Report.CorrelationId, deserializedReport.CorrelationId);
        Assert.Equal(originalResult.Report.ProducedAtUtc, deserializedReport.ProducedAtUtc);
        Assert.Equal(originalResult.Report.WorktreePath, deserializedReport.WorktreePath);
    }

    [Fact]
    public async Task LifecycleE2E_GivenHappyPath_ExpectedRestoreBuildTestValidateReviewApproveAndReleaseSucceeds()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-lifecycle-e2e-happy"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ValidationProgressionOutcome.Allowed, result.ProgressionPolicy.Outcome);
        Assert.True(result.ProgressionPolicy.ReleaseReady);
        Assert.True(result.ProgressionPolicy.DeploymentReady);

        var invocations = fixture.GetCommandInvocations();
        Assert.Equal(["restore", "build", "test"], invocations);

        Assert.Equal(3, result.Report.CommandResults.Count);
        Assert.Collection(
            result.Report.CommandResults,
            entry =>
            {
                Assert.Equal(1, entry.Sequence);
                Assert.Contains("restore", entry.Command, StringComparison.OrdinalIgnoreCase);
                Assert.True(entry.Succeeded);
                Assert.Equal(0, entry.ExitCode);
            },
            entry =>
            {
                Assert.Equal(2, entry.Sequence);
                Assert.Contains("build", entry.Command, StringComparison.OrdinalIgnoreCase);
                Assert.True(entry.Succeeded);
                Assert.Equal(0, entry.ExitCode);
            },
            entry =>
            {
                Assert.Equal(3, entry.Sequence);
                Assert.Contains("test", entry.Command, StringComparison.OrdinalIgnoreCase);
                Assert.True(entry.Succeeded);
                Assert.Equal(0, entry.ExitCode);
            });

        Assert.Equal(3, result.Report.CommandRollup.Count);
        Assert.All(result.Report.CommandRollup, static entry => Assert.True(entry.Succeeded));

        var diagnostics = Assert.IsType<ValidationOperationalDiagnostics>(result.Report.OperationalDiagnostics);
        Assert.Equal(ValidationProgressionOutcome.Allowed, diagnostics.Status.ProgressionOutcome);
        Assert.True(diagnostics.Status.Succeeded);

        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, CancellationToken.None));
        var root = persisted.RootElement;
        Assert.Equal(3, root.GetProperty("CommandResults").GetArrayLength());
        Assert.Equal(3, root.GetProperty("CommandRollup").GetArrayLength());
        Assert.Equal((int)ValidationProgressionOutcome.Allowed, root.GetProperty("ProgressionPolicy").GetProperty("Outcome").GetInt32());
    }

    [Fact]
    public async Task LifecycleE2E_GivenBuildOrTestFailure_ExpectedDeterministicBlockAtReadinessAndReleaseGates()
    {
        await using var fixture = await TempGitRepository.CreateAsync(buildExitCode: 1);
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-lifecycle-e2e-blocked"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ValidationProgressionOutcome.Blocked, result.ProgressionPolicy.Outcome);
        Assert.False(result.ProgressionPolicy.ReleaseReady);
        Assert.False(result.ProgressionPolicy.DeploymentReady);

        var invocations = fixture.GetCommandInvocations();
        Assert.Equal(["restore", "build"], invocations);

        Assert.Equal(3, result.Report.CommandResults.Count);
        Assert.Collection(
            result.Report.CommandResults,
            entry =>
            {
                Assert.Equal(1, entry.Sequence);
                Assert.Contains("restore", entry.Command, StringComparison.OrdinalIgnoreCase);
                Assert.True(entry.Succeeded);
                Assert.Equal(0, entry.ExitCode);
            },
            entry =>
            {
                Assert.Equal(2, entry.Sequence);
                Assert.Contains("build", entry.Command, StringComparison.OrdinalIgnoreCase);
                Assert.False(entry.Succeeded);
                Assert.Equal(1, entry.ExitCode);
            },
            entry =>
            {
                Assert.Equal(3, entry.Sequence);
                Assert.Contains("test", entry.Command, StringComparison.OrdinalIgnoreCase);
                Assert.False(entry.Succeeded);
                Assert.Equal(-2, entry.ExitCode);
                Assert.Contains("Skipped because build failed or timed out", entry.StandardError, StringComparison.OrdinalIgnoreCase);
            });

        Assert.Equal(3, result.Report.CommandRollup.Count);
        Assert.Equal(1, result.Report.CommandRollup.Count(static entry => entry.Succeeded));

        var diagnostics = Assert.IsType<ValidationOperationalDiagnostics>(result.Report.OperationalDiagnostics);
        Assert.Equal(ValidationProgressionOutcome.Blocked, diagnostics.Status.ProgressionOutcome);
        Assert.False(diagnostics.Status.Succeeded);

        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, CancellationToken.None));
        var root = persisted.RootElement;
        Assert.Equal(3, root.GetProperty("CommandResults").GetArrayLength());
        Assert.Equal(3, root.GetProperty("CommandRollup").GetArrayLength());
        Assert.Equal((int)ValidationProgressionOutcome.Blocked, root.GetProperty("ProgressionPolicy").GetProperty("Outcome").GetInt32());
    }

    private sealed class TempGitRepository : IAsyncDisposable
    {
        private const string CommandLogFileName = "dotnet-invocations.log";

        private TempGitRepository(string repositoryPath, string dotNetStubPath)
        {
            RepositoryPath = repositoryPath;
            DotNetStubPath = dotNetStubPath;
        }

        public string RepositoryPath { get; }

        public string DotNetStubPath { get; }

        public int GetCommandInvocationCount()
        {
            var logPath = Path.Combine(RepositoryPath, CommandLogFileName);
            if (!File.Exists(logPath))
                return 0;

            return File.ReadLines(logPath).Count();
        }

        public IReadOnlyList<string> GetCommandInvocations()
        {
            var logPath = Path.Combine(RepositoryPath, CommandLogFileName);
            if (!File.Exists(logPath))
                return [];

            return File.ReadLines(logPath).ToArray();
        }

        public static async ValueTask<TempGitRepository> CreateAsync(
            int restoreExitCode = 0,
            int buildExitCode = 0,
            int testExitCode = 0,
            string testStdErr = "",
            int restoreDelayMilliseconds = 0,
            int buildDelayMilliseconds = 0,
            int testDelayMilliseconds = 0,
            string dotNetVersion = "10.0.100")
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-validate-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);

            var fixture = new TempGitRepository(
                repositoryPath,
                dotNetStubPath: Path.Combine(repositoryPath, "dotnet.cmd"));

            await fixture.WriteDotNetStubAsync(
                restoreExitCode,
                buildExitCode,
                testExitCode,
                testStdErr,
                restoreDelayMilliseconds,
                buildDelayMilliseconds,
                testDelayMilliseconds,
                dotNetVersion);
            await fixture.InitializeGitRepositoryAsync();

            return fixture;
        }

        private async ValueTask InitializeGitRepositoryAsync()
        {
            await RunGitAsync("init", "--initial-branch=main", ".");
            await RunGitAsync("config", "user.email", "wip-validation-tests@example.test");
            await RunGitAsync("config", "user.name", "Wip Validation Tests");

            Directory.CreateDirectory(Path.Combine(RepositoryPath, "src", "Example"));
            Directory.CreateDirectory(Path.Combine(RepositoryPath, "tests", "Example.Tests"));
            await File.WriteAllTextAsync(Path.Combine(RepositoryPath, "src", "Example", "Example.csproj"), "<Project />", CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(RepositoryPath, "tests", "Example.Tests", "Example.Tests.csproj"), "<Project />", CancellationToken.None);

            await RunGitAsync("add", ".");
            await RunGitAsync("commit", "-m", "initial");
        }

        private async ValueTask WriteDotNetStubAsync(
            int restoreExitCode,
            int buildExitCode,
            int testExitCode,
            string testStdErr,
            int restoreDelayMilliseconds,
            int buildDelayMilliseconds,
            int testDelayMilliseconds,
            string dotNetVersion)
        {
            var content =
                "@echo off\r\n" +
                "set args=%*\r\n" +
                "if not \"%args:version=%\"==\"%args%\" (\r\n" +
                $"  echo {dotNetVersion}\r\n" +
                "  exit /b 0\r\n" +
                ")\r\n" +
                "set cmd=%1\r\n" +
                $"echo %cmd%>>{CommandLogFileName}\r\n" +
                "if /I \"%cmd%\"==\"restore\" (\r\n" +
                BuildDelayLine(restoreDelayMilliseconds) +
                $"  echo Restore completed for %2\r\n  exit /b {restoreExitCode}\r\n" +
                ")\r\n" +
                "if /I \"%cmd%\"==\"build\" (\r\n" +
                BuildDelayLine(buildDelayMilliseconds) +
                $"  echo Build succeeded for %2\r\n  exit /b {buildExitCode}\r\n" +
                ")\r\n" +
                "if /I \"%cmd%\"==\"test\" (\r\n" +
                BuildDelayLine(testDelayMilliseconds) +
                "  echo Test command invoked for %2\r\n" +
                (string.IsNullOrWhiteSpace(testStdErr)
                    ? string.Empty
                    : $"  1>&2 echo {EscapeBatchLiteral(testStdErr)}\r\n") +
                $"  exit /b {testExitCode}\r\n" +
                ")\r\n" +
                "echo Unsupported command 1>&2\r\n" +
                "exit /b 99\r\n";

            await File.WriteAllTextAsync(DotNetStubPath, content, CancellationToken.None);
        }

        private static string BuildDelayLine(int delayMilliseconds)
        {
            if (delayMilliseconds <= 0)
                return string.Empty;

            return $"  powershell -NoProfile -Command \"Start-Sleep -Milliseconds {delayMilliseconds}\"\r\n";
        }

        private static string EscapeBatchLiteral(string value)
            => value.Replace("^", "^^", StringComparison.Ordinal)
                .Replace("&", "^&", StringComparison.Ordinal)
                .Replace("|", "^|", StringComparison.Ordinal)
                .Replace("<", "^<", StringComparison.Ordinal)
                .Replace(">", "^>", StringComparison.Ordinal);

        private async ValueTask RunGitAsync(params string[] args)
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = RepositoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in args)
                startInfo.ArgumentList.Add(arg);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start git process.");

            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(CancellationToken.None);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Git command failed ({string.Join(" ", args)}). ExitCode={process.ExitCode}. StdErr={await stdErrTask}. StdOut={await stdOutTask}");
            }
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (Directory.Exists(RepositoryPath))
                    Directory.Delete(RepositoryPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubAstAnalyzer : IRoslynAstAnalyzer
    {
        private readonly AstValidationResult _result;

        public StubAstAnalyzer(AstValidationResult result)
        {
            _result = result;
        }

        public ValueTask<AstValidationResult> AnalyzeAsync(AstValidationRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult(_result);
    }
}
