using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Diagnostics;
using Modus.Host.Plugins.Lifecycle;

namespace Modus.Host.Plugins.Validation;

internal sealed class PluginIsolationBoundary
{
    private readonly PluginFailureReporter _failureReporter;

    public PluginIsolationBoundary(PluginFailureReporter failureReporter)
    {
        _failureReporter = failureReporter;
    }

    public void IsolateFailure(
        PluginId pluginId,
        string failedStage,
        IEnumerable<string> failureReasons,
        ISet<string> failedPluginIds,
        List<string> diagnostics,
        RegistrationTransactionLog? transactionLog = null)
    {
        failedPluginIds.Add(pluginId.Value);

        foreach (var failureReason in failureReasons)
        {
            diagnostics.Add(_failureReporter.StageFailure(failedStage, pluginId, failureReason));
        }

        if (transactionLog is not null)
        {
            PluginRollbackCoordinator.Rollback(pluginId.Value, transactionLog, diagnostics);
        }

        diagnostics.Add(_failureReporter.Isolation(failedStage, pluginId));
        diagnostics.Add(_failureReporter.ContinuityPreserved());
    }

    public void IsolateOperationFailure(
        PluginId pluginId,
        OperationName operation,
        string reason,
        ISet<string> failedPluginIds,
        List<string> diagnostics,
        RegistrationTransactionLog transactionLog)
    {
        failedPluginIds.Add(pluginId.Value);
        diagnostics.Add(_failureReporter.OperationFailure(pluginId, operation, reason));
        PluginRollbackCoordinator.Rollback(pluginId.Value, transactionLog, diagnostics);
        diagnostics.Add(_failureReporter.Isolation("operation", pluginId));
        diagnostics.Add(_failureReporter.ContinuityPreserved());
    }
}
