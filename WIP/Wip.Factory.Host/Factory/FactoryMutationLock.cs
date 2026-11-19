using System.Text;
using Wip.Factory.Core.Io;

namespace Wip.Factory.Host.Factory;

/// <summary>Raised when a command cannot obtain the shared mutation lease in time.</summary>
public sealed class MutationLockUnavailableException : InvalidOperationException
{
    public MutationLockUnavailableException(string category)
        : base(category) => Category = category;

    public string Category { get; }
}

/// <summary>
/// The single documented cross-process consistency contract for control-root mutations (v0.4 FAC-P1-03).
///
/// Lock order: a command may hold <c>mutation.lock</c> and then acquire a domain lock (<c>factory.lock</c>,
/// <c>operations.lock</c>, <c>templates.lock</c>); the reverse order is never taken, so the ordering is acyclic.
/// Model calls and other long external work happen before the lease is taken, never inside it.
/// </summary>
public sealed class FactoryMutationLock : IDisposable
{
    public const string HeldCategory = "mutation-lock-held";
    public const string DeniedCategory = "mutation-lock-denied";

    private readonly CrossProcessFileLock _lease;

    private FactoryMutationLock(CrossProcessFileLock lease) => _lease = lease;

    public static string PathFor(string controlRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlRoot);
        return Path.Combine(controlRoot, "locks", "mutation.lock");
    }

    /// <summary>Acquires the shared mutation lease, waiting up to the timeout. Callers must not perform model calls inside.</summary>
    public static async ValueTask<FactoryMutationLock> AcquireAsync(
        string controlRoot,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var lease = await CrossProcessFileLock
                .AcquireAsync(PathFor(controlRoot), timeout ?? TimeSpan.FromSeconds(30), cancellationToken)
                .ConfigureAwait(false);
            return new FactoryMutationLock(lease);
        }
        catch (TimeoutException exception)
        {
            throw new MutationLockUnavailableException(HeldCategory + ":" + exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw new MutationLockUnavailableException(DeniedCategory + ":" + exception.Message);
        }
    }

    /// <summary>Synchronous variant for the operator CLI's template registry, which is not async.</summary>
    public static FactoryMutationLock Acquire(string controlRoot, TimeSpan? timeout = null)
        => AcquireAsync(controlRoot, timeout).AsTask().GetAwaiter().GetResult();

    public void Dispose() => _lease.Dispose();
}

/// <summary>
/// A durable record of a released serve supervisor. One supervisor per (control root, work item) is enforced with
/// an OS-exclusive handle while it lives, and the record makes ownership inspectable after recovery (FAC-P1-02).
/// </summary>
public sealed class SupervisorLease : IDisposable
{
    public const string DuplicateCategory = "serve-duplicate-ownership";

    private readonly CrossProcessFileLock _lease;

    private SupervisorLease(CrossProcessFileLock lease, string workItemId, string mergedSha, int processId)
    {
        _lease = lease;
        WorkItemId = workItemId;
        MergedSha = mergedSha;
        ProcessId = processId;
    }

    public string WorkItemId { get; }

    public string MergedSha { get; }

    public int ProcessId { get; }

    public static async ValueTask<SupervisorLease> AcquireAsync(
        string controlRoot,
        string workItemId,
        string mergedSha,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(controlRoot, "serve");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, SafeName(workItemId) + ".supervisor");

        try
        {
            var lease = await CrossProcessFileLock
                .AcquireAsync(path, TimeSpan.FromMilliseconds(250), cancellationToken)
                .ConfigureAwait(false);
            return new SupervisorLease(lease, workItemId, mergedSha, Environment.ProcessId);
        }
        catch (TimeoutException)
        {
            throw new MutationLockUnavailableException(DuplicateCategory + ":" + workItemId);
        }
    }

    private static string SafeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_');
        }

        return builder.ToString();
    }

    public void Dispose() => _lease.Dispose();
}
