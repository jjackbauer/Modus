using System.Text.Json;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Privacy;

namespace Wip.Runtime.Runtime;

/// <summary>
/// Artifact-backed session memory (WP-3.4). Each session has one JSON memory artifact
/// (<c>agent-memory.json</c>); appends rewrite it with the retained tail so artifacts stay
/// inspectable and retention is bounded. WP-5.2: message content and tool-call arguments are
/// redacted before persistence and on load.
/// </summary>
public sealed class ArtifactAgentMemory : IAgentMemory
{
    private const string MemoryFileName = "agent-memory.json";
    private const string ProducerType = "ArtifactAgentMemory";
    private const string ProducerVersion = "1.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IArtifactStore _artifactStore;
    private readonly AgentMemoryOptions _options;
    private readonly ISecretRedactor _redactor;

    public ArtifactAgentMemory(
        IArtifactStore artifactStore,
        AgentMemoryOptions? options = null,
        ISecretRedactor? redactor = null)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _options = options ?? new AgentMemoryOptions();
        _redactor = redactor ?? SecretRedactor.Default;
    }

    public async ValueTask<AgentMemorySnapshot> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var descriptors = await _artifactStore.ListAsync(sessionId, cancellationToken);
        var memoryDescriptor = descriptors
            .Where(IsMemoryArtifact)
            .OrderByDescending(descriptor => descriptor.ProducedAtUtc)
            .FirstOrDefault();

        if (memoryDescriptor is null)
        {
            return AgentMemorySnapshot.Empty;
        }

        var content = await _artifactStore.LoadAsync(sessionId, memoryDescriptor.ArtifactId, cancellationToken);
        var document = JsonSerializer.Deserialize<AgentMemoryDocument>(content.Content, JsonOptions);
        var messages = (document?.Messages ?? Array.Empty<ChatMessage>())
            .Select(Sanitize)
            .ToArray();
        return new AgentMemorySnapshot(messages);
    }

    public async ValueTask AppendAsync(SessionId sessionId, ChatMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var snapshot = await LoadAsync(sessionId, cancellationToken);
        var messages = snapshot.Messages
            .Append(Sanitize(message))
            .TakeLast(Math.Max(1, _options.MaxMessages))
            .ToArray();

        var payload = JsonSerializer.Serialize(new AgentMemoryDocument(messages), JsonOptions);
        await _artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                new ArtifactId($"agent-memory-{Guid.NewGuid():N}"),
                ArtifactKind.Json,
                MemoryFileName,
                payload,
                ProducerType,
                ProducerVersion,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public async ValueTask<string> SummarizeAsync(SessionId sessionId, int maxMessages, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(sessionId, cancellationToken);
        var messages = snapshot.Messages;
        var take = Math.Max(1, maxMessages);
        var omitted = Math.Max(0, messages.Count - take);
        var tail = messages
            .TakeLast(take)
            .Select(static message => $"{message.Role}: {message.Content}");

        var summary = string.Join("\n", tail);
        return omitted > 0
            ? $"[{omitted} earlier message(s) omitted]\n{summary}"
            : summary;
    }

    /// <summary>
    /// Memory discovery accepts both the exact <c>agent-memory.json</c> file name written by simple
    /// stores and the artifact-id-suffixed name written by the local artifact store
    /// (<c>agent-memory-{artifactId}.json</c>), while still requiring the memory producer type.
    /// </summary>
    private static bool IsMemoryArtifact(ArtifactDescriptor descriptor)
    {
        if (!string.Equals(descriptor.ProducerType, ProducerType, StringComparison.Ordinal))
            return false;

        var fileName = Path.GetFileName(descriptor.RelativePath.Replace('\\', '/'));
        if (string.Equals(fileName, MemoryFileName, StringComparison.OrdinalIgnoreCase))
            return true;

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return fileNameWithoutExtension.StartsWith("agent-memory-", StringComparison.OrdinalIgnoreCase);
    }

    private ChatMessage Sanitize(ChatMessage message)
    {
        var content = _redactor.Redact(message.Content).Value;
        if (message.ToolCalls.Count == 0)
        {
            return string.Equals(content, message.Content, StringComparison.Ordinal)
                ? message
                : new ChatMessage(message.Role, content, message.ToolCalls, message.ToolCallId);
        }

        var toolCalls = new List<ToolCall>(message.ToolCalls.Count);
        foreach (var call in message.ToolCalls)
        {
            var rawArguments = call.Arguments.GetRawText();
            var redactedArguments = _redactor.Redact(rawArguments).Value;
            if (string.Equals(rawArguments, redactedArguments, StringComparison.Ordinal))
            {
                toolCalls.Add(call);
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(redactedArguments);
                toolCalls.Add(new ToolCall(call.Id, call.Name, document.RootElement.Clone()));
            }
            catch (JsonException)
            {
                // Redaction must never corrupt the persisted conversation; keep the original.
                toolCalls.Add(call);
            }
        }

        return new ChatMessage(message.Role, content, toolCalls, message.ToolCallId);
    }

    private sealed record AgentMemoryDocument(IReadOnlyList<ChatMessage> Messages);
}