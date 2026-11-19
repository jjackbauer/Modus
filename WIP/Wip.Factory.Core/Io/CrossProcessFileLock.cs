using System.Text;

namespace Wip.Factory.Core.Io;

/// <summary>
/// A cross-process exclusive lease over one lock file. FileShare.None is the OS exclusivity mechanism; the durable
/// owner record makes it inspectable when a second process is refused. Shared by the work-item store, the operation
/// ledger, the template registry and the supervisor lease so they all obey one consistency contract (FAC-P1-03).
/// </summary>
public sealed class CrossProcessFileLock : IDisposable
{
    public const string TimeoutCategory = "lock-timeout";

    private readonly FileStream _handle;
    private bool _disposed;

    private CrossProcessFileLock(FileStream handle) => _handle = handle;

    public static async ValueTask<CrossProcessFileLock> AcquireAsync(
        string lockPath,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(lockPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (true)
        {
            try
            {
                var handle = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough);

                try
                {
                    handle.SetLength(0);
                    var owner = Encoding.UTF8.GetBytes(
                        Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + "|"
                        + DateTimeOffset.UtcNow.ToString("O"));
                    handle.Write(owner, 0, owner.Length);
                    handle.Flush(flushToDisk: true);
                }
                catch (IOException)
                {
                    // The exclusive handle is what matters; the owner record is best effort.
                }

                return new CrossProcessFileLock(handle);
            }
            catch (IOException)
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    throw new TimeoutException(TimeoutCategory + ":" + Path.GetFileName(lockPath));
                }

                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new InvalidOperationException("lock-denied:" + Path.GetFileName(lockPath), exception);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
    }
}
