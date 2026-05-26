using System.Security.Cryptography;
using System.Text;
using Wip.Abstractions.Identifiers;

namespace Wip.Runtime.Runtime;

public sealed record ApprovalValidationReport(
    ArtifactId ReportArtifactId,
    bool BuildSucceeded,
    bool TestSucceeded,
    string DiffHash)
{
    public bool Succeeded => BuildSucceeded && TestSucceeded;
}

public sealed record ApprovalReviewReport(
    ArtifactId ReportArtifactId,
    string ReviewedDiffHash,
    bool IsStale,
    string? StaleReason = null);

public sealed record ApprovalTokenRequest(
    SessionId SessionId,
    WorkflowId WorkflowId,
    string DiffHash,
    string TargetBranch,
    string TargetCommit,
    ApprovalReviewReport ReviewReport,
    ApprovalValidationReport ValidationReport,
    DateTimeOffset? ProducedAtUtc = null);

public sealed record ApprovalTokenBinding(
    SessionId SessionId,
    WorkflowId WorkflowId,
    string DiffHash,
    string TargetBranch,
    string TargetCommit,
    ArtifactId ValidationReportArtifactId,
    string ValidationReportDiffHash);

public sealed record ApprovalToken(
    string Token,
    ApprovalTokenBinding Binding,
    DateTimeOffset ProducedAtUtc,
    string BindingHash);

public sealed class WipRuntimeApprovalTokenFactory
{
    public ApprovalToken Create(ApprovalTokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ReviewReport);
        ArgumentNullException.ThrowIfNull(request.ValidationReport);

        ValidateRequired(request.DiffHash, nameof(request.DiffHash));
        ValidateRequired(request.TargetBranch, nameof(request.TargetBranch));
        ValidateRequired(request.TargetCommit, nameof(request.TargetCommit));
        ValidateRequired(request.ReviewReport.ReviewedDiffHash, nameof(request.ReviewReport.ReviewedDiffHash));
        ValidateRequired(request.ValidationReport.DiffHash, nameof(request.ValidationReport.DiffHash));

        if (request.ReviewReport.IsStale)
        {
            var staleReason = string.IsNullOrWhiteSpace(request.ReviewReport.StaleReason)
                ? "Approval token creation rejected: review report is stale."
                : $"Approval token creation rejected: review report is stale. {request.ReviewReport.StaleReason}";

            throw new InvalidOperationException(staleReason);
        }

        if (!string.Equals(request.DiffHash, request.ReviewReport.ReviewedDiffHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Approval token creation rejected: reviewed diff hash does not match the current approval candidate diff hash.");
        }

        if (!request.ValidationReport.Succeeded)
        {
            throw new InvalidOperationException(
                "Approval token creation requires a passing validation report (build and test must both succeed).");
        }

        if (!string.Equals(request.DiffHash, request.ValidationReport.DiffHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Approval token creation rejected: validation report diff hash does not match the approval candidate diff hash.");
        }

        var binding = new ApprovalTokenBinding(
            SessionId: request.SessionId,
            WorkflowId: request.WorkflowId,
            DiffHash: request.DiffHash,
            TargetBranch: request.TargetBranch,
            TargetCommit: request.TargetCommit,
            ValidationReportArtifactId: request.ValidationReport.ReportArtifactId,
            ValidationReportDiffHash: request.ValidationReport.DiffHash);

        var producedAtUtc = request.ProducedAtUtc ?? DateTimeOffset.UtcNow;
        var bindingHash = ComputeBindingHash(binding);
        var token = $"wip-approval-{Guid.NewGuid():N}";

        return new ApprovalToken(token, binding, producedAtUtc, bindingHash);
    }

    public static string ComputeBindingHash(ApprovalTokenBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var payload = string.Join(
            '\n',
            "wip-approval-token-v1",
            binding.SessionId.Value,
            binding.WorkflowId.Value,
            binding.DiffHash,
            binding.TargetBranch,
            binding.TargetCommit,
            binding.ValidationReportArtifactId.Value,
            binding.ValidationReportDiffHash);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
    }
}