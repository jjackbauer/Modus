namespace Wip.Shell.History;

/// <summary>
/// Abstraction for command history management.
/// </summary>
public interface ICommandHistory
{
    /// <summary>
    /// Adds a command to history.
    /// </summary>
    /// <param name="command">The command line to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask AddAsync(string command, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all commands in history.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of commands in chronological order.</returns>
    ValueTask<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Clears all history.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask ClearAsync(CancellationToken cancellationToken);
}
