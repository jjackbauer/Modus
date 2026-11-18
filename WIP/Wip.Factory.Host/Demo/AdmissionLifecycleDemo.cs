using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Admission;
using Wip.Factory.Contracts.Identifiers;

namespace Wip.Factory.Host.Demo;

/// <summary>
/// D9: the executable-capability admission lifecycle end to end. A candidate is inspected, admitted,
/// health-tested and activated; a second version is promoted while an in-flight session keeps its pin;
/// a live lease blocks the drain; rollback restores the previous version; and a reflection self-install
/// is refused.
/// </summary>
public sealed class AdmissionLifecycleDemo
{
    private const string Actor = "implementations-analyst";

    public async Task<int> RunAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var catalog = new FactoryAdmissionCatalog();
        var steps = new List<object>();

        var v1 = Package("p-capability-v1", "1.0.0", "sha256:aaaa1111");
        var v2 = Package("p-capability-v2", "2.0.0", "sha256:bbbb2222");

        // 1. Discovery through activation, in declared order.
        catalog.Discover(v1);
        steps.Add(Step("discover", catalog.StateOf(v1.PackageId)));

        var illegal = catalog.Activate(v1.PackageId, Actor, actorIsReflection: false);
        steps.Add(Step("activate-before-inspection", catalog.StateOf(v1.PackageId), illegal.Reason));

        if (illegal.Accepted)
        {
            return Fail("an undeclared transition was accepted");
        }

        catalog.Inspect(v1.PackageId);
        steps.Add(Step("inspect", catalog.StateOf(v1.PackageId)));

        catalog.Resolve(v1.PackageId);
        steps.Add(Step("resolve", catalog.StateOf(v1.PackageId)));

        catalog.Admit(v1.PackageId);
        steps.Add(Step("admit", catalog.StateOf(v1.PackageId)));

        var health = catalog.HealthTest(v1.PackageId, () => true);
        steps.Add(Step("health", catalog.StateOf(v1.PackageId), health.Reason));

        var activated = catalog.Activate(v1.PackageId, Actor, actorIsReflection: false);
        steps.Add(Step("activate", catalog.StateOf(v1.PackageId), activated.Reason));

        if (catalog.ActivePackageId != v1.PackageId.Value)
        {
            return Fail("activation did not promote the package");
        }

        // 2. An in-flight session pins the version it resolved.
        var inFlight = new SessionId("session-in-flight");
        catalog.PinSession(inFlight);

        // 3. A second version is promoted; the pinned session must not move.
        foreach (var step in new Action[]
                 {
                     () => catalog.Discover(v2),
                     () => catalog.Inspect(v2.PackageId),
                     () => catalog.Resolve(v2.PackageId),
                     () => catalog.Admit(v2.PackageId)
                 })
        {
            step();
        }

        catalog.HealthTest(v2.PackageId, () => true);
        catalog.Activate(v2.PackageId, Actor, actorIsReflection: false);

        var pinnedResolution = catalog.ResolveForSession(inFlight);
        var freshResolution = catalog.ResolveForSession(new SessionId("session-fresh"));
        steps.Add(new
        {
            step = "promote-v2",
            active = catalog.ActivePackageId,
            pinnedSessionResolves = pinnedResolution,
            newSessionResolves = freshResolution,
            pinnedSessionStale = catalog.IsStaleForSession(inFlight)
        });

        if (pinnedResolution != v1.PackageId.Value || freshResolution != v2.PackageId.Value)
        {
            return Fail("version pinning did not hold across promotion");
        }

        // 4. A live lease blocks the drain; releasing it lets the unload proceed.
        catalog.AcquireLease(v2.PackageId);
        var blocked = catalog.DrainAndUnload(v2.PackageId);
        steps.Add(Step("drain-with-lease", catalog.StateOf(v2.PackageId), blocked.Reason));

        if (blocked.Accepted)
        {
            return Fail("a live lease did not block the drain");
        }

        catalog.ReleaseLease(v2.PackageId);
        var unloaded = catalog.DrainAndUnload(v2.PackageId);
        steps.Add(Step("drain-after-release", catalog.StateOf(v2.PackageId), unloaded.Reason));

        // 5. Rollback restores the previous version and retains the rolled-back one.
        var rollback = catalog.RollBack();
        steps.Add(new
        {
            step = "rollback",
            accepted = rollback.Accepted,
            active = catalog.ActivePackageId,
            rolledBackRetained = catalog.IsRetained(v2.PackageId)
        });

        if (catalog.ActivePackageId != v1.PackageId.Value || !catalog.IsRetained(v2.PackageId))
        {
            return Fail("rollback did not restore and retain correctly");
        }

        // 6. A reflection principal cannot install its own package.
        var selfInstall = Package("p-self-install", "1.0.0", "sha256:cccc3333");
        catalog.Discover(selfInstall);
        catalog.Inspect(selfInstall.PackageId);
        catalog.Resolve(selfInstall.PackageId);
        catalog.Admit(selfInstall.PackageId);
        catalog.HealthTest(selfInstall.PackageId, () => true);

        var refused = catalog.Activate(selfInstall.PackageId, "requirements-analyst", actorIsReflection: true);
        steps.Add(new
        {
            step = "reflection-self-install",
            accepted = refused.Accepted,
            reason = refused.Reason,
            active = catalog.ActivePackageId
        });

        if (refused.Accepted || catalog.ActivePackageId != v1.PackageId.Value)
        {
            return Fail("a reflection principal installed its own package");
        }

        var path = Path.Combine(outputDirectory, "admission-lifecycle.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(new { steps, activePackage = catalog.ActivePackageId }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        foreach (var step in steps)
        {
            Console.WriteLine(JsonSerializer.Serialize(step));
        }

        Console.WriteLine($"active       : {catalog.ActivePackageId}");
        Console.WriteLine($"evidence     : {Path.GetFullPath(path)}");

        return 0;
    }

    private static CapabilityPackage Package(string id, string version, string hash) =>
        new(
            new PackageId(id),
            new CapabilityId("capability.factory.analysis"),
            new CapabilityVersion(version),
            hash,
            [],
            "packages/" + id + ".dll",
            Actor);

    private static object Step(string name, AdmissionState? state, string? reason = null)
        => new { step = name, state = state?.ToString(), reason };

    private static int Fail(string reason)
    {
        Console.Error.WriteLine(reason);
        return 1;
    }
}
