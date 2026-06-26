namespace Wip.Shell.History;

/// <summary>
/// In-memory command history implementation. Used as fallback when persistent storage is unavailable.
/// </summary>
public sealed class InMemoryCommandHistory : ICommandHistory
{
    private readonly List<string> _commands = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask AddAsync(string command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _commands.Add(command);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _commands.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _commands.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }
}
