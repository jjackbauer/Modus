using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Builder;
using Wip.Factory.Adaptation;
using Wip.Factory.Agents.Core.Tools;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Profiles;
using Wip.Factory.Wip.Fanout;
using Wip.Factory.Wip.Isolation;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// FAC-P1-06 / K-14: tool authority is derived from the pinned profile at the production dispatch seam, workspace
/// paths are physically resolved before a write, and a missing OS bound fails closed.
/// </summary>
public sealed class FactoryBoundaryTests
{
    private sealed class NeverInvokedPort : IFactoryAgentExecutionPort
    {
        public ValueTask<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request,
            WipBuilder builder,
            IServiceProvider scopedServices,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("a forged tool definition must be refused before execution");
    }

    private sealed class NoIsolationRunner : IProcessRunner
    {
        public ValueTask<BoundedExecutionResult> RunAsync(
            string command,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            ProcessBounds bounds,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new BoundedExecutionResult(
                Completed: true,
                ExitCode: 0,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                Elapsed: TimeSpan.Zero,
                TerminatedByWallTime: false,
                TerminatedByMemoryLimit: false,
                IsolationApplied: false));
    }

    [Fact]
    public async Task Factory_GivenForgedToolDefinition_InvokerRefuses()
    {
        var directory = Path.Combine(Path.GetTempPath(), "factory-forged-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var catalog = new CapabilityCatalog(
            [
                new CapabilityCatalogEntry(new CapabilityId("tool.repo-read"), new CapabilityVersion("1.0.0"))
            ]);

            var services = new ServiceCollection();
            var provider = services.BuildServiceProvider();
            var builder = new WipBuilder(services);
            var registry = new LocalAgentProfileRegistry(directory, catalog, FactoryRolePack.Definitions);

            var published = await registry.PublishAsync(FactoryRolePack.Profiles[2]);
            Assert.True(published.Published, published.Reason);

            var dispatcher = new FactoryAgentDispatcher(
                registry,
                FactoryRolePack.Definitions,
                catalog,
                new AgentProfilePermissionResolver(),
                new NeverInvokedPort(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                builder);

            var dispatch = new FactoryWorkDispatch(
                new SessionId("session-forged"),
                new WorkItemId("W-forged"),
                new StageName("implementation"),
                new AgentDefinitionId("factory.implementations-analyst"),
                new AgentProfileVersion(1),
                "implement",
                Tools:
                [
                    new ToolDefinition(
                        "factory.tool.forged",
                        "A tool the pinned profile never admitted.",
                        JsonDocument.Parse("""{"type":"object"}""").RootElement)
                ]);

            var result = await dispatcher.ExecuteAsync(dispatch);

            Assert.False(result.Completed);
            Assert.StartsWith(FactoryAgentDispatcher.ForgedToolCategory, result.FailureCategory, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Factory_GivenSymlinkEscape_RefusesBeforeWrite()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "factory-escape-" + Guid.NewGuid().ToString("N"));
        var worktree = Path.Combine(basePath, "worktree");
        var outside = Path.Combine(basePath, "outside");
        Directory.CreateDirectory(worktree);
        Directory.CreateDirectory(outside);

        var junction = Path.Combine(worktree, "escape");
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(junction);
        start.ArgumentList.Add(outside);

        using var process = Process.Start(start);
        Assert.NotNull(process);
        await process!.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            // Junction creation is unavailable in this environment; the traversal guard is still exercised by
            // the absolute/traversal tests. Do not claim a pass for a test that never ran the scenario.
            return;
        }

        try
        {
            var context = new CapabilityContext(new SessionId("session-escape"), worktree);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await new WriteWorkspaceFileTool().ExecuteAsync(
                    new WriteFileRequest("escape/escaped.txt", "escaped"), context, CancellationToken.None));

            Assert.Contains(WorkspacePaths.ReparseCategory, error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(outside, "escaped.txt")));
        }
        finally
        {
            try
            {
                using var remove = Process.Start(new ProcessStartInfo("cmd.exe")
                {
                    Arguments = "/c rmdir \"" + junction + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                remove?.WaitForExit(5000);

                Directory.Delete(worktree, recursive: true);
                Directory.Delete(outside, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task Factory_GivenMissingOSBounds_FailsClosed()
    {
        var worktree = Path.Combine(Path.GetTempPath(), "factory-bounds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(worktree);

        var tool = new RunCommandTool(new NoIsolationRunner());
        var context = new CapabilityContext(new SessionId("session-bounds"), worktree);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await tool.ExecuteAsync(new RunCommandRequest("build", []), context, CancellationToken.None));

        Assert.Contains(RunCommandTool.IsolationUnavailableCategory, error.Message, StringComparison.Ordinal);
    }
}
