using Modus.Core.Messaging;
using Modus.Core.Plugins;

namespace Modus.Host.Diagnostics;

internal sealed class PluginFailureReporter
{
    public string StageFailure(string stage, PluginId pluginId, string reason)
    {
        return $"stage={stage} plugin={pluginId} outcome=failure reason={reason}";
    }

    public string OperationFailure(PluginId pluginId, OperationName operation, string reason)
    {
        return $"stage=operation plugin={pluginId} operation={operation} outcome=failure reason={reason}";
    }

    public string Isolation(string failedStage, PluginId pluginId)
    {
        return $"stage=isolation plugin={pluginId} failed-stage={failedStage} outcome=isolated";
    }

    public string ContinuityPreserved()
    {
        return "stage=continuity outcome=preserved";
    }
}
