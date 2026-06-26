using System.Collections.Concurrent;
using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class WipRuntimeValidationReportTests
{
    [Fact]
    public async Task ValidateAsync_GivenConfiguredCommandResults_PersistsOrderedCommandRollupWithEvidenceSummaries()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-runtime-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new InMemorySessionStore();
            var publisher = new CollectingSessionEventPublisher();
            var artifactStore = new RecordingArtifactStore();
            var orchestrator = new WipRuntimeOrchestrator(store, publisher);

            var session = await orchestrator.StartSessionAsync(
                workflowId: new WorkflowId("workflow.linear"),
                repositoryPath: repositoryPath,
                worktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "validation"),
                cancellationToken: CancellationToken.None);

            await orchestrator.TransitionAsync(session.SessionId, SessionState.Editing, CancellationToken.None);

            var result = await orchestrator.ValidateAsync(
                session.SessionId,
                new SessionValidationRequest(
                    BuildSucceeded: true,
                    TestSucceeded: true,
                    DiffHash: "diff-123",
                    Summary: "dotnet build/test passed",
                    CommandResults: [
                        new SessionValidationCommandResult(
                            Command: "dotnet build WIP/Wip.Runtime/Wip.Runtime.csproj --nologo -v minimal",
                            ExitCode: 0,
                            StandardOutput: "build line 1\r\nbuild line 2\r\nbuild line 3\r\nbuild line 4",
                            StandardError: string.Empty,
                            TimedOut: false,
                            StartedAtUtc: new DateTimeOffset(2026, 5, 31, 10, 0, 0, TimeSpan.Zero),
                            CompletedAtUtc: new DateTimeOffset(2026, 5, 31, 10, 0, 1, TimeSpan.Zero)),
                        new SessionValidationCommandResult(
                            Command: "dotnet test WIP/Wip.Runtime.Tests/Wip.Runtime.Tests.csproj --no-build --nologo -v minimal",
                            ExitCode: 0,
                            StandardOutput: "test line 1\n test line 2\n test line 3\n test line 4",
                            StandardError: "warn line 1\nwarn line 2\nwarn line 3\nwarn line 4",
                            TimedOut: false,
                            StartedAtUtc: new DateTimeOffset(2026, 5, 31, 10, 1, 0, TimeSpan.Zero),
                            CompletedAtUtc: new DateTimeOffset(2026, 5, 31, 10, 1, 1, TimeSpan.Zero))]),
                artifactStore,
                CancellationToken.None);

            Assert.True(result.Succeeded);

            var saved = Assert.Single(artifactStore.SavedArtifacts);
            using var document = JsonDocument.Parse(saved.Content);
            var root = document.RootElement;

            Assert.Equal("diff-123", root.GetProperty("DiffHash").GetString());

            var commandResults = root.GetProperty("CommandResults").EnumerateArray().ToArray();
            Assert.Equal(2, commandResults.Length);
            Assert.Equal(1, commandResults[0].GetProperty("Sequence").GetInt32());
            Assert.Equal(2, commandResults[1].GetProperty("Sequence").GetInt32());
            Assert.True(commandResults[0].GetProperty("Succeeded").GetBoolean());
            Assert.True(commandResults[1].GetProperty("Succeeded").GetBoolean());
            Assert.Equal("build line 1 | build line 2 | build line 3", commandResults[0].GetProperty("StandardOutputSummary").GetString());
            Assert.Equal(string.Empty, commandResults[0].GetProperty("StandardErrorSummary").GetString());
            Assert.Equal("test line 1 | test line 2 | test line 3", commandResults[1].GetProperty("StandardOutputSummary").GetString());
            Assert.Equal("warn line 1 | warn line 2 | warn line 3", commandResults[1].GetProperty("StandardErrorSummary").GetString());

            var commandRollup = root.GetProperty("CommandRollup").EnumerateArray().ToArray();
            Assert.Equal(2, commandRollup.Length);
            Assert.Equal(1, commandRollup[0].GetProperty("Sequence").GetInt32());
            Assert.Equal(2, commandRollup[1].GetProperty("Sequence").GetInt32());
            Assert.True(commandRollup[0].GetProperty("Succeeded").GetBoolean());
            Assert.True(commandRollup[1].GetProperty("Succeeded").GetBoolean());
            Assert.Equal("build line 1 | build line 2 | build line 3", commandRollup[0].GetProperty("StandardOutputSummary").GetString());
            Assert.Equal("warn line 1 | warn line 2 | warn line 3", commandRollup[1].GetProperty("StandardErrorSummary").GetString());
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAsync_GivenRuntimeDotNetValidationRequest_PersistsAuthoritativeBuildAndTestEvidence()
    {
        await using var fixture = await TempValidationWorkspace.CreateAsync();

        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var artifactStore = new RecordingArtifactStore();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);

        var session = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: fixture.RepositoryPath,
            worktreePath: fixture.RepositoryPath,
            cancellationToken: CancellationToken.None);

        await orchestrator.TransitionAsync(session.SessionId, SessionState.Editing, CancellationToken.None);

        var result = await orchestrator.ValidateAsync(
            session.SessionId,
            new SessionValidationRequest(
                BuildSucceeded: false,
                TestSucceeded: false,
                DiffHash: "diff-123",
                Summary: "authoritative runtime validation",
                RuntimeValidation: new SessionRuntimeDotNetValidationRequest(
                    BuildProjectPath: "src/Example/Example.csproj",
                    TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
                    DotNetExecutablePath: fixture.DotNetStubPath)),
            artifactStore,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("diff-123", result.DiffHash);

        var saved = Assert.Single(artifactStore.SavedArtifacts);
        using var document = JsonDocument.Parse(saved.Content);
        var root = document.RootElement;

        Assert.Equal("diff-123", root.GetProperty("DiffHash").GetString());
        Assert.Equal(0, root.GetProperty("Build").GetProperty("ExitCode").GetInt32());
        Assert.Equal(0, root.GetProperty("Test").GetProperty("ExitCode").GetInt32());
        Assert.True(root.GetProperty("Build").GetProperty("Succeeded").GetBoolean());
        Assert.True(root.GetProperty("Test").GetProperty("Succeeded").GetBoolean());

        var commandResults = root.GetProperty("CommandResults").EnumerateArray().ToArray();
        Assert.Equal(3, commandResults.Length);
        Assert.Equal(1, commandResults[0].GetProperty("Sequence").GetInt32());
        Assert.Equal(2, commandResults[1].GetProperty("Sequence").GetInt32());
        Assert.Equal(3, commandResults[2].GetProperty("Sequence").GetInt32());
        Assert.True(commandResults[0].GetProperty("Succeeded").GetBoolean());
        Assert.True(commandResults[1].GetProperty("Succeeded").GetBoolean());
        Assert.True(commandResults[2].GetProperty("Succeeded").GetBoolean());
        Assert.Contains(" restore src/Example/Example.csproj", commandResults[0].GetProperty("Command").GetString(), StringComparison.Ordinal);
        Assert.Contains(" build src/Example/Example.csproj", commandResults[1].GetProperty("Command").GetString(), StringComparison.Ordinal);
        Assert.Contains(" test tests/Example.Tests/Example.Tests.csproj --no-build", commandResults[2].GetProperty("Command").GetString(), StringComparison.Ordinal);

        var commandRollup = root.GetProperty("CommandRollup").EnumerateArray().ToArray();
        Assert.Equal(3, commandRollup.Length);
        Assert.Equal(1, commandRollup[0].GetProperty("Sequence").GetInt32());
        Assert.Equal(2, commandRollup[1].GetProperty("Sequence").GetInt32());
        Assert.Equal(3, commandRollup[2].GetProperty("Sequence").GetInt32());
        Assert.Contains("build src/Example/Example.csproj", root.GetProperty("Build").GetProperty("Command").GetString(), StringComparison.Ordinal);
        Assert.Contains("test tests/Example.Tests/Example.Tests.csproj --no-build", root.GetProperty("Test").GetProperty("Command").GetString(), StringComparison.Ordinal);
        Assert.Equal(["restore", "build", "test"], fixture.GetCommandInvocations());
    }

    [Fact]
    public async Task ValidateAsync_GivenRuntimeValidationAndReattach_PersistsReplayableEvidenceAndRestoresCorrelationContinuity()
    {
        await using var fixture = await TempValidationWorkspace.CreateAsync();

        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var artifactStore = new RecordingArtifactStore();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);

        var session = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: fixture.RepositoryPath,
            worktreePath: fixture.RepositoryPath,
            cancellationToken: CancellationToken.None);

        await orchestrator.TransitionAsync(session.SessionId, SessionState.Editing, CancellationToken.None);

        var validation = await orchestrator.ValidateAsync(
            session.SessionId,
            new SessionValidationRequest(
                BuildSucceeded: false,
                TestSucceeded: false,
                DiffHash: "diff-456",
                Summary: "authoritative validation evidence",
                RuntimeValidation: new SessionRuntimeDotNetValidationRequest(
                    BuildProjectPath: "src/Example/Example.csproj",
                    TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
                    DotNetExecutablePath: fixture.DotNetStubPath)),
            artifactStore,
            CancellationToken.None);

        Assert.True(validation.Succeeded);

        var saved = Assert.Single(artifactStore.SavedArtifacts);
        using var artifactDocument = JsonDocument.Parse(saved.Content);
        var artifactRoot = artifactDocument.RootElement;
        var commandResults = artifactRoot.GetProperty("CommandResults").EnumerateArray().ToArray();

        Assert.Equal(3, commandResults.Length);
        Assert.Equal(1, commandResults[0].GetProperty("Sequence").GetInt32());
        Assert.Equal(2, commandResults[1].GetProperty("Sequence").GetInt32());
        Assert.Equal(3, commandResults[2].GetProperty("Sequence").GetInt32());
        Assert.Contains("restore src/Example/Example.csproj", commandResults[0].GetProperty("Command").GetString(), StringComparison.Ordinal);
        Assert.Contains("build src/Example/Example.csproj", commandResults[1].GetProperty("Command").GetString(), StringComparison.Ordinal);
        Assert.Contains("test tests/Example.Tests/Example.Tests.csproj --no-build", commandResults[2].GetProperty("Command").GetString(), StringComparison.Ordinal);

        var commandRollup = artifactRoot.GetProperty("CommandRollup").EnumerateArray().ToArray();
        Assert.Equal(3, commandRollup.Length);
        Assert.Equal(1, commandRollup[0].GetProperty("Sequence").GetInt32());
        Assert.Equal(2, commandRollup[1].GetProperty("Sequence").GetInt32());
        Assert.Equal(3, commandRollup[2].GetProperty("Sequence").GetInt32());
        Assert.Equal(["restore", "build", "test"], fixture.GetCommandInvocations());

        using var persistedState = await ReadPersistedSessionStateAsync(fixture.RepositoryPath, session.SessionId);
        var stateRoot = persistedState.RootElement;
        var persistedCorrelationId = stateRoot.GetProperty("ValidationCorrelationId").GetString();

        Assert.Equal(saved.Descriptor.ArtifactId.Value, stateRoot.GetProperty("ValidationArtifactId").GetString());
        Assert.Equal(saved.Descriptor.RelativePath, stateRoot.GetProperty("ValidationArtifactPath").GetString());
        Assert.False(string.IsNullOrWhiteSpace(persistedCorrelationId));
        Assert.Equal("Passed", stateRoot.GetProperty("ValidationStatus").GetString());

        var validationEvent = publisher.Events.Single(e => e.Kind == SessionEventKind.ValidationCompleted);
        Assert.Equal(persistedCorrelationId, validationEvent.CorrelationId);

        var journalCorrelationId = await ReadValidationCorrelationIdAsync(fixture.RepositoryPath, session.SessionId);
        Assert.Equal(persistedCorrelationId, journalCorrelationId);

        var attached = await orchestrator.AttachSessionAsync(fixture.RepositoryPath, session.SessionId, CancellationToken.None);
        Assert.Equal(session.SessionId, attached.SessionId);

        Assert.True(await orchestrator.DetachSessionAsync(CancellationToken.None));

        var restoredOrchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new CollectingSessionEventPublisher());
        var restored = await restoredOrchestrator.AttachSessionAsync(fixture.RepositoryPath, session.SessionId, CancellationToken.None);

        Assert.Equal(saved.Descriptor.ArtifactId.Value, restored.ValidationArtifactId);
        Assert.Equal(saved.Descriptor.RelativePath, restored.ValidationArtifactPath);
        Assert.Equal(persistedCorrelationId, restored.ValidationCorrelationId);
        Assert.Equal(SessionValidationStatus.Passed, restored.ValidationStatus);
    }

    [Fact]
    public async Task HostLifecycle_GivenValidationPasses_ExpectedReadinessTransitionsToHealthyWithExecutionEvidence()
    {
        await using var fixture = await TempValidationWorkspace.CreateAsync();
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var artifactStore = new RecordingArtifactStore();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);

        var session = await orchestrator.StartSessionAsync(new WorkflowId("workflow.linear"), fixture.RepositoryPath, fixture.RepositoryPath, CancellationToken.None);
        await orchestrator.TransitionAsync(session.SessionId, SessionState.Editing, CancellationToken.None);

        var validation = await orchestrator.ValidateAsync(
            session.SessionId,
            new SessionValidationRequest(
                BuildSucceeded: false,
                TestSucceeded: false,
                DiffHash: "diff-pass",
                Summary: "runtime lifecycle integration",
                RuntimeValidation: new SessionRuntimeDotNetValidationRequest(
                    BuildProjectPath: "src/Example/Example.csproj",
                    TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
                    DotNetExecutablePath: fixture.DotNetStubPath)),
            artifactStore,
            CancellationToken.None);

        Assert.True(validation.Succeeded);
        Assert.Equal(SessionValidationStatus.Passed, validation.Session.ValidationStatus);
        Assert.False(string.IsNullOrWhiteSpace(validation.Session.ValidationCorrelationId));
        Assert.Equal(["restore", "build", "test"], fixture.GetCommandInvocations());
    }

    [Fact]
    public async Task HostLifecycle_GivenValidationFails_ExpectedReadinessRemainsBlockedWithDeterministicFailureState()
    {
        await using var fixture = await TempValidationWorkspace.CreateAsync(testExitCode: 1);
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var artifactStore = new RecordingArtifactStore();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);

        var session = await orchestrator.StartSessionAsync(new WorkflowId("workflow.linear"), fixture.RepositoryPath, fixture.RepositoryPath, CancellationToken.None);
        await orchestrator.TransitionAsync(session.SessionId, SessionState.Editing, CancellationToken.None);

        var validation = await orchestrator.ValidateAsync(
            session.SessionId,
            new SessionValidationRequest(
                BuildSucceeded: true,
                TestSucceeded: true,
                DiffHash: "diff-fail",
                Summary: "runtime lifecycle failure proof",
                RuntimeValidation: new SessionRuntimeDotNetValidationRequest(
                    BuildProjectPath: "src/Example/Example.csproj",
                    TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
                    DotNetExecutablePath: fixture.DotNetStubPath)),
            artifactStore,
            CancellationToken.None);

        Assert.False(validation.Succeeded);
        Assert.Equal(SessionValidationStatus.Failed, validation.Session.ValidationStatus);
        Assert.Equal(SessionState.Validating, validation.Session.State);
    }

    [Fact]
    public async Task HostLifecycle_GivenRepeatedValidationRuns_ExpectedStateTransitionsRemainDeterministicAndIsolated()
    {
        await using var fixture = await TempValidationWorkspace.CreateAsync();
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var artifactStore = new RecordingArtifactStore();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);

        var session = await orchestrator.StartSessionAsync(new WorkflowId("workflow.linear"), fixture.RepositoryPath, fixture.RepositoryPath, CancellationToken.None);
        await orchestrator.TransitionAsync(session.SessionId, SessionState.Editing, CancellationToken.None);

        var first = await orchestrator.ValidateAsync(
            session.SessionId,
            new SessionValidationRequest(
                BuildSucceeded: false,
                TestSucceeded: false,
                DiffHash: "diff-1",
                Summary: "run-1",
                RuntimeValidation: new SessionRuntimeDotNetValidationRequest(
                    BuildProjectPath: "src/Example/Example.csproj",
                    TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
                    DotNetExecutablePath: fixture.DotNetStubPath)),
            artifactStore,
            CancellationToken.None);

        var second = await orchestrator.ValidateAsync(
            session.SessionId,
            new SessionValidationRequest(
                BuildSucceeded: false,
                TestSucceeded: false,
                DiffHash: "diff-2",
                Summary: "run-2",
                RuntimeValidation: new SessionRuntimeDotNetValidationRequest(
                    BuildProjectPath: "src/Example/Example.csproj",
                    TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
                    DotNetExecutablePath: fixture.DotNetStubPath)),
            artifactStore,
            CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(SessionState.Validating, second.Session.State);
        Assert.NotEqual(first.DiffHash, second.DiffHash);
        Assert.NotEqual(first.ValidationArtifact.ArtifactId, second.ValidationArtifact.ArtifactId);
        Assert.NotEmpty(publisher.Events.Where(static e => e.Kind == SessionEventKind.ValidationCompleted));
    }

    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<SessionId, SessionSnapshot> _sessions = new();

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _sessions[snapshot.SessionId] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
        {
            if (_sessions.TryGetValue(sessionId, out var snapshot))
                return ValueTask.FromResult<SessionSnapshot?>(snapshot);

            return ValueTask.FromResult<SessionSnapshot?>(null);
        }
    }

    private sealed class CollectingSessionEventPublisher : ISessionEventPublisher
    {
        public List<SessionEvent> Events { get; } = [];

        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
        {
            Events.Add(sessionEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingArtifactStore : IArtifactStore
    {
        public List<(ArtifactDescriptor Descriptor, string Content)> SavedArtifacts { get; } = [];

        public ValueTask<ArtifactDescriptor> SaveAsync(SessionId sessionId, ArtifactContent artifact, CancellationToken cancellationToken)
        {
            var descriptor = new ArtifactDescriptor(
                artifact.ArtifactId,
                sessionId,
                artifact.Kind,
                $"artifacts/{sessionId.Value}/{artifact.FileName}-{artifact.ArtifactId.Value}",
                artifact.ProducerType,
                artifact.ProducerVersion,
                artifact.ProducedAtUtc);

            SavedArtifacts.Add((descriptor, artifact.Content));
            return ValueTask.FromResult(descriptor);
        }

        public ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<ArtifactDescriptor>>(SavedArtifacts.Select(static item => item.Descriptor).ToArray());
    }

    private static async Task<string?> ReadValidationCorrelationIdAsync(string repositoryPath, SessionId sessionId)
    {
        var journalPath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, "event-journal.ndjson");

        foreach (var line in await File.ReadAllLinesAsync(journalPath, CancellationToken.None))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var document = JsonDocument.Parse(line);
            if (!string.Equals(document.RootElement.GetProperty("Kind").GetString(), SessionEventKind.ValidationCompleted.ToString(), StringComparison.Ordinal))
                continue;

            return document.RootElement.GetProperty("CorrelationId").GetString();
        }

        return null;
    }

    private static async Task<JsonDocument> ReadPersistedSessionStateAsync(string repositoryPath, SessionId sessionId)
    {
        var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, "session-state.json");
        var payload = await File.ReadAllTextAsync(statePath, CancellationToken.None);
        return JsonDocument.Parse(payload);
    }

    private sealed class TempValidationWorkspace : IAsyncDisposable
    {
        private const string CommandLogFileName = "dotnet-invocations.log";

        private TempValidationWorkspace(string repositoryPath, string dotNetStubPath)
        {
            RepositoryPath = repositoryPath;
            DotNetStubPath = dotNetStubPath;
        }

        public string RepositoryPath { get; }

        public string DotNetStubPath { get; }

        public IReadOnlyList<string> GetCommandInvocations()
        {
            var logPath = Path.Combine(RepositoryPath, CommandLogFileName);
            if (!File.Exists(logPath))
                return [];

            return File.ReadLines(logPath).ToArray();
        }

        public static async ValueTask<TempValidationWorkspace> CreateAsync(int testExitCode = 0)
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-runtime-validation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);
            Directory.CreateDirectory(Path.Combine(repositoryPath, "src", "Example"));
            Directory.CreateDirectory(Path.Combine(repositoryPath, "tests", "Example.Tests"));

            await File.WriteAllTextAsync(Path.Combine(repositoryPath, "src", "Example", "Example.csproj"), "<Project />", CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(repositoryPath, "tests", "Example.Tests", "Example.Tests.csproj"), "<Project />", CancellationToken.None);

            var dotNetStubPath = Path.Combine(repositoryPath, "dotnet.cmd");
            var script =
                "@echo off\r\n" +
                "set args=%*\r\n" +
                "if not \"%args:version=%\"==\"%args%\" (\r\n" +
                "  echo 10.0.100\r\n" +
                "  exit /b 0\r\n" +
                ")\r\n" +
                "set cmd=%1\r\n" +
                $"echo %cmd%>>{CommandLogFileName}\r\n" +
                "if /I \"%cmd%\"==\"restore\" (\r\n" +
                "  echo Restore succeeded for %2\r\n" +
                "  exit /b 0\r\n" +
                ")\r\n" +
                "if /I \"%cmd%\"==\"build\" (\r\n" +
                "  echo Build succeeded for %2\r\n" +
                "  exit /b 0\r\n" +
                ")\r\n" +
                "if /I \"%cmd%\"==\"test\" (\r\n" +
                "  echo Test succeeded for %2\r\n" +
                $"  exit /b {testExitCode}\r\n" +
                ")\r\n" +
                "echo Unsupported command 1>&2\r\n" +
                "exit /b 99\r\n";
            await File.WriteAllTextAsync(dotNetStubPath, script, CancellationToken.None);

            return new TempValidationWorkspace(repositoryPath, dotNetStubPath);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (Directory.Exists(RepositoryPath))
                    Directory.Delete(RepositoryPath, recursive: true);
            }
            catch
            {
            }

            return ValueTask.CompletedTask;
        }
    }
}