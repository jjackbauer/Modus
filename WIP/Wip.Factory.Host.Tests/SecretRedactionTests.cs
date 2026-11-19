using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Wip.Isolation;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// v0.4 FAC-P1-06.5 / K-14 companion: child-process output is redacted before it becomes a model observation or
/// evidence, and a redactor that cannot complete is a named refusal rather than raw output.
/// </summary>
public sealed class SecretRedactionTests
{
    private sealed class ScriptedRunner : IProcessRunner
    {
        private readonly string _output;

        public ScriptedRunner(string output) => _output = output;

        public ValueTask<BoundedExecutionResult> RunAsync(
            string command,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            ProcessBounds bounds,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new BoundedExecutionResult(
                Completed: true,
                ExitCode: 0,
                StandardOutput: _output,
                StandardError: "token=" + "another-secret-value-123",
                Elapsed: TimeSpan.Zero,
                TerminatedByWallTime: false,
                TerminatedByMemoryLimit: false,
                IsolationApplied: true));
    }

    private sealed class FailingRedactor : ISecretRedactor
    {
        public string Redact(string text, out int redactions)
        {
            redactions = 0;
            throw new SecretRedactionException("scanner-unavailable");
        }
    }

    private static string Worktree()
    {
        var path = Path.Combine(Path.GetTempPath(), "factory-redact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task Factory_GivenSecretInChildOutput_RedactsEvidence()
    {
        var worktree = Worktree();
        const string secret = "supersecretvalue123";
        var runner = new ScriptedRunner("api_key=" + secret + "\nsk-abcdefghijklmnop123456\n");
        var tool = new RunCommandTool(runner, "dotnet", new SecretRedactor([secret]));
        var context = new CapabilityContext(new SessionId("session-redact"), worktree);

        var result = await tool.ExecuteAsync(new RunCommandRequest("build", []), context, CancellationToken.None);

        Assert.DoesNotContain(secret, result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-abcdefghijklmnop123456", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains(SecretRedactor.RedactedMarker, result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("another-secret-value-123", result.StandardError, StringComparison.Ordinal);
        Assert.True(result.Redactions >= 3, $"expected at least three redactions, saw {result.Redactions}");
    }

    [Fact]
    public async Task Factory_GivenRedactorFailure_ReturnsNamedError()
    {
        var worktree = Worktree();
        var runner = new ScriptedRunner("api_key=supersecretvalue123");
        var tool = new RunCommandTool(runner, "dotnet", new FailingRedactor());
        var context = new CapabilityContext(new SessionId("session-redact"), worktree);

        var error = await Assert.ThrowsAsync<SecretRedactionException>(async () =>
            await tool.ExecuteAsync(new RunCommandRequest("build", []), context, CancellationToken.None));

        Assert.StartsWith(SecretRedactionException.Category + ":", error.Message, StringComparison.Ordinal);
        Assert.Equal("scanner-unavailable", error.Detail);
    }
}
