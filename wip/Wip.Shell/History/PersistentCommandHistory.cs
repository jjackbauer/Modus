using System.Text.Json;
using System.Runtime.InteropServices;

namespace Wip.Shell.History;

/// <summary>
/// Persistent command history implementation. Writes commands to repository-local .wip/command-history.ndjson.
/// Falls back to in-memory storage if persistence fails.
/// </summary>
public sealed class PersistentCommandHistory : ICommandHistory
{
    private readonly string _repositoryPath;
    private readonly InMemoryCommandHistory _fallback;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _persistenceAvailable;
    private string _statusCode = "persistent";
    private string _statusDescription = "persistent";

    private string HistoryFilePath => Path.Combine(_repositoryPath, ".wip", "command-history.ndjson");

    public PersistentCommandHistory(string repositoryPath)
    {
        _repositoryPath = repositoryPath ?? throw new ArgumentNullException(nameof(repositoryPath));
        _fallback = new InMemoryCommandHistory();
        _persistenceAvailable = TryInitializePersistence();
    }

    public CommandHistoryStatus GetStatus() => new(
        PersistenceEnabled: _persistenceAvailable,
        StatusCode: _statusCode,
        Description: _statusDescription,
        HistoryFilePath: _persistenceAvailable ? HistoryFilePath : null);

    public async ValueTask AddAsync(string command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        // Always add to fallback
        await _fallback.AddAsync(command, cancellationToken);

        if (!_persistenceAvailable)
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var wipDir = Path.Combine(_repositoryPath, ".wip");
            if (!Directory.Exists(wipDir))
                Directory.CreateDirectory(wipDir);

            var entry = new CommandHistoryEntry
            {
                Command = command,
                TimestampUtc = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(entry);
            await File.AppendAllTextAsync(HistoryFilePath, json + Environment.NewLine, cancellationToken);
        }
        catch
        {
            DisablePersistence("append-failed", "in-memory fallback (append-failed)");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken)
    {
        var persistedCommands = new List<string>();

        if (_persistenceAvailable)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (File.Exists(HistoryFilePath))
                {
                    var lines = await File.ReadAllLinesAsync(HistoryFilePath, cancellationToken);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        try
                        {
                            var entry = JsonSerializer.Deserialize<CommandHistoryEntry>(line);
                            if (entry?.Command is not null)
                                persistedCommands.Add(entry.Command);
                        }
                        catch
                        {
                            DisablePersistence("deserialize-failed", "in-memory fallback (deserialize-failed)");
                            break;
                        }
                    }
                }
            }
            catch
            {
                DisablePersistence("read-failed", "in-memory fallback (read-failed)");
            }
            finally
            {
                _gate.Release();
            }
        }

        // If we have persisted commands, return those (they're authoritative)
        if (persistedCommands.Count > 0)
            return persistedCommands.AsReadOnly();

        // Otherwise fall back to in-memory history
        return await _fallback.GetAllAsync(cancellationToken);
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        await _fallback.ClearAsync(cancellationToken);

        if (!_persistenceAvailable)
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(HistoryFilePath))
                File.Delete(HistoryFilePath);
        }
        catch
        {
            DisablePersistence("clear-failed", "in-memory fallback (clear-failed)");
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class CommandHistoryEntry
    {
        public string? Command { get; set; }
        public DateTime TimestampUtc { get; set; }
    }

    private bool TryInitializePersistence()
    {
        if (!IsSupportedPlatform())
        {
            DisablePersistence("unsupported-platform", "in-memory fallback (unsupported-platform)");
            return false;
        }

        try
        {
            var wipDir = Path.Combine(_repositoryPath, ".wip");
            Directory.CreateDirectory(wipDir);

            if (Directory.Exists(HistoryFilePath))
            {
                DisablePersistence("history-file-path-is-directory", "in-memory fallback (history-file-path-is-directory)");
                return false;
            }

            using var stream = new FileStream(
                HistoryFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read);

            _statusCode = "persistent";
            _statusDescription = "persistent";
            return true;
        }
        catch
        {
            DisablePersistence("initialization-failed", "in-memory fallback (initialization-failed)");
            return false;
        }
    }

    private static bool IsSupportedPlatform()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }

    private void DisablePersistence(string statusCode, string statusDescription)
    {
        _persistenceAvailable = false;
        _statusCode = statusCode;
        _statusDescription = statusDescription;
    }
}
