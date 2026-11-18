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
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class NineRoleDispatchTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wip-factory-nine-role-tests", Guid.NewGuid().ToString("N"));

    private static readonly CapabilityCatalog Catalog = new(
    [
        new CapabilityCatalogEntry(new CapabilityId("tool.repo-read"), new CapabilityVersion("1.0.0"))
    ]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class Marker
    {
    }

    private sealed class SuccessPort : IFactoryAgentExecutionPort
    {
        public List<string> InvokedTasks { get; } = [];

        public ValueTask<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request,
            WipBuilder builder,
            IServiceProvider scopedServices,
            CancellationToken cancellationToken = default)
        {
            InvokedTasks.Add(request.Task);
            return ValueTask.FromResult(new AgentExecutionResult(
                AgentExecutionStatus.Completed,
                ChatMessage.Assistant("role completed"),
                [],
                new ModelProviderUsage(4, 2),
                null));
        }
    }

    private sealed class UnusedChatModel : IChatModel
    {
        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("unused");

        public IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("unused");
    }

    [Fact]
    public async Task Role_GivenEachRoleTrigger_DispatchesAndPublishesItsDeclaredArtifact()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Marker>();
        services.AddSingleton<IChatModel, UnusedChatModel>();
        await using var provider = services.BuildServiceProvider();

        var builder = new WipBuilder(services);
        var registry = new LocalAgentProfileRegistry(_root, Catalog, FactoryRolePack.Definitions);

        foreach (var profile in FactoryRolePack.Profiles)
        {
            var published = await registry.PublishAsync(profile);
            Assert.True(published.Published, published.Reason);
        }

        var port = new SuccessPort();
        var dispatcher = new FactoryAgentDispatcher(
            registry,
            FactoryRolePack.Definitions,
            Catalog,
            new AgentProfilePermissionResolver(),
            port,
            provider.GetRequiredService<IServiceScopeFactory>(),
            builder);

        var runner = new NineRoleDispatchRunner(registry, FactoryRolePack.Definitions, dispatcher);
        var records = await runner.RunAsync(
            new SessionId("session-nine-role"),
            new WorkItemId("W-9"),
            modelDriven: false);

        Assert.Equal(9, records.Count);
        Assert.All(records, static record => Assert.True(record.Dispatched, record.FailureCategory));
        Assert.All(records, static record => Assert.NotNull(record.Artifact));

        // Every role was actually dispatched through the shared port, once each.
        Assert.Equal(9, port.InvokedTasks.Count);
        Assert.Equal(9, port.InvokedTasks.Distinct(StringComparer.Ordinal).Count());

        // Each role publishes a distinct declared artifact kind from its own stage.
        var artifactKinds = records.Select(static record => record.Artifact!.ArtifactKind).ToArray();
        Assert.Equal(9, artifactKinds.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("review-verdict", artifactKinds);
        Assert.Contains("release-authorization", artifactKinds);

        var stages = records.Select(static record => record.Artifact!.StageName).ToArray();
        Assert.Equal(
            [
                "requirements", "tactical-architecture", "implementation", "review", "delivery-decision",
                "delivery", "release", "strategic-architecture", "operations"
            ],
            stages);

        // Every artifact records the producing session and the pinned profile version.
        Assert.All(records, record =>
        {
            Assert.Equal("session-nine-role", record.Artifact!.ProducedBySession);
            Assert.Equal("1", record.Artifact.ProfileVersion);
        });

        // The run leaves one artifact file per role on disk.
        var artifactDirectory = Path.Combine(_root, "artifacts");
        await NineRoleDispatchRunner.WriteArtifactsAsync(artifactDirectory, records);
        Assert.Equal(9, Directory.GetFiles(artifactDirectory, "*.json").Length);
    }
}
