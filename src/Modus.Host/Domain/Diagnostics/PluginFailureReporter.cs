namespace Modus.Host.Diagnostics;

internal sealed class PluginFailureReporter
{
    public string StageFailure(string stage, string pluginId, string reason)
    {
        return $"stage={stage} plugin={pluginId} outcome=failure reason={reason}";
    }

    public string OperationFailure(string pluginId, string operation, string reason)
    {
        return $"stage=operation plugin={pluginId} operation={operation} outcome=failure reason={reason}";
    }

    public string Isolation(string failedStage, string pluginId)
    {
        return $"stage=isolation plugin={pluginId} failed-stage={failedStage} outcome=isolated";
    }

    public string ContinuityPreserved()
    {
        return "stage=continuity outcome=preserved";
    }
}
