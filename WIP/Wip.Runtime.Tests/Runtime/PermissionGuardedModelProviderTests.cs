using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class PermissionGuardedModelProviderTests
{
    [Fact]
    public async Task ExecuteAsync_GivenBuiltInProvider_ExpectedAllowedAndForwarded()
    {
        var inner = new RecordingModelProvider();
        var guard = new PermissionGuardedModelProvider<ProbeRequest, ProbeResult>(inner, new PermissionEvaluator());

        var response = await guard.ExecuteAsync(
            new ModelProviderRequest<ProbeRequest>(new ProbeRequest("hi"), modelId: "model.x"),
            new CapabilityContext(new SessionId("session-built-in"), WorktreePath: "/tmp/worktree"),
            CancellationToken.None);

        Assert.Equal("answer:hi", response.Payload.Answer);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_GivenPluginProviderWithoutGrant_ExpectedDeniedBeforeInvocation()
    {
        var inner = new RecordingModelProvider();
        var origin = new PluginOrigin("plugin.models", new CapabilityVersion("1.0.0"), "Plugin.Models");
        var guard = new PermissionGuardedModelProvider<ProbeRequest, ProbeResult>(
            inner,
            new PermissionEvaluator(),
            origin);

        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(() => guard.ExecuteAsync(
            new ModelProviderRequest<ProbeRequest>(new ProbeRequest("hi"), modelId: "model.x"),
            new CapabilityContext(new SessionId("session-denied"), WorktreePath: "/tmp/worktree"),
            CancellationToken.None).AsTask());

        Assert.Equal(PermissionCatalog.ModelInvoke, exception.Permission);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_GivenAuditModePluginProvider_ExpectedInvokedAndAudited()
    {
        var audited = new List<PermissionDecision>();
        var inner = new RecordingModelProvider();
        var origin = new PluginOrigin("plugin.models", new CapabilityVersion("1.0.0"), "Plugin.Models");
        var guard = new PermissionGuardedModelProvider<ProbeRequest, ProbeResult>(
            inner,
            new PermissionEvaluator(PermissionMode.Audit, auditSink: decision => audited.Add(decision)),
            origin);

        await guard.ExecuteAsync(
            new ModelProviderRequest<ProbeRequest>(new ProbeRequest("hi"), modelId: "model.x"),
            new CapabilityContext(new SessionId("session-audit"), WorktreePath: "/tmp/worktree"),
            CancellationToken.None);

        Assert.Equal(1, inner.CallCount);
        var decision = Assert.Single(audited);
        Assert.Equal(PermissionCatalog.ModelInvoke, decision.Permission);
        Assert.True(decision.Audited);
    }

    public sealed record ProbeRequest(string Text);

    public sealed record ProbeResult(string Answer);

    private sealed class RecordingModelProvider : IModelProvider<ProbeRequest, ProbeResult>
    {
        public int CallCount { get; private set; }

        public ValueTask<ModelProviderResponse<ProbeResult>> ExecuteAsync(
            ModelProviderRequest<ProbeRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(new ModelProviderResponse<ProbeResult>(
                new ProbeResult($"answer:{request.Payload.Text}"),
                providerId: "probe",
                modelId: request.ModelId));
        }
    }
}