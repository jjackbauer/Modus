using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Builder;
using Wip.Factory.Adaptation;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Profiles;
using Wip.Factory.Wip.Fanout;
using Wip.Runtime;
using Wip.Runtime.Runtime;

namespace Wip.Factory.Host.Demo;

/// <summary>
/// RISK-007: run one role end to end through the <b>real</b> DeepSeek provider using the production
/// execution port, and record the real model id and token counts in the artifact.
/// </summary>
public sealed class RealModelRoleDemo
{
    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly Dictionary<string, SessionSnapshot> _snapshots = new(StringComparer.Ordinal);

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _snapshots[snapshot.SessionId.Value] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult(_snapshots.TryGetValue(sessionId.Value, out var snapshot) ? snapshot : null);
    }

    private sealed class NoOpPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    public async Task<int> RunAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var credentials = LoadCredentials();
        if (credentials is null)
        {
            Console.Error.WriteLine("no DeepSeek credentials found in .secrets/deepseek.env or the environment");
            return 78;
        }

        var (baseUrl, apiKey) = credentials.Value;

        var sessionId = new SessionId("session-real-model");
        var store = new InMemorySessionStore();
        await store.SaveAsync(
            new SessionSnapshot(
                sessionId,
                new WorkflowId("workflow.linear"),
                SessionState.Editing,
                RepositoryPath: ".",
                WorktreePath: string.Empty,
                UpdatedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);

        var services = new ServiceCollection();
        services.AddDeepSeekChatModel(new DeepSeekModelProviderOptions(baseUrl, apiKey, TimeSpan.FromMinutes(2)));
        await using var provider = services.BuildServiceProvider();

        var builder = new WipBuilder(services);
        var catalog = new CapabilityCatalog(
        [
            new CapabilityCatalogEntry(new CapabilityId("tool.repo-read"), new CapabilityVersion("1.0.0"))
        ]);

        var registryRoot = Path.Combine(outputDirectory, "registry");
        var registry = new LocalAgentProfileRegistry(registryRoot, catalog, FactoryRolePack.Definitions);

        var requirementsProfile = FactoryRolePack.Profiles[0];
        var published = await registry.PublishAsync(requirementsProfile, cancellationToken);
        if (!published.Published)
        {
            Console.Error.WriteLine($"profile publish failed: {published.Reason}");
            return 1;
        }

        var orchestrator = new WipRuntimeOrchestrator(store, new NoOpPublisher());
        var dispatcher = new FactoryAgentDispatcher(
            registry,
            FactoryRolePack.Definitions,
            catalog,
            new AgentProfilePermissionResolver(),
            new WipRuntimeAgentExecutorPort(orchestrator),
            provider.GetRequiredService<IServiceScopeFactory>(),
            builder);

        var result = await dispatcher.ExecuteAsync(
            new FactoryWorkDispatch(
                sessionId,
                new WorkItemId("W-1042"),
                new StageName("requirements"),
                requirementsProfile.DefinitionId,
                new AgentProfileVersion(1),
                "Reply with exactly the single word ALIGNED and nothing else."),
            cancellationToken);

        var artifact = new
        {
            Role = "Requirements Analyst",
            AgentDefinitionId = requirementsProfile.DefinitionId.Value,
            ModelId = result.ModelId,
            Completed = result.Completed,
            FailureCategory = result.FailureCategory,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            ModelDriven = result.Completed && result.OutputTokens > 0 && !string.IsNullOrEmpty(result.ModelId),
            ExecutedVia = "WipRuntimeAgentExecutorPort -> WipRuntimeOrchestrator.RunAgentAsync"
        };

        var path = Path.Combine(outputDirectory, "real-model-role.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        Console.WriteLine($"role        : {artifact.Role}");
        Console.WriteLine($"modelId     : {result.ModelId}");
        Console.WriteLine($"completed   : {result.Completed} {result.FailureCategory}");
        Console.WriteLine($"tokens      : in={result.InputTokens} out={result.OutputTokens}");
        Console.WriteLine($"modelDriven : {artifact.ModelDriven}");
        Console.WriteLine($"artifact    : {Path.GetFullPath(path)}");

        return artifact.ModelDriven ? 0 : 1;
    }

    private static (Uri BaseUrl, string ApiKey)? LoadCredentials()
    {
        var environmentKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        var environmentBase = Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL");
        if (!string.IsNullOrWhiteSpace(environmentKey) && !string.IsNullOrWhiteSpace(environmentBase))
        {
            return (new Uri(environmentBase), environmentKey);
        }

        var secretsPath = Path.Combine(Directory.GetCurrentDirectory(), ".secrets", "deepseek.env");
        if (!File.Exists(secretsPath))
        {
            return null;
        }

        string? key = null;
        string? url = null;

        foreach (var line in File.ReadAllLines(secretsPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim().Trim('"').Trim('\'');

            if (string.Equals(name, "DEEPSEEK_API_KEY", StringComparison.Ordinal))
            {
                key = value;
            }
            else if (string.Equals(name, "DEEPSEEK_BASE_URL", StringComparison.Ordinal))
            {
                url = value;
            }
        }

        return key is null || url is null ? null : (new Uri(url), key);
    }
}
