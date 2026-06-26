using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Validation.DotNet.DotNet;
using Xunit;

namespace Wip.Validation.DotNet.Tests.DotNet;

public sealed class AstValidationContractsTests
{
    [Fact]
    public void AstValidationRequest_GivenConcreteInputs_StoresDeterministicExecutionInputs()
    {
        var request = new AstValidationRequest(
            RepositoryPath: "C:/repo/modus",
            ProjectPath: "src/Example/Example.csproj",
            Timeout: TimeSpan.FromSeconds(30));

        Assert.Equal("C:/repo/modus", request.RepositoryPath);
        Assert.Equal("src/Example/Example.csproj", request.ProjectPath);
        Assert.Equal(TimeSpan.FromSeconds(30), request.Timeout);
    }

    [Fact]
    public void AstValidationResult_GivenAnalyzerSuccess_ExposesDeterministicSuccessAndCountSemantics()
    {
        var diagnostic = new AstDiagnostic(
            Id: "CS8019",
            Severity: "Info",
            Message: "Unnecessary using directive.",
            FilePath: "src/Example/Program.cs",
            StartLine: 1,
            StartColumn: 1,
            EndLine: 1,
            EndColumn: 6);

        var result = AstValidationResult.Success(
            documentCount: 2,
            syntaxTreeCount: 2,
            diagnostics: [diagnostic],
            startedAtUtc: DateTimeOffset.UnixEpoch,
            completedAtUtc: DateTimeOffset.UnixEpoch.AddSeconds(1));

        Assert.Equal(AstValidationOutcome.Succeeded, result.Outcome);
        Assert.True(result.Succeeded);
        Assert.Null(result.FailureReason);
        Assert.Equal(2, result.DocumentCount);
        Assert.Equal(2, result.SyntaxTreeCount);
        Assert.Equal(1, result.DiagnosticCount);
        Assert.Equal(diagnostic, Assert.Single(result.Diagnostics));
    }

    [Fact]
    public void AstValidationResult_GivenAnalyzerFailure_ExposesFailureReasonAndPreservesDiagnostics()
    {
        var diagnostic = new AstDiagnostic(
            Id: "CS1002",
            Severity: "Error",
            Message: "; expected",
            FilePath: "src/Example/Broken.cs",
            StartLine: 7,
            StartColumn: 14,
            EndLine: 7,
            EndColumn: 15);

        var result = AstValidationResult.Failure(
            failureReason: "AST analysis failed.",
            diagnostics: [diagnostic],
            documentCount: 1,
            syntaxTreeCount: 0,
            startedAtUtc: DateTimeOffset.UnixEpoch,
            completedAtUtc: DateTimeOffset.UnixEpoch.AddSeconds(2));

        Assert.Equal(AstValidationOutcome.Failed, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal("AST analysis failed.", result.FailureReason);
        Assert.Equal(1, result.DocumentCount);
        Assert.Equal(0, result.SyntaxTreeCount);
        Assert.Equal(1, result.DiagnosticCount);
        Assert.Equal(diagnostic, Assert.Single(result.Diagnostics));
    }

    [Fact]
    public void AstDiagnostic_GivenJsonProjection_PreservesRuleIdSeverityLocationAndMessage()
    {
        var diagnostic = new AstDiagnostic(
            Id: "CS1002",
            Severity: "Error",
            Message: "; expected",
            FilePath: "src/Example/Broken.cs",
            StartLine: 7,
            StartColumn: 14,
            EndLine: 7,
            EndColumn: 15);

        var json = JsonSerializer.Serialize(diagnostic);
        var roundTrip = JsonSerializer.Deserialize<AstDiagnostic>(json);

        Assert.NotNull(roundTrip);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("CS1002", root.GetProperty("Id").GetString());
        Assert.Equal("Error", root.GetProperty("Severity").GetString());
        Assert.Equal("; expected", root.GetProperty("Message").GetString());
        Assert.Equal("src/Example/Broken.cs", root.GetProperty("FilePath").GetString());
        Assert.Equal(7, root.GetProperty("StartLine").GetInt32());
        Assert.Equal(14, root.GetProperty("StartColumn").GetInt32());
        Assert.Equal(7, root.GetProperty("EndLine").GetInt32());
        Assert.Equal(15, root.GetProperty("EndColumn").GetInt32());

        Assert.Equal(diagnostic, roundTrip);
    }

    [Fact]
    public void DotNetValidationResult_GivenAstFailure_MarksOverallResultFailed()
    {
        var report = new ValidationReport(
            SessionId: new SessionId("session-ast-failure"),
            ProducedAtUtc: DateTimeOffset.UnixEpoch,
            WorktreePath: "C:/repo/modus/.wip/worktree",
            Restore: new ValidationCommandResult("dotnet restore", 0, "", "", false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMilliseconds(500)),
            Build: new ValidationCommandResult("dotnet build", 0, "", "", false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1)),
            Test: new ValidationCommandResult("dotnet test", 0, "", "", false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(2)),
            DiffHash: null,
            CorrelationId: "session-ast-failure",
            Ast: AstValidationResult.Failure(
                failureReason: "AST analysis failed.",
                diagnostics: [],
                documentCount: 1,
                syntaxTreeCount: 0,
                startedAtUtc: DateTimeOffset.UnixEpoch,
                completedAtUtc: DateTimeOffset.UnixEpoch.AddSeconds(3)));

        var result = new DotNetValidationResult(
            report,
            new ArtifactDescriptor(
                new ArtifactId("artifact.validation"),
                new SessionId("session-ast-failure"),
                ArtifactKind.Json,
                "artifacts/session-ast-failure/validation-report.json",
                "Wip.Validation.DotNet",
                "1.0.0",
                DateTimeOffset.UnixEpoch));

        Assert.False(result.Succeeded);
        Assert.Equal(AstValidationOutcome.Failed, Assert.IsType<AstValidationResult>(result.Report.Ast).Outcome);
        Assert.Equal(ValidationProgressionOutcome.Failed, result.ProgressionPolicy.Outcome);
        Assert.False(result.ProgressionPolicy.ReleaseReady);
        Assert.False(result.ProgressionPolicy.DeploymentReady);
    }
}