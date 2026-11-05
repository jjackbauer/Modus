using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;
using Wip.Abstractions.Tools;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class RuntimeToolInvokerTests
{
    private static readonly CapabilityId EchoToolId = new("tool.echo");

    [Fact]
    public async Task InvokeAsync_GivenKnownToolAndArguments_ExpectedJsonObservation()
    {
        var (invoker, provider) = CreateInvoker();
        await using var _ = provider;

        var result = await InvokeAsync(invoker, EchoToolId, new { text = "hi" });

        Assert.False(result.IsError);
        Assert.Contains("echo:hi", result.Content, StringComparison.Ordinal);
        Assert.Equal("call-1", result.ToolCallId);
    }

    [Fact]
    public async Task InvokeAsync_GivenUnknownTool_ExpectedDeterministicError()
    {
        var (invoker, provider) = CreateInvoker();
        await using var _ = provider;

        var result = await invoker.InvokeAsync(
            new ToolCall("call-1", "tool.missing", JsonSerializer.SerializeToElement(new { })),
            CreateContext(),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("unknown-tool", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_GivenInvalidArguments_ExpectedDeterministicError()
    {
        var (invoker, provider) = CreateInvoker();
        await using var _ = provider;

        using var document = JsonDocument.Parse("\"not-an-object\"");
        var result = await invoker.InvokeAsync(
            new ToolCall("call-1", EchoToolId.Value, document.RootElement),
            CreateContext(),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("invalid-arguments", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_GivenSlowTool_ExpectedTimeoutError()
    {
        var (invoker, provider) = CreateInvoker(new ToolInvocationOptions { Timeout = TimeSpan.FromMilliseconds(50) });
        await using var _ = provider;

        var result = await InvokeAsync(invoker, new CapabilityId("tool.slow"), new { milliseconds = 5_000 });

        Assert.True(result.IsError);
        Assert.Contains("timeout", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_GivenThrowingTool_ExpectedDeterministicToolError()
    {
        var (invoker, provider) = CreateInvoker();
        await using var _ = provider;

        var result = await InvokeAsync(invoker, new CapabilityId("tool.throwing"), new { });

        Assert.True(result.IsError);
        Assert.Contains("tool-error", result.Content, StringComparison.Ordinal);
        Assert.Contains("boom", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_GivenPluginToolWithoutGrant_ExpectedPermissionDeniedResult()
    {
        var (invoker, provider) = CreateInvoker();
        await using var _ = provider;

        var result = await InvokeAsync(invoker, new CapabilityId("tool.plugin"), new { text = "x" });

        Assert.True(result.IsError);
        Assert.Contains("permission-denied", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_GivenLongObservation_ExpectedTruncatedContent()
    {
        var (invoker, provider) = CreateInvoker(new ToolInvocationOptions { MaxObservationCharacters = 20 });
        await using var _ = provider;

        var result = await InvokeAsync(invoker, new CapabilityId("tool.long"), new { characters = 200 });

        Assert.False(result.IsError);
        Assert.Contains("[truncated", result.Content, StringComparison.Ordinal);
        Assert.True(result.Content.Length < 60);
    }

    [Fact]
    public async Task InvokeAsync_GivenCallerCancellation_ExpectedOperationCanceled()
    {
        var (invoker, provider) = CreateInvoker();
        await using var _ = provider;

        using var cancellation = new CancellationTokenSource();
        var invocation = invoker.InvokeAsync(
            new ToolCall("call-1", "tool.slow", JsonSerializer.SerializeToElement(new { milliseconds = 5_000 })),
            CreateContext(),
            cancellation.Token).AsTask();

        cancellation.Cancel();

        try
        {
            var result = await invocation;
            Assert.True(result.IsError, "A cancelled tool call must not report success.");
            Assert.Contains("cancel", result.Content, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            // Propagating the caller's cancellation is equally valid: the loop stops either way.
        }
    }

    private static async ValueTask<ToolResult> InvokeAsync(IToolInvoker invoker, CapabilityId toolId, object arguments)
        => await invoker.InvokeAsync(
            new ToolCall("call-1", toolId.Value, JsonSerializer.SerializeToElement(arguments)),
            CreateContext(),
            CancellationToken.None);

    private static CapabilityContext CreateContext()
        => new(new SessionId("session-tool-invoker"), WorktreePath: Path.GetTempPath());

    private static (RuntimeToolInvoker Invoker, ServiceProvider Provider) CreateInvoker(
        ToolInvocationOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<EchoTool>();
        services.AddSingleton<SlowTool>();
        services.AddSingleton<ThrowingTool>();
        services.AddSingleton<LongTool>();

        var descriptors = new ICapabilityDescriptor[]
        {
            new CapabilityDescriptor<EchoRequest, EchoResult>(
                EchoToolId,
                "Echo",
                CapabilityKind.Tool,
                typeof(EchoTool)),
            new CapabilityDescriptor<SlowRequest, SlowResult>(
                new CapabilityId("tool.slow"),
                "Slow",
                CapabilityKind.Tool,
                typeof(SlowTool)),
            new CapabilityDescriptor<ThrowingRequest, ThrowingResult>(
                new CapabilityId("tool.throwing"),
                "Throwing",
                CapabilityKind.Tool,
                typeof(ThrowingTool)),
            new CapabilityDescriptor<LongRequest, LongResult>(
                new CapabilityId("tool.long"),
                "Long",
                CapabilityKind.Tool,
                typeof(LongTool)),
            new CapabilityDescriptor<EchoRequest, EchoResult>(
                new CapabilityId("tool.plugin"),
                "Plugin echo",
                CapabilityKind.Tool,
                typeof(EchoTool),
                requiredPermissions: [new CapabilityPermission(PermissionCatalog.WorkspaceWrite)],
                pluginOrigin: new PluginOrigin("plugin.tools", new CapabilityVersion("1.0.0"), "Plugin.Tools")),
        };

        var provider = services.BuildServiceProvider();
        return (new RuntimeToolInvoker(provider, descriptors, options), provider);
    }

    public sealed record EchoRequest(string Text);

    public sealed record EchoResult(string Output);

    public sealed record SlowRequest(int Milliseconds);

    public sealed record SlowResult(bool Completed);

    public sealed record ThrowingRequest;

    public sealed record ThrowingResult(bool Completed);

    public sealed record LongRequest(int Characters);

    public sealed record LongResult(string Text);

    private sealed class EchoTool : ITool<EchoRequest, EchoResult>
    {
        public ValueTask<EchoResult> ExecuteAsync(EchoRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new EchoResult($"echo:{request.Text}"));
    }

    private sealed class SlowTool : ITool<SlowRequest, SlowResult>
    {
        public async ValueTask<SlowResult> ExecuteAsync(SlowRequest request, CapabilityContext context, CancellationToken cancellationToken)
        {
            await Task.Delay(request.Milliseconds, cancellationToken);
            return new SlowResult(true);
        }
    }

    private sealed class ThrowingTool : ITool<ThrowingRequest, ThrowingResult>
    {
        public ValueTask<ThrowingResult> ExecuteAsync(ThrowingRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    private sealed class LongTool : ITool<LongRequest, LongResult>
    {
        public ValueTask<LongResult> ExecuteAsync(LongRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new LongResult(new string('x', request.Characters)));
    }
}