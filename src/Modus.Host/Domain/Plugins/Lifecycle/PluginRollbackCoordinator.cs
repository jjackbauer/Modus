namespace Modus.Host.Plugins.Lifecycle;

internal static class PluginRollbackCoordinator
{
    public static void Rollback(string pluginId, RegistrationTransactionLog transactionLog, List<string> diagnostics)
    {
        diagnostics.Add($"stage=rollbackpending plugin={pluginId} outcome=started");

        var reverted = transactionLog.Rollback();
        var revertedEffects = reverted.Count == 0 ? "none" : string.Join(',', reverted);

        diagnostics.Add($"stage=rollback plugin={pluginId} outcome=success reverted={revertedEffects}");
    }
}
