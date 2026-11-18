using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Builder;
using Wip.Factory.Adaptation;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Profiles;
using Wip.Factory.Wip.Fanout;
using Wip.Factory.Wip.Roles;
using Wip.Factory.Contracts.Triggers;
using Wip.Factory.Modus;
using Wip.Factory.Persistence;
using Modus.Core.Plugins;
using Wip.Factory.Wip.Delivery;

namespace Wip.Factory.Host.Demo;

/// <summary>
/// D8: dispatch all nine roles from their own stage triggers and write one declared artifact per role.
/// </summary>
/// <remarks>
/// This demonstration proves <b>dispatch wiring and artifact production</b>. It does <b>not</b> prove that
/// a model drove any role: the execution port here is deterministic, and every artifact records
/// <c>modelDriven: false</c> so the claim can never be mistaken for model-driven evidence later.
/// </remarks>
public sealed class NineRoleDispatchDemo
{
    private sealed class DeterministicPort : IFactoryAgentExecutionPort
    {
        public ValueTask<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request,
            WipBuilder builder,
            IServiceProvider scopedServices,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentExecutionResult(
                AgentExecutionStatus.Completed,
                ChatMessage.Assistant("deterministic role output"),
                [],
                new ModelProviderUsage(12, 3),
                null));
    }

    private sealed class NoOpScheduler : IPluginScheduler
    {
        public void ScheduleRecurring(JobName jobName, TimeSpan interval, OperationName operation)
        {
        }

        public void ScheduleAt(JobName jobName, DateTimeOffset at, OperationName operation)
        {
        }
    }

    private sealed class UnusedChatModel : IChatModel
    {
        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("The chat model is not used by this demonstration.");

        public IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("The chat model is not used by this demonstration.");
    }

    public async Task<int> RunAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var services = new ServiceCollection();
        services.AddSingleton<IChatModel, UnusedChatModel>();
        await using var provider = services.BuildServiceProvider();

        var builder = new WipBuilder(services);

        var catalog = new CapabilityCatalog(
        [
            new CapabilityCatalogEntry(new CapabilityId("tool.repo-read"), new CapabilityVersion("1.0.0"))
        ]);

        var registryRoot = Path.Combine(outputDirectory, "registry");
        var registry = new LocalAgentProfileRegistry(registryRoot, catalog, FactoryRolePack.Definitions);

        foreach (var profile in FactoryRolePack.Profiles)
        {
            var published = await registry.PublishAsync(profile, cancellationToken);
            if (!published.Published)
            {
                Console.Error.WriteLine($"profile publish failed for {profile.AgentProfileId.Value}: {published.Reason}");
                return 1;
            }
        }

        var dispatcher = new FactoryAgentDispatcher(
            registry,
            FactoryRolePack.Definitions,
            catalog,
            new AgentProfilePermissionResolver(),
            new DeterministicPort(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            builder);

        // The strategic role is activated by a durable scheduled occurrence emitted through Modus'
        // scheduler (F-N.3); the operations role by a telemetry signal that actually breaches (F-G.9).
        var scheduleSource = new FactoryPluginScheduleSource(
            new NoOpScheduler(),
            new LocalScheduleOccurrenceStore(Path.Combine(outputDirectory, "schedules")));

        var dueAt = new DateTimeOffset(2026, 11, 18, 3, 0, 0, TimeSpan.Zero);
        var scheduleEvent = await scheduleSource.EmitDueAsync(
            new ScheduleId("schedule.strategic-architecture"),
            dueAt,
            "W-9",
            cancellationToken);

        var telemetryEvent = new FactoryTelemetrySignalSource().Emit(
            new TelemetrySignal("work-queue-depth", ObservedValue: 42, Threshold: 20, WorkItemId: new WorkItemId("W-9")));

        var triggers = new List<RoleTrigger>
        {
            RoleTrigger.Of(RoleTriggerKind.RequirementSubmitted, "event:factory.requirement.submitted.v1"),
            RoleTrigger.Of(RoleTriggerKind.AlignedRequirement, "artifact:aligned-requirement"),
            RoleTrigger.Of(RoleTriggerKind.ReadyWork, "event:factory.work.ready.v1"),
            RoleTrigger.Of(RoleTriggerKind.ProposedImplementation, "event:factory.implementation.proposed.v1"),
            RoleTrigger.Of(RoleTriggerKind.AcceptedReview, "event:factory.review.accepted.v1"),
            RoleTrigger.Of(RoleTriggerKind.MergeCandidate, "event:factory.release.completed.v1"),
            RoleTrigger.Of(RoleTriggerKind.ValidatedCandidate, "artifact:delivery-validation"),
            scheduleEvent is null
                ? RoleTrigger.Of(RoleTriggerKind.ScheduleOccurrence, "schedule:already-claimed")
                : RoleTrigger.Of(RoleTriggerKind.ScheduleOccurrence, "schedule:strategic-architecture@" + dueAt.ToString("O")),
            telemetryEvent is null
                ? RoleTrigger.Of(RoleTriggerKind.TelemetrySignal, "telemetry:within-band")
                : RoleTrigger.Of(RoleTriggerKind.TelemetrySignal, "telemetry:work-queue-depth=42>20")
        };

        if (scheduleEvent is null || telemetryEvent is null)
        {
            Console.Error.WriteLine("the scheduled occurrence or telemetry signal did not produce a trigger");
            return 1;
        }

        var runner = new NineRoleDispatchRunner(registry, FactoryRolePack.Definitions, dispatcher);
        var records = await runner.RunAsync(
            new SessionId("session-nine-role"),
            new WorkItemId("W-9"),
            modelDriven: false,
            triggers,
            cancellationToken);

        var artifacts = Path.Combine(outputDirectory, "artifacts");
        await NineRoleDispatchRunner.WriteArtifactsAsync(artifacts, records, cancellationToken);

        foreach (var record in records)
        {
            Console.WriteLine(
                $"{(record.Dispatched ? "dispatched" : "FAILED")} {record.AgentDefinitionId.Value,-46} " +
                $"stage={record.Artifact?.StageName,-24} artifact={record.Artifact?.ArtifactKind,-24} trigger={record.Artifact?.TriggerKind} {record.FailureCategory}");
        }

        // The release role's rollback is exercised for real: version 1 serves HTTP, version 2 takes
        // over, a broken version 3 is refused, and rollback restarts and re-probes version 1.
        var releaseDirectory = Path.Combine(outputDirectory, "releases");
        Directory.CreateDirectory(Path.Combine(releaseDirectory, "v1"));
        Directory.CreateDirectory(Path.Combine(releaseDirectory, "v2"));
        await File.WriteAllTextAsync(Path.Combine(releaseDirectory, "v1", "index.html"), "release 1", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(releaseDirectory, "v2", "index.html"), "release 2", cancellationToken);

        static int FreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        DeliveryCandidate Server(int version, int port) => new(
            version,
            "python",
            [
                "-m", "http.server", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--bind", "127.0.0.1",
                "--directory", Path.Combine(releaseDirectory, "v" + version)
            ],
            $"http://127.0.0.1:{port}/",
            TimeSpan.FromSeconds(25));

        using var delivery = new LocalProcessDeliveryEnvironment();
        var v1 = await delivery.ReleaseAsync(Server(1, FreePort()), cancellationToken);
        var v2 = await delivery.ReleaseAsync(Server(2, FreePort()), cancellationToken);
        var broken = await delivery.ReleaseAsync(
            new DeliveryCandidate(3, "python", ["-c", "raise SystemExit(1)"], $"http://127.0.0.1:{FreePort()}/", TimeSpan.FromSeconds(5)),
            cancellationToken);
        var rollback = await delivery.RollBackAsync(cancellationToken);
        var afterRollback = await delivery.ProbeAsync(cancellationToken);

        Console.WriteLine($"release v1  : released={v1.Released}");
        Console.WriteLine($"release v2  : released={v2.Released} live={delivery.LiveVersion}");
        Console.WriteLine($"release v3  : released={broken.Released} ({broken.Detail})");
        Console.WriteLine($"rollback    : rolledBack={rollback.RolledBack} restored={rollback.RestoredVersion} live={delivery.LiveVersion}");
        Console.WriteLine($"probe       : healthy={afterRollback.Healthy} version={afterRollback.Version}");

        var rollbackExercised = rollback.RolledBack && rollback.RestoredVersion == 1 && afterRollback.Healthy;
        var dispatched = records.Count(static record => record.Dispatched);
        Console.WriteLine($"roles dispatched: {dispatched}/{records.Count}");
        Console.WriteLine($"artifacts       : {Directory.GetFiles(artifacts, "*.json").Length} files in {Path.GetFullPath(artifacts)}");
        Console.WriteLine("modelDriven     : false (deterministic port; this demonstrates wiring, not model-driven execution)");

        return dispatched == records.Count && dispatched == 9 && rollbackExercised ? 0 : 1;
    }
}
