using Wip.Abstractions.Agents;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Memory;

public sealed class ArtifactAgentMemoryTests
{
    [Fact]
    public async Task AppendThenLoad_GivenNewMemoryInstance_ExpectedMessagesSurvive()
    {
        var store = new InMemoryArtifactStore();
        var session = new SessionId("session-memory");
        await new ArtifactAgentMemory(store).AppendAsync(session, ChatMessage.User("first"), CancellationToken.None);
        await new ArtifactAgentMemory(store).AppendAsync(session, ChatMessage.Assistant("second"), CancellationToken.None);

        var snapshot = await new ArtifactAgentMemory(store).LoadAsync(session, CancellationToken.None);

        Assert.Equal(2, snapshot.Messages.Count);
        Assert.Equal("first", snapshot.Messages[0].Content);
        Assert.Equal(ChatRole.Assistant, snapshot.Messages[1].Role);
    }

    [Fact]
    public async Task Load_GivenOtherSession_ExpectedIsolation()
    {
        var store = new InMemoryArtifactStore();
        await new ArtifactAgentMemory(store).AppendAsync(
            new SessionId("session-a"),
            ChatMessage.User("only-a"),
            CancellationToken.None);

        var other = await new ArtifactAgentMemory(store).LoadAsync(new SessionId("session-b"), CancellationToken.None);

        Assert.Empty(other.Messages);
    }

    [Fact]
    public async Task Append_GivenMaxMessages_ExpectedTailRetained()
    {
        var store = new InMemoryArtifactStore();
        var memory = new ArtifactAgentMemory(store, new AgentMemoryOptions { MaxMessages = 2 });
        var session = new SessionId("session-retention");

        await memory.AppendAsync(session, ChatMessage.User("one"), CancellationToken.None);
        await memory.AppendAsync(session, ChatMessage.User("two"), CancellationToken.None);
        await memory.AppendAsync(session, ChatMessage.User("three"), CancellationToken.None);

        var snapshot = await memory.LoadAsync(session, CancellationToken.None);

        Assert.Equal(["two", "three"], snapshot.Messages.Select(static message => message.Content).ToArray());
    }

    [Fact]
    public async Task Summarize_GivenOmittedMessages_ExpectedMarkerAndTail()
    {
        var store = new InMemoryArtifactStore();
        var memory = new ArtifactAgentMemory(store);
        var session = new SessionId("session-summary");

        await memory.AppendAsync(session, ChatMessage.User("one"), CancellationToken.None);
        await memory.AppendAsync(session, ChatMessage.Assistant("two"), CancellationToken.None);
        await memory.AppendAsync(session, ChatMessage.User("three"), CancellationToken.None);

        var summary = await memory.SummarizeAsync(session, maxMessages: 1, CancellationToken.None);

        Assert.Contains("[2 earlier message(s) omitted]", summary, StringComparison.Ordinal);
        Assert.Contains("User: three", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Append_GivenMessage_ExpectedMemoryArtifactIsInspectable()
    {
        var store = new InMemoryArtifactStore();
        var session = new SessionId("session-artifact");

        await new ArtifactAgentMemory(store).AppendAsync(session, ChatMessage.User("hello"), CancellationToken.None);

        var descriptor = Assert.Single(await store.ListAsync(session, CancellationToken.None));
        Assert.EndsWith("agent-memory.json", descriptor.RelativePath, StringComparison.Ordinal);
        var content = await store.LoadAsync(session, descriptor.ArtifactId, CancellationToken.None);
        Assert.Contains("\"hello\"", content.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Append_GivenMessageContainingApiKey_ExpectedPersistedArtifactIsRedacted()
    {
        var store = new InMemoryArtifactStore();
        var session = new SessionId("session-redaction");

        await new ArtifactAgentMemory(store).AppendAsync(
            session,
            ChatMessage.User("use sk-abcdef0123456789abcdef and Bearer abcdefghijklmnop1234 to call the API"),
            CancellationToken.None);

        var descriptor = Assert.Single(await store.ListAsync(session, CancellationToken.None));
        var content = await store.LoadAsync(session, descriptor.ArtifactId, CancellationToken.None);
        Assert.DoesNotContain("sk-abcdef0123456789abcdef", content.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer abcdefghijklmnop1234", content.Content, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", content.Content, StringComparison.Ordinal);

        var snapshot = await new ArtifactAgentMemory(store).LoadAsync(session, CancellationToken.None);
        Assert.DoesNotContain("sk-abcdef0123456789abcdef", snapshot.Messages[0].Content, StringComparison.Ordinal);
    }

    private sealed class InMemoryArtifactStore : IArtifactStore
    {
        private readonly List<(ArtifactDescriptor Descriptor, ArtifactContent Content)> _items = [];

        public ValueTask<ArtifactDescriptor> SaveAsync(
            SessionId sessionId,
            ArtifactContent artifact,
            CancellationToken cancellationToken)
        {
            var descriptor = new ArtifactDescriptor(
                artifact.ArtifactId,
                sessionId,
                artifact.Kind,
                $".wip/artifacts/{sessionId.Value}/{artifact.FileName}",
                artifact.ProducerType,
                artifact.ProducerVersion,
                artifact.ProducedAtUtc);

            _items.Add((descriptor, artifact));
            return ValueTask.FromResult(descriptor);
        }

        public ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(
            SessionId sessionId,
            CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<ArtifactDescriptor>>(
                _items
                    .Where(item => item.Descriptor.SessionId.Equals(sessionId))
                    .Select(item => item.Descriptor)
                    .ToArray());

        public ValueTask<ArtifactContent> LoadAsync(
            SessionId sessionId,
            ArtifactId artifactId,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                _items.Single(item => item.Descriptor.SessionId.Equals(sessionId)
                    && item.Descriptor.ArtifactId.Equals(artifactId)).Content);
    }
}