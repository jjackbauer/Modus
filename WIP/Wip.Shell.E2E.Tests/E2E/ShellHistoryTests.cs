using Xunit;
using Wip.Shell.Interactive;
using Wip.Runtime.Runtime;
using Wip.Abstractions.Sessions;
using Wip.Builder;
using Wip.Abstractions.Identifiers;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Wip.Shell.E2E.Tests.E2E;

public sealed class ShellHistoryTests : IAsyncLifetime
{
    private string _repositoryPath = null!;
    private WipBuilder _builder = null!;
    private WipRuntimeOrchestrator _orchestrator = null!;

    public async Task InitializeAsync()
    {
        _repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-shell-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_repositoryPath);

        _builder = CreateTestBuilder();
        _orchestrator = new WipRuntimeOrchestrator(
            new InMemorySessionStore(),
            new CollectingSessionEventPublisher());

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (Directory.Exists(_repositoryPath))
            Directory.Delete(_repositoryPath, recursive: true);
        await Task.CompletedTask;
    }

    [Fact]
    [Trait("ChecklistItem", "Complete persistent command history implementation on supported environments while preserving deterministic fallback when persistence is unavailable [depends on SH-007 history persistence hardening]")]
    public async Task ShellHistory_GivenTwoSequentialSessions_ExpectedPreviousCommandsRestoredOrDeterministicFallbackExplained()
    {
        // ARRANGE: First shell session
        using var firstInput = new StringReader("help\nexit\n");
        using var firstOutput = new StringWriter();
        var firstLoop = new WipShellCommandLoop(_orchestrator, firstInput, firstOutput, _builder, _repositoryPath);

        // ACT: Run first session
        var firstExitCode = await firstLoop.RunAsync(CancellationToken.None);

        // ASSERT: First session completes
        Assert.Equal(0, firstExitCode);
        var firstOutput_str = firstOutput.ToString();
        Assert.Contains("Command history: persistent", firstOutput_str, StringComparison.Ordinal);
        Assert.Contains("Available commands:", firstOutput_str);

        // ARRANGE: Check if history file exists after first session
        var historyFilePath = Path.Combine(_repositoryPath, ".wip", "command-history.ndjson");
        Assert.True(File.Exists(historyFilePath), "History file should exist on supported environments.");
        Assert.Contains(historyFilePath, firstOutput_str, StringComparison.Ordinal);
        var firstSessionHistoryCommands = await ReadCommandHistoryAsync(historyFilePath);
        Assert.Contains("help", firstSessionHistoryCommands);
        Assert.Contains("exit", firstSessionHistoryCommands);

        // ARRANGE: Second shell session that tries to read history
        using var secondInput = new StringReader("repo\nexit\n");
        using var secondOutput = new StringWriter();
        var secondLoop = new WipShellCommandLoop(_orchestrator, secondInput, secondOutput, _builder, _repositoryPath);

        // ACT: Run second session
        var secondExitCode = await secondLoop.RunAsync(CancellationToken.None);

        // ASSERT: Second session completes
        Assert.Equal(0, secondExitCode);
        var secondOutputStr = secondOutput.ToString();
        Assert.Contains("Command history: persistent", secondOutputStr, StringComparison.Ordinal);

        var combinedHistoryCommands = await ReadCommandHistoryAsync(historyFilePath);
        Assert.Contains("help", combinedHistoryCommands);
        Assert.Contains("repo", combinedHistoryCommands);
        Assert.Contains("exit", combinedHistoryCommands);
        Assert.True(combinedHistoryCommands.Count >= 4, "History should include commands from both shell sessions.");
    }

    [Fact]
    [Trait("ChecklistItem", "Complete persistent command history implementation on supported environments while preserving deterministic fallback when persistence is unavailable [depends on SH-007 history persistence hardening]")]
    public async Task ShellHistory_GivenUnavailablePersistenceDirectory_ExpectedFallbackToInMemoryHistory()
    {
        // ARRANGE: Make history file path a directory to force deterministic persistence fallback.
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-shell-history-fallback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);
        Directory.CreateDirectory(Path.Combine(repositoryPath, ".wip"));
        Directory.CreateDirectory(Path.Combine(repositoryPath, ".wip", "command-history.ndjson"));
        
        try
        {
            using var input = new StringReader("help\nexit\n");
            using var output = new StringWriter();
            var loop = new WipShellCommandLoop(_orchestrator, input, output, _builder, repositoryPath);

            // ACT: Run shell - should not crash when persistence is unavailable
            var exitCode = await loop.RunAsync(CancellationToken.None);

            // ASSERT: Shell completes successfully and reports deterministic fallback status.
            Assert.Equal(0, exitCode);
            
            var outputStr = output.ToString();
            Assert.Contains("Product:", outputStr);
            Assert.Contains("Command history: in-memory fallback (history-file-path-is-directory)", outputStr, StringComparison.Ordinal);
            Assert.Contains("Available commands:", outputStr);
            Assert.Contains("Hint: run 'help' to list available commands.", outputStr);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
            {
                try
                {
                    Directory.Delete(repositoryPath, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }
    }

    private WipBuilder CreateTestBuilder()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);
        return builder;
    }

    private static async Task<IReadOnlyList<string>> ReadCommandHistoryAsync(string historyFilePath)
    {
        var commands = new List<string>();
        var lines = await File.ReadAllLinesAsync(historyFilePath);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("Command", out var commandElement))
            {
                var command = commandElement.GetString();
                if (!string.IsNullOrWhiteSpace(command))
                    commands.Add(command);
            }
        }

        return commands;
    }

    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly Dictionary<SessionId, SessionSnapshot> _sessions = [];

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _sessions[snapshot.SessionId] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
        {
            _sessions.TryGetValue(sessionId, out var snapshot);
            return ValueTask.FromResult(snapshot);
        }
    }

    private sealed class CollectingSessionEventPublisher : ISessionEventPublisher
    {
        private readonly List<SessionEvent> _events = [];

        public IReadOnlyList<SessionEvent> Events => _events.AsReadOnly();

        public ValueTask PublishAsync(SessionEvent evt, CancellationToken cancellationToken)
        {
            _events.Add(evt);
            return ValueTask.CompletedTask;
        }
    }
}
