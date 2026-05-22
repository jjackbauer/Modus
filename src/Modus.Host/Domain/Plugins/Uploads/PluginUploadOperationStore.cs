namespace Modus.Host.Plugins.Uploads;

public sealed class PluginUploadOperationStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, PluginUploadOperationStatus> _operations = new();

    public PluginUploadOperationStatus CreateQueued(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            throw new ArgumentException("A package name is required.", nameof(packageName));
        }

        var now = DateTimeOffset.UtcNow;
        var operation = new PluginUploadOperationStatus(
            OperationId: Guid.NewGuid(),
            PackageName: packageName,
            Stage: PluginUploadOperationStage.Queued,
            ProgressPercent: 0,
            IsTerminal: false,
            IsSuccess: false,
            FailureReason: null,
            CreatedAt: now,
            UpdatedAt: now,
            Diagnostics: []);

        lock (_gate)
        {
            _operations.Add(operation.OperationId, operation);
        }

        return operation;
    }

    public bool TryGet(Guid operationId, out PluginUploadOperationStatus? status)
    {
        lock (_gate)
        {
            return _operations.TryGetValue(operationId, out status);
        }
    }

    public void MarkStage(Guid operationId, PluginUploadOperationStage stage, int progressPercent, string diagnostic)
    {
        Update(operationId, status =>
        {
            var diagnostics = status.Diagnostics.ToList();
            if (!string.IsNullOrWhiteSpace(diagnostic))
            {
                diagnostics.Add(diagnostic);
            }

            return status with
            {
                Stage = stage,
                ProgressPercent = Clamp(progressPercent),
                UpdatedAt = DateTimeOffset.UtcNow,
                Diagnostics = diagnostics,
            };
        });
    }

    public void Complete(Guid operationId, IEnumerable<string> diagnostics)
    {
        var snapshot = diagnostics?.Where(static d => !string.IsNullOrWhiteSpace(d)).ToArray() ?? [];

        Update(operationId, status =>
        {
            var mergedDiagnostics = status.Diagnostics.ToList();
            mergedDiagnostics.AddRange(snapshot);

            return status with
            {
                Stage = PluginUploadOperationStage.Completed,
                ProgressPercent = 100,
                IsTerminal = true,
                IsSuccess = true,
                FailureReason = null,
                UpdatedAt = DateTimeOffset.UtcNow,
                Diagnostics = mergedDiagnostics,
            };
        });
    }

    public void Fail(Guid operationId, string failureReason, IEnumerable<string>? diagnostics = null)
    {
        Update(operationId, status =>
        {
            var mergedDiagnostics = status.Diagnostics.ToList();
            if (diagnostics is not null)
            {
                mergedDiagnostics.AddRange(diagnostics.Where(static d => !string.IsNullOrWhiteSpace(d)));
            }

            return status with
            {
                Stage = PluginUploadOperationStage.Failed,
                ProgressPercent = 100,
                IsTerminal = true,
                IsSuccess = false,
                FailureReason = string.IsNullOrWhiteSpace(failureReason)
                    ? "Plugin upload operation failed."
                    : failureReason,
                UpdatedAt = DateTimeOffset.UtcNow,
                Diagnostics = mergedDiagnostics,
            };
        });
    }

    private void Update(Guid operationId, Func<PluginUploadOperationStatus, PluginUploadOperationStatus> updater)
    {
        lock (_gate)
        {
            if (!_operations.TryGetValue(operationId, out var current))
            {
                return;
            }

            _operations[operationId] = updater(current);
        }
    }

    private static int Clamp(int value)
    {
        if (value < 0)
        {
            return 0;
        }

        return value > 100 ? 100 : value;
    }
}
