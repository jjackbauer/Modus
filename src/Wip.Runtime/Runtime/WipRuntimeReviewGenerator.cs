using System.Text;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;

namespace Wip.Runtime.Runtime;

public sealed record ReviewValidationStatus(
    bool BuildSucceeded,
    bool TestSucceeded,
    string? DiffHash)
{
    public bool Succeeded => BuildSucceeded && TestSucceeded;
}

public sealed record ReviewRequest(
    SessionId SessionId,
    string CurrentDiffHash,
    IReadOnlyList<string> ChangedFiles,
    ReviewValidationStatus Validation,
    DateTimeOffset? ProducedAtUtc = null);

public sealed record ReviewStaleness(
    bool IsStale,
    string? Reason);

public sealed record ReviewResult(
    ReviewStaleness Staleness,
    string Markdown,
    ArtifactDescriptor ReportArtifact);

public sealed class WipRuntimeReviewGenerator
{
    private const string ProducerType = "Wip.Runtime";
    private const string ProducerVersion = "1.0.0";

    private readonly IArtifactStore _artifactStore;

    public WipRuntimeReviewGenerator(IArtifactStore artifactStore)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
    }

    public async ValueTask<ReviewResult> ReviewAsync(ReviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Validation);
        ArgumentNullException.ThrowIfNull(request.ChangedFiles);

        ValidateRequired(request.CurrentDiffHash, nameof(request.CurrentDiffHash));

        var staleness = DetectStaleness(request.Validation.DiffHash, request.CurrentDiffHash);
        var producedAtUtc = request.ProducedAtUtc ?? DateTimeOffset.UtcNow;
        var markdown = BuildMarkdown(request, staleness, producedAtUtc);

        var reportArtifact = await _artifactStore.SaveAsync(
            request.SessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"review-report-{Guid.NewGuid():N}"),
                kind: ArtifactKind.Markdown,
                fileName: "review-report",
                content: markdown,
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: producedAtUtc),
            cancellationToken);

        return new ReviewResult(staleness, markdown, reportArtifact);
    }

    public static ReviewStaleness DetectStaleness(string? validationDiffHash, string currentDiffHash)
    {
        ValidateRequired(currentDiffHash, nameof(currentDiffHash));

        if (string.IsNullOrWhiteSpace(validationDiffHash))
        {
            return new ReviewStaleness(
                IsStale: true,
                Reason: "Validation report is stale: no diff hash is available for current candidate comparison.");
        }

        if (!string.Equals(validationDiffHash, currentDiffHash, StringComparison.Ordinal))
        {
            return new ReviewStaleness(
                IsStale: true,
                Reason: "Validation report is stale: validation diff hash does not match current candidate diff hash.");
        }

        return new ReviewStaleness(IsStale: false, Reason: null);
    }

    private static string BuildMarkdown(ReviewRequest request, ReviewStaleness staleness, DateTimeOffset producedAtUtc)
    {
        var lines = new StringBuilder();
        lines.AppendLine("# WiP Review Report");
        lines.AppendLine();
        lines.AppendLine($"Produced at (UTC): {producedAtUtc:O}");
        lines.AppendLine($"Session: {request.SessionId.Value}");
        lines.AppendLine();
        lines.AppendLine("## Validation");
        lines.AppendLine($"Validation: {(request.Validation.Succeeded ? "Passed" : "Failed")}");
        lines.AppendLine($"Build succeeded: {request.Validation.BuildSucceeded}");
        lines.AppendLine($"Test succeeded: {request.Validation.TestSucceeded}");
        lines.AppendLine($"Validation diff hash: {request.Validation.DiffHash ?? "(missing)"}");
        lines.AppendLine($"Current diff hash: {request.CurrentDiffHash}");
        lines.AppendLine();
        lines.AppendLine("## Staleness");
        lines.AppendLine($"Stale: {(staleness.IsStale ? "Yes" : "No")}");
        if (staleness.IsStale)
            lines.AppendLine($"Reason: {staleness.Reason}");

        lines.AppendLine();
        lines.AppendLine("## Changed Files");
        if (request.ChangedFiles.Count == 0)
        {
            lines.AppendLine("- (none)");
        }
        else
        {
            foreach (var changedFile in request.ChangedFiles)
                lines.AppendLine($"- {changedFile}");
        }

        return lines.ToString();
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
    }
}