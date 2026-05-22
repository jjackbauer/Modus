namespace Modus.Host.Plugins.Uploads;

public enum PluginUploadOperationStage
{
    Queued,
    Authorizing,
    Extracting,
    Validating,
    Loading,
    Running,
    Completed,
    Failed,
}

public sealed record PluginUploadOperationStatus(
    Guid OperationId,
    string PackageName,
    PluginUploadOperationStage Stage,
    int ProgressPercent,
    bool IsTerminal,
    bool IsSuccess,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Diagnostics);
