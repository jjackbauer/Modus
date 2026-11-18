using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Admission;
using Wip.Factory.Contracts.Identifiers;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class AdmissionLifecycleTests
{
    private static Episode NewPackage(string id, string role = "implementations-analyst") =>
        new(id, role);

    private sealed class Episode
    {
        public Episode(string id, string role)
        {
            Package = new CapabilityPackage(
                new PackageId(id),
                new CapabilityId("capability." + id),
                new CapabilityVersion("1.0.0"),
                "sha256:abcdef0123456789",
                [],
                "packages/" + id + ".dll",
                role);
            Catalog = new FactoryAdmissionCatalog();
            Catalog.Discover(Package);
        }

        public CapabilityPackage Package { get; }

        public FactoryAdmissionCatalog Catalog { get; }

        public PackageId Id => Package.PackageId;

        public void Admit()
        {
            Catalog.Inspect(Id);
            Catalog.Resolve(Id);
            Catalog.Admit(Id);
        }

        public AdmissionResult Activate()
        {
            Admit();
            Catalog.HealthTest(Id, () => true);
            return Catalog.Activate(Id, Package.SubmittedByRole, actorIsReflection: false);
        }
    }

    [Fact]
    public void Admission_GivenIllegalLifecycleTransition_RejectsAndLeavesStateUnchanged()
    {
        var episode = NewPackage("p-illegal");

        // Discovery is not admission: activating straight from Discovered must be refused.
        var illegal = episode.Catalog.Activate(episode.Id, "implementations-analyst", actorIsReflection: false);

        Assert.False(illegal.Accepted);
        Assert.StartsWith(FactoryAdmissionCatalog.IllegalTransitionCategory, illegal.Reason, StringComparison.Ordinal);
        Assert.Equal(AdmissionState.Discovered, illegal.State);
        Assert.Equal(AdmissionState.Discovered, episode.Catalog.StateOf(episode.Id));

        // A terminal state admits nothing.
        episode.Catalog.Inspect(episode.Id);
        episode.Catalog.Resolve(episode.Id);
        episode.Catalog.Admit(episode.Id);
        // HealthTest performs the load itself; loading first would make that an illegal self-transition.
        episode.Catalog.HealthTest(episode.Id, () => true);
        Assert.True(episode.Catalog.Activate(episode.Id, "implementations-analyst", false).Accepted);
        Assert.True(episode.Catalog.DrainAndUnload(episode.Id).Accepted);

        var afterUnload = episode.Catalog.Activate(episode.Id, "implementations-analyst", false);
        Assert.False(afterUnload.Accepted);
        Assert.Equal(AdmissionState.Unloaded, episode.Catalog.StateOf(episode.Id));
    }

    [Fact]
    public void Inspection_GivenMissingDependencyOrIncompatibleApi_RejectsBeforeLoad()
    {
        var catalog = new FactoryAdmissionCatalog();
        var dependent = new CapabilityPackage(
            new PackageId("p-dependent"),
            new CapabilityId("capability.dependent"),
            new CapabilityVersion("1.0.0"),
            "sha256:1111",
            [new PackageDependency(new CapabilityId("capability.missing"), new CapabilityVersion("2.0.0"))],
            "packages/dependent.dll",
            "implementations-analyst");

        catalog.Discover(dependent);

        var inspection = catalog.Inspect(dependent.PackageId);

        Assert.False(inspection.Accepted);
        Assert.Equal(AdmissionState.Rejected, catalog.StateOf(dependent.PackageId));
        Assert.Contains("capability.missing@2.0.0", catalog.RejectionOf(dependent.PackageId), StringComparison.Ordinal);

        // A package whose content hash is absent is equally inadmissible.
        var unhashed = dependent with
        {
            PackageId = new PackageId("p-unhashed"),
            CapabilityId = new CapabilityId("capability.unhashed"),
            ContentHash = string.Empty,
            Dependencies = []
        };
        catalog.Discover(unhashed);
        Assert.False(catalog.Inspect(unhashed.PackageId).Accepted);
        Assert.Equal(FactoryAdmissionCatalog.InvalidContentHashCategory, catalog.RejectionOf(unhashed.PackageId));
    }

    [Fact]
    public void Admission_GivenFailingHealthTest_NeverBecomesActive()
    {
        var episode = NewPackage("p-sick");
        episode.Admit();

        var health = episode.Catalog.HealthTest(episode.Id, () => false);

        Assert.False(health.Accepted);
        Assert.Equal(AdmissionState.Rejected, episode.Catalog.StateOf(episode.Id));
        Assert.Equal(FactoryAdmissionCatalog.HealthTestFailedCategory, episode.Catalog.RejectionOf(episode.Id));

        // A rejected package cannot be activated afterwards.
        var activation = episode.Catalog.Activate(episode.Id, "implementations-analyst", false);
        Assert.False(activation.Accepted);
        Assert.Null(episode.Catalog.ActivePackageId);
    }

    [Fact]
    public void Reflection_GivenCandidatePackageSubmission_CannotApproveOrPreAdmitReference()
    {
        var episode = NewPackage("p-reflection", role: "requirements-analyst");

        // The candidate can be submitted and inspected, but not activated by its author.
        Assert.True(episode.Catalog.Discover(episode.Package).Accepted);
        episode.Admit();
        episode.Catalog.HealthTest(episode.Id, () => true);

        var selfActivation = episode.Catalog.Activate(episode.Id, "requirements-analyst", actorIsReflection: true);

        Assert.False(selfActivation.Accepted);
        Assert.StartsWith(
            FactoryAdmissionCatalog.SelfInstallRefusedCategory,
            selfActivation.Reason,
            StringComparison.Ordinal);
        Assert.Null(episode.Catalog.ActivePackageId);
    }

    [Fact]
    public void Drain_GivenLiveLease_BlocksUnloadAndRecordsTimeout()
    {
        var episode = NewPackage("p-lease");
        Assert.True(episode.Activate().Accepted);

        episode.Catalog.AcquireLease(episode.Id);
        var blocked = episode.Catalog.DrainAndUnload(episode.Id);

        Assert.False(blocked.Accepted);
        Assert.StartsWith(FactoryAdmissionCatalog.LiveLeaseBlockingDrainCategory, blocked.Reason, StringComparison.Ordinal);
        Assert.Equal(AdmissionState.Active, episode.Catalog.StateOf(episode.Id));

        // Once the lease drains, the unload proceeds.
        episode.Catalog.ReleaseLease(episode.Id);
        var unloaded = episode.Catalog.DrainAndUnload(episode.Id);

        Assert.True(unloaded.Accepted);
        Assert.Equal(AdmissionState.Unloaded, episode.Catalog.StateOf(episode.Id));
    }

    [Fact]
    public void Rollback_GivenRejectedVersion_RestoresRoutingAndRetainsInspectionRecord()
    {
        var first = NewPackage("p-v1");
        Assert.True(first.Activate().Accepted);
        Assert.Equal("p-v1", first.Catalog.ActivePackageId);

        // A second version is admitted and promoted over the first.
        var second = new CapabilityPackage(
            new PackageId("p-v2"),
            first.Package.CapabilityId,
            new CapabilityVersion("2.0.0"),
            "sha256:2222",
            [],
            "packages/p-v2.dll",
            "implementations-analyst");

        first.Catalog.Discover(second);
        first.Catalog.Inspect(second.PackageId);
        first.Catalog.Resolve(second.PackageId);
        first.Catalog.Admit(second.PackageId);
        first.Catalog.HealthTest(second.PackageId, () => true);
        Assert.True(first.Catalog.Activate(second.PackageId, "implementations-analyst", false).Accepted);
        Assert.Equal("p-v2", first.Catalog.ActivePackageId);

        var rollback = first.Catalog.RollBack();

        Assert.True(rollback.Accepted);
        Assert.Equal("p-v1", first.Catalog.ActivePackageId);

        // The rolled-back package and its record are retained.
        Assert.True(first.Catalog.IsRetained(second.PackageId));
        Assert.Equal(AdmissionState.Active, first.Catalog.StateOf(second.PackageId));
    }

    [Fact]
    public void Versioning_GivenPromotedVersion_PinnedSessionKeepsOriginalResolution()
    {
        var episode = NewPackage("p-v1-pin");
        Assert.True(episode.Activate().Accepted);

        // A session starts work against the active version and pins it.
        var inFlight = new SessionId("session-in-flight");
        Assert.True(episode.Catalog.PinSession(inFlight));
        Assert.Equal("p-v1-pin", episode.Catalog.ResolveForSession(inFlight));

        var next = new CapabilityPackage(
            new PackageId("p-v2-pin"),
            episode.Package.CapabilityId,
            new CapabilityVersion("2.0.0"),
            "sha256:3333",
            [],
            "packages/p-v2-pin.dll",
            "implementations-analyst");

        episode.Catalog.Discover(next);
        episode.Catalog.Inspect(next.PackageId);
        episode.Catalog.Resolve(next.PackageId);
        episode.Catalog.Admit(next.PackageId);
        episode.Catalog.HealthTest(next.PackageId, () => true);
        Assert.True(episode.Catalog.Activate(next.PackageId, "implementations-analyst", false).Accepted);

        // The in-flight session keeps its original resolution; a new session gets the promoted one.
        Assert.Equal("p-v1-pin", episode.Catalog.ResolveForSession(inFlight));
        Assert.True(episode.Catalog.IsStaleForSession(inFlight));

        var freshSession = new SessionId("session-fresh");
        Assert.Equal("p-v2-pin", episode.Catalog.ResolveForSession(freshSession));
        Assert.False(episode.Catalog.IsStaleForSession(freshSession));

        Assert.True(episode.Catalog.PinSession(freshSession));
        Assert.Equal("p-v2-pin", episode.Catalog.ResolveForSession(freshSession));
        Assert.False(episode.Catalog.IsStaleForSession(freshSession));
    }
    [Fact]
    public void ReflectionSelfInstall_GivenDirectCatalogWrite_IsRefusedAndRecorded()
    {
        var episode = NewPackage("p-selfinstall", role: "operations-analyst");
        episode.Admit();
        episode.Catalog.HealthTest(episode.Id, () => true);

        // The reflection principal tries to force its own package active.
        var attempt = episode.Catalog.Activate(episode.Id, "operations-analyst", actorIsReflection: true);

        Assert.False(attempt.Accepted);
        Assert.Null(episode.Catalog.ActivePackageId);

        // The attempt is recorded rather than silently dropped.
        Assert.Contains(
            FactoryAdmissionCatalog.SelfInstallRefusedCategory,
            episode.Catalog.RejectionOf(episode.Id),
            StringComparison.Ordinal);
        Assert.Contains("operations-analyst", episode.Catalog.RejectionOf(episode.Id), StringComparison.Ordinal);
    }
}
