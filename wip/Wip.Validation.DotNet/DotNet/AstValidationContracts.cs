using System.Text.Json.Serialization;

namespace Wip.Validation.DotNet.DotNet;

public enum AstValidationOutcome
{
    Succeeded,
    Failed,
}

public sealed record AstValidationRequest(
    string RepositoryPath,
    string ProjectPath,
    TimeSpan? Timeout = null);

public sealed record AstDiagnostic(
    string Id,
    string Severity,
    string Message,
    string? FilePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

public sealed record AstValidationResult
{
    [JsonConstructor]
    public AstValidationResult(
        AstValidationOutcome outcome,
        int documentCount,
        int syntaxTreeCount,
        IReadOnlyList<AstDiagnostic>? diagnostics,
        string? failureReason,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome), "Outcome value is not supported.");

        ValidateCounts(documentCount, syntaxTreeCount);

        if (outcome == AstValidationOutcome.Failed && string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("Failure reason is required when outcome is failed.", nameof(failureReason));

        Outcome = outcome;
        DocumentCount = documentCount;
        SyntaxTreeCount = syntaxTreeCount;
        Diagnostics = SnapshotDiagnostics(diagnostics);
        FailureReason = outcome == AstValidationOutcome.Succeeded ? null : failureReason;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
    }

    [JsonPropertyOrder(0)]
    public AstValidationOutcome Outcome { get; }

    [JsonPropertyOrder(1)]
    public int DocumentCount { get; }

    [JsonPropertyOrder(2)]
    public int SyntaxTreeCount { get; }

    [JsonPropertyOrder(3)]
    public IReadOnlyList<AstDiagnostic> Diagnostics { get; }

    [JsonPropertyOrder(4)]
    public int DiagnosticCount => Diagnostics.Count;

    [JsonPropertyOrder(5)]
    public string? FailureReason { get; }

    [JsonPropertyOrder(6)]
    public bool Succeeded => Outcome == AstValidationOutcome.Succeeded;

    [JsonPropertyOrder(7)]
    public DateTimeOffset StartedAtUtc { get; }

    [JsonPropertyOrder(8)]
    public DateTimeOffset CompletedAtUtc { get; }

    public static AstValidationResult Success(
        int documentCount,
        int syntaxTreeCount,
        IReadOnlyList<AstDiagnostic>? diagnostics,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        return new AstValidationResult(
            AstValidationOutcome.Succeeded,
            documentCount,
            syntaxTreeCount,
            diagnostics,
            failureReason: null,
            startedAtUtc,
            completedAtUtc);
    }

    public static AstValidationResult Failure(
        string failureReason,
        IReadOnlyList<AstDiagnostic>? diagnostics,
        int documentCount,
        int syntaxTreeCount,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        return new AstValidationResult(
            AstValidationOutcome.Failed,
            documentCount,
            syntaxTreeCount,
            diagnostics,
            failureReason,
            startedAtUtc,
            completedAtUtc);
    }

    private static IReadOnlyList<AstDiagnostic> SnapshotDiagnostics(IReadOnlyList<AstDiagnostic>? diagnostics)
        => diagnostics?.ToArray() ?? [];

    private static void ValidateCounts(int documentCount, int syntaxTreeCount)
    {
        if (documentCount < 0)
            throw new ArgumentOutOfRangeException(nameof(documentCount), "Document count cannot be negative.");

        if (syntaxTreeCount < 0)
            throw new ArgumentOutOfRangeException(nameof(syntaxTreeCount), "Syntax tree count cannot be negative.");
    }
}