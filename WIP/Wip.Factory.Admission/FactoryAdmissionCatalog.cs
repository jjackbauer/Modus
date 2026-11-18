using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Identifiers;

namespace Wip.Factory.Admission;

/// <summary>The executable-capability lifecycle from source §4.3 (F-M.1).</summary>
public enum AdmissionState
{
    Discovered = 1,
    Inspected = 2,
    Resolved = 3,
    Admitted = 4,
    Loaded = 5,
    Healthy = 6,
    Active = 7,
    Draining = 8,
    Unloaded = 9,
    Rejected = 10
}

/// <summary>A capability version the package requires at or above the given version.</summary>
public sealed record PackageDependency(CapabilityId CapabilityId, CapabilityVersion MinimumVersion);

/// <summary>An executable capability package offered for admission (F-M.2).</summary>
public sealed record CapabilityPackage(
    PackageId PackageId,
    CapabilityId CapabilityId,
    CapabilityVersion Version,
    string ContentHash,
    IReadOnlyList<PackageDependency> Dependencies,
    string AssemblyPath,
    string SubmittedByRole);

/// <summary>The verdict for one lifecycle request. On refusal nothing changed.</summary>
public sealed record AdmissionResult(bool Accepted, AdmissionState State, string? Reason)
{
    public static AdmissionResult Ok(AdmissionState state) => new(true, state, null);

    public static AdmissionResult Refused(AdmissionState state, string reason) => new(false, state, reason);
}

/// <summary>
/// The admission catalog. Every transition is declared; an undeclared transition is refused with the
/// state unchanged, and a package can only become <see cref="AdmissionState.Active"/> after it has been
/// inspected, resolved, admitted, loaded and health-tested in that order (F-M.1–F-M.3).
/// </summary>
public sealed class FactoryAdmissionCatalog
{
    private static readonly IReadOnlyDictionary<AdmissionState, AdmissionState[]> Adjacency =
        new Dictionary<AdmissionState, AdmissionState[]>
        {
            [AdmissionState.Discovered] = [AdmissionState.Inspected, AdmissionState.Rejected],
            [AdmissionState.Inspected] = [AdmissionState.Resolved, AdmissionState.Rejected],
            [AdmissionState.Resolved] = [AdmissionState.Admitted, AdmissionState.Rejected],
            [AdmissionState.Admitted] = [AdmissionState.Loaded, AdmissionState.Rejected],
            [AdmissionState.Loaded] = [AdmissionState.Healthy, AdmissionState.Rejected],
            [AdmissionState.Healthy] = [AdmissionState.Active, AdmissionState.Rejected],
            [AdmissionState.Active] = [AdmissionState.Draining, AdmissionState.Rejected],
            [AdmissionState.Draining] = [AdmissionState.Unloaded],
            [AdmissionState.Unloaded] = [],
            [AdmissionState.Rejected] = []
        };

    private readonly Dictionary<string, AdmissionState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CapabilityPackage> _packages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rejections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _leases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _admittedCapabilities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pins = new(StringComparer.Ordinal);

    private string? _activePackageId;
    private string? _previousActivePackageId;

    public const string IllegalTransitionCategory = "illegal-transition";
    public const string UnresolvedDependencyCategory = "unresolved-dependency";
    public const string InvalidContentHashCategory = "invalid-content-hash";
    public const string HealthTestFailedCategory = "health-test-failed";
    public const string LiveLeaseBlockingDrainCategory = "live-lease-blocks-drain";
    public const string SelfInstallRefusedCategory = "reflection-self-install-refused";
    public const string NothingToRollBackCategory = "nothing-to-roll-back";

    public string? ActivePackageId => _activePackageId;

    public AdmissionState? StateOf(PackageId packageId)
        => _states.TryGetValue(packageId.Value, out var state) ? state : null;

    /// <summary>Registers a package discovered on disk. Discovery is not admission.</summary>
    public AdmissionResult Discover(CapabilityPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        _packages[package.PackageId.Value] = package;
        _states[package.PackageId.Value] = AdmissionState.Discovered;
        return AdmissionResult.Ok(AdmissionState.Discovered);
    }

    /// <summary>Inspection: content hash present, declared dependency set resolvable (F-M.2).</summary>
    public AdmissionResult Inspect(PackageId packageId)
    {
        if (!TryGet(packageId, out var package, out var refusal))
        {
            return refusal!;
        }

        var transition = Transition(packageId, AdmissionState.Inspected);
        if (!transition.Accepted)
        {
            return transition;
        }

        if (string.IsNullOrWhiteSpace(package!.ContentHash)
            || !package.ContentHash.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return Reject(packageId, InvalidContentHashCategory);
        }

        foreach (var dependency in package.Dependencies)
        {
            var key = dependency.CapabilityId.Value + "@" + dependency.MinimumVersion.Value;
            if (!_admittedCapabilities.Contains(dependency.CapabilityId.Value))
            {
                return Reject(packageId, UnresolvedDependencyCategory + ":" + key);
            }
        }

        return AdmissionResult.Ok(AdmissionState.Inspected);
    }

    /// <summary>Resolution records that the package's contract and dependency graph is usable.</summary>
    public AdmissionResult Resolve(PackageId packageId)
        => TryGet(packageId, out _, out var refusal) ? Transition(packageId, AdmissionState.Resolved) : refusal!;

    public AdmissionResult Admit(PackageId packageId)
    {
        var result = Transition(packageId, AdmissionState.Admitted);
        if (result.Accepted && _packages.TryGetValue(packageId.Value, out var package))
        {
            _admittedCapabilities.Add(package.CapabilityId.Value);
        }

        return result;
    }

    public AdmissionResult Load(PackageId packageId) => Transition(packageId, AdmissionState.Loaded);

    /// <summary>
    /// Health test in isolation. A package whose health test fails never reaches Active (F-M.3).
    /// </summary>
    public AdmissionResult HealthTest(PackageId packageId, Func<bool> healthTest)
    {
        ArgumentNullException.ThrowIfNull(healthTest);

        var loaded = Load(packageId);
        if (!loaded.Accepted)
        {
            return loaded;
        }

        bool healthy;
        try
        {
            healthy = healthTest();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Reject(packageId, HealthTestFailedCategory + ":" + exception.Message);
        }

        return healthy
            ? Transition(packageId, AdmissionState.Healthy)
            : Reject(packageId, HealthTestFailedCategory);
    }

    /// <summary>
    /// Activates a healthy package. A reflection principal cannot force its own package active
    /// (F-M.4, F-M.8): self-install is refused and recorded.
    /// </summary>
    public AdmissionResult Activate(PackageId packageId, string actorRole, bool actorIsReflection)
    {
        if (!TryGet(packageId, out var package, out var refusal))
        {
            return refusal!;
        }

        if (actorIsReflection)
        {
            _rejections[packageId.Value] = SelfInstallRefusedCategory + ":" + actorRole;
            return AdmissionResult.Refused(AdmissionState.Healthy, SelfInstallRefusedCategory + ":" + actorRole);
        }

        var result = Transition(packageId, AdmissionState.Active);
        if (!result.Accepted)
        {
            return result;
        }

        if (_activePackageId is { } previous && !string.Equals(previous, packageId.Value, StringComparison.Ordinal))
        {
            _previousActivePackageId = previous;
        }

        _activePackageId = packageId.Value;
        return AdmissionResult.Ok(AdmissionState.Active);
    }

    /// <summary>Acquires a lease, so draining can wait for real work to finish (F-M.6).</summary>
    public void AcquireLease(PackageId packageId)
    {
        var key = packageId.Value;
        _leases[key] = _leases.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    public void ReleaseLease(PackageId packageId)
    {
        var key = packageId.Value;
        if (_leases.TryGetValue(key, out var count))
        {
            if (count <= 1)
            {
                _leases.Remove(key);
            }
            else
            {
                _leases[key] = count - 1;
            }
        }
    }

    /// <summary>
    /// Drains and unloads. A package with a live lease cannot be unloaded; the timeout is reported rather
    /// than forced (F-M.6).
    /// </summary>
    public AdmissionResult DrainAndUnload(PackageId packageId)
    {
        var draining = Transition(packageId, AdmissionState.Draining);
        if (!draining.Accepted)
        {
            return draining;
        }

        if (_leases.TryGetValue(packageId.Value, out var live) && live > 0)
        {
            _states[packageId.Value] = AdmissionState.Active;
            return AdmissionResult.Refused(
                AdmissionState.Active,
                LiveLeaseBlockingDrainCategory + ":" + live);
        }

        return Transition(packageId, AdmissionState.Unloaded);
    }

    /// <summary>
    /// Rolls back to the previously active package. The rolled-back package is retained, not deleted
    /// (F-M.7).
    /// </summary>
    public AdmissionResult RollBack()
    {
        if (_previousActivePackageId is not { } previous)
        {
            return AdmissionResult.Refused(AdmissionState.Active, NothingToRollBackCategory);
        }

        var from = _activePackageId;
        _activePackageId = previous;
        _previousActivePackageId = from;
        return AdmissionResult.Ok(_states.TryGetValue(previous, out var state) ? state : AdmissionState.Active);
    }

    public string? RejectionOf(PackageId packageId)
        => _rejections.TryGetValue(packageId.Value, out var reason) ? reason : null;

    /// <summary>
    /// Pins a session to the currently active package (F-M.5). A session keeps the resolution it
    /// started with, so promoting a new version never changes work already in flight.
    /// </summary>
    public bool PinSession(SessionId sessionId)
    {
        if (_activePackageId is not { } active)
        {
            return false;
        }

        _pins[sessionId.Value] = active;
        return true;
    }

    /// <summary>The package a pinned session resolved, or the currently active one when it never pinned.</summary>
    public string? ResolveForSession(SessionId sessionId)
        => _pins.TryGetValue(sessionId.Value, out var pinned) ? pinned : _activePackageId;

    /// <summary>True when the session is pinned to a version other than the active one.</summary>
    public bool IsStaleForSession(SessionId sessionId)
        => _pins.TryGetValue(sessionId.Value, out var pinned)
           && _activePackageId is { } active
           && !string.Equals(pinned, active, StringComparison.Ordinal);

    public bool IsRetained(PackageId packageId) => _packages.ContainsKey(packageId.Value) && _states.ContainsKey(packageId.Value);

    private AdmissionResult Transition(PackageId packageId, AdmissionState target)
    {
        var current = _states[packageId.Value];
        if (Adjacency.TryGetValue(current, out var targets) && targets.Contains(target))
        {
            _states[packageId.Value] = target;
            return AdmissionResult.Ok(target);
        }

        var allowed = targets is null || targets.Length == 0 ? "none (terminal)" : string.Join(", ", targets);
        return AdmissionResult.Refused(current, IllegalTransitionCategory + $":{current}->{target}; legal: {allowed}");
    }

    private AdmissionResult Reject(PackageId packageId, string reason)
    {
        _states[packageId.Value] = AdmissionState.Rejected;
        _rejections[packageId.Value] = reason;
        return AdmissionResult.Refused(AdmissionState.Rejected, reason);
    }

    private bool TryGet(PackageId packageId, out CapabilityPackage? package, out AdmissionResult? refusal)
    {
        package = null;
        refusal = null;

        if (!_packages.TryGetValue(packageId.Value, out package))
        {
            refusal = AdmissionResult.Refused(AdmissionState.Discovered, "unknown-package:" + packageId.Value);
            return false;
        }

        return true;
    }
}
