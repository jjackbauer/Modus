using Modus.Host.Plugins.Uploads;

namespace Modus.Host.Domain.WebApi;

internal sealed record ManagementPluginUploadOperationStatusResponse(
    string OperationId,
    string PackageName,
    string Stage,
    int ProgressPercent,
    bool IsTerminal,
    bool IsSuccess,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Diagnostics)
{
    public static ManagementPluginUploadOperationStatusResponse FromStatus(PluginUploadOperationStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new ManagementPluginUploadOperationStatusResponse(
            OperationId: status.OperationId.ToString(),
            PackageName: status.PackageName,
            Stage: status.Stage.ToString(),
            ProgressPercent: status.ProgressPercent,
            IsTerminal: status.IsTerminal,
            IsSuccess: status.IsSuccess,
            FailureReason: status.FailureReason,
            CreatedAt: status.CreatedAt,
            UpdatedAt: status.UpdatedAt,
            Diagnostics: status.Diagnostics.ToArray());
    }
}
