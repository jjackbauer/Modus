using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;
using Wip.Runtime.Runtime;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Runtime;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Tests.Runtime;

/// <summary>
/// A deterministic <see cref="IChatModel"/> that records every request and replays a fixed script.
/// When the loop asks for more turns than the script holds, the last scripted message repeats.
/// </summary>
internal sealed class ScriptedChatModel : IChatModel
{
    private readonly ChatMessage[] _script;
    private readonly List<ChatRequest> _requests = [];

    public ScriptedChatModel(params ChatMessage[] script)
    {
        if (script.Length == 0)
        {
            throw new ArgumentException("A scripted model needs at least one response.", nameof(script));
        }

        _script = script;
    }

    public IReadOnlyList<ChatRequest> Requests => _requests;

    public ValueTask<ChatResponse> CompleteAsync(
        ChatRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        _requests.Add(request);
        var index = Math.Min(_requests.Count - 1, _script.Length - 1);
        return new ValueTask<ChatResponse>(new ChatResponse(_script[index], request.ModelId, "scripted"));
    }

    public async IAsyncEnumerable<StreamingChunk> StreamAsync(
        ChatRequest request,
        CapabilityContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }
}

/// <summary>
/// Session-isolated in-memory artifact store test double. List returns newest first so the memory
/// tail resolution is deterministic even when two saves share the same wall-clock timestamp.
/// </summary>
internal sealed class InMemoryArtifactStore : IArtifactStore
{
    private readonly Dictionary<string, List<StoredArtifact>> _sessions = new(StringComparer.Ordinal);

    public ValueTask<ArtifactDescriptor> SaveAsync(
        SessionId sessionId,
        ArtifactContent artifact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();

        var descriptor = new ArtifactDescriptor(
            artifact.ArtifactId,
            sessionId,
            artifact.Kind,
            artifact.FileName,
            artifact.ProducerType,
            artifact.ProducerVersion,
            artifact.ProducedAtUtc);

        if (!_sessions.TryGetValue(sessionId.Value, out var stored))
        {
            stored = [];
            _sessions[sessionId.Value] = stored;
        }

        stored.Add(new StoredArtifact(descriptor, artifact));
        return new ValueTask<ArtifactDescriptor>(descriptor);
    }

    public ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(sessionId.Value, out var stored))
        {
            return new ValueTask<IReadOnlyList<ArtifactDescriptor>>(Array.Empty<ArtifactDescriptor>());
        }

        var descriptors = new ArtifactDescriptor[stored.Count];
        for (var index = 0; index < stored.Count; index++)
        {
            descriptors[index] = stored[stored.Count - 1 - index].Descriptor;
        }

        return new ValueTask<IReadOnlyList<ArtifactDescriptor>>(descriptors);
    }

    public ValueTask<ArtifactContent> LoadAsync(
        SessionId sessionId,
        ArtifactId artifactId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_sessions.TryGetValue(sessionId.Value, out var stored))
        {
            foreach (var artifact in stored)
            {
                if (artifact.Descriptor.ArtifactId == artifactId)
                {
                    return new ValueTask<ArtifactContent>(artifact.Content);
                }
            }
        }

        throw new KeyNotFoundException(
            $"Artifact '{artifactId.Value}' was not found for session '{sessionId.Value}'.");
    }

    private sealed record StoredArtifact(ArtifactDescriptor Descriptor, ArtifactContent Content);
}

/// <summary>Denies every permission request and records what was asked for.</summary>
internal sealed class DenyingPermissionEvaluator : IPermissionEvaluator
{
    private readonly List<PermissionRequest> _requests = [];

    public IReadOnlyList<PermissionRequest> Requests => _requests;

    public PermissionDecision Evaluate(PermissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Add(request);
        return new PermissionDecision(
            request.Permission,
            Allowed: false,
            Audited: false,
            BuiltIn: false,
            Reason: "test-denial");
    }
}

/// <summary>
/// The real runtime pipeline used by the tests: registered tools plus a
/// <see cref="RuntimeToolInvoker"/>, <see cref="AgentExecutor"/>, <see cref="ArtifactAgentMemory"/>
/// over <see cref="InMemoryArtifactStore"/> and the <see cref="TholdemRuntimeAgent"/> under test.
/// </summary>
internal sealed class RuntimePipeline : IDisposable
{
    private readonly ServiceProvider _provider;

    private RuntimePipeline(
        ServiceProvider provider,
        ScriptedChatModel model,
        InMemoryArtifactStore artifacts,
        SessionHandStore handStore,
        SessionId sessionId,
        TholdemRuntimeAgent agent)
    {
        _provider = provider;
        Model = model;
        Artifacts = artifacts;
        HandStore = handStore;
        SessionId = sessionId;
        Agent = agent;
    }

    public ScriptedChatModel Model { get; }

    public InMemoryArtifactStore Artifacts { get; }

    public SessionHandStore HandStore { get; }

    public SessionId SessionId { get; }

    public TholdemRuntimeAgent Agent { get; }

    public static RuntimePipeline Create(
        ScriptedChatModel model,
        TholdemAgentOptions options,
        IPermissionEvaluator? permissions = null,
        int seat = 0,
        SessionHandStore? handStore = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);

        var store = handStore ?? new SessionHandStore();
        var artifacts = new InMemoryArtifactStore();
        var services = new ServiceCollection();
        services.AddSingleton(permissions ?? new PermissionEvaluator());
        services.AddSingleton(store);
        services.AddSingleton(new EquityTool(options.EquitySamples));
        services.AddSingleton(new PotOddsTool());
        services.AddSingleton(new HandHistoryTool(seat, store));
        services.AddSingleton(new OpponentStatsTool(seat, store));
        services.AddSingleton<IArtifactStore>(artifacts);

        var provider = services.BuildServiceProvider();
        var invoker = new RuntimeToolInvoker(provider, TholdemToolCatalog.CreateDescriptors());
        var executor = new AgentExecutor(model, invoker);
        var memory = new ArtifactAgentMemory(artifacts);
        var sessionId = new SessionId("tholdem-runtime-agent-test");
        var agent = new TholdemRuntimeAgent(
            executor,
            memory,
            sessionId,
            AppContext.BaseDirectory,
            options,
            TholdemToolCatalog.CreateDefinitions());

        return new RuntimePipeline(provider, model, artifacts, store, sessionId, agent);
    }

    public void Dispose() => _provider.Dispose();
}

internal static class RuntimeAgentTestSupport
{
    public const ulong SessionSeed = 987654321UL;

    public static ChatMessage Final(string content) => ChatMessage.Assistant(content);

    public static ChatMessage ToolTurn(params ToolCall[] calls) => ChatMessage.Assistant(string.Empty, calls);

    public static ToolCall CreateToolCall(string id, string name, string argumentsJson)
    {
        using var document = JsonDocument.Parse(argumentsJson);
        return new ToolCall(id, name, document.RootElement);
    }

    public static string TaskText(ChatRequest request) => request.Messages[^1].Content;

    public static string SystemText(ChatRequest request) => request.Messages[0].Content;

    public static PlayerObservation Observation(int seat = 0, ulong seed = SessionSeed, int handNumber = 1)
        => PlayerObservation.ForSeat(HeadsUpHandEngine.Start(seed, handNumber: handNumber), seat);

    public static PlayerObservation FlopObservation(int seat, ulong seed = SessionSeed)
    {
        var state = HeadsUpHandEngine.Start(seed);
        state = HeadsUpHandEngine.Apply(state, PokerAction.Call());
        state = HeadsUpHandEngine.Apply(state, PokerAction.Check());

        if (state.Street != Street.Flop || state.ActingSeat != seat)
        {
            throw new InvalidOperationException(
                $"Expected the flop with seat {seat} to act, observed {state.Street} and seat {state.ActingSeat}.");
        }

        return PlayerObservation.ForSeat(state, seat);
    }

    public static AgentTurnContext Context(PlayerObservation observation, int actionIndex = 0)
        => new(
            observation.HandNumber,
            observation.ActingSeat,
            actionIndex);

    public static string[] DescribePermissions(IReadOnlyList<ICapabilityDescriptor> descriptors)
        => descriptors
            .Select(descriptor =>
                $"{descriptor.CapabilityId.Value}=" +
                string.Join("+", descriptor.RequiredPermissions.Select(permission => permission.Value)))
            .ToArray();

    public static string[] CreateDefinitionsNames()
        => TholdemToolCatalog.CreateDefinitions().Select(definition => definition.Name).ToArray();
}
