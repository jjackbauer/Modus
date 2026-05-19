namespace Modus.Host.Plugins.Lifecycle;

internal sealed class RegistrationTransactionLog
{
    private readonly List<(string Effect, Action Undo)> _rollbackActions = [];

    public void Record(string effect, Action undo)
    {
        _rollbackActions.Add((effect, undo));
    }

    public IReadOnlyList<string> Rollback()
    {
        var reverted = new List<string>(_rollbackActions.Count);

        for (var i = _rollbackActions.Count - 1; i >= 0; i--)
        {
            var rollbackAction = _rollbackActions[i];
            rollbackAction.Undo();
            reverted.Add(rollbackAction.Effect);
        }

        return reverted;
    }
}
