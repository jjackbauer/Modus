using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class PermissionEvaluatorTests
{
    [Fact]
    public void Evaluate_GivenBuiltInCapability_ExpectedAllowed()
    {
        var decision = PermissionEvaluator.EnforceAllowBuiltIns.Evaluate(
            new PermissionRequest(PermissionCatalog.WorkspaceWrite));

        Assert.True(decision.Allowed);
        Assert.True(decision.BuiltIn);
        Assert.False(decision.Audited);
    }

    [Fact]
    public void Evaluate_GivenPluginWithoutGrant_ExpectedDenied()
    {
        var evaluator = new PermissionEvaluator();
        var decision = evaluator.Evaluate(new PermissionRequest(
            PermissionCatalog.WorkspaceWrite,
            new CapabilityId("tool.plugin"),
            new PluginOrigin("plugin.x", new CapabilityVersion("1.0.0"), "Plugin.X"),
            IsBuiltIn: false));

        Assert.False(decision.Allowed);
        Assert.Contains("plugin.x", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_GivenWildcardGrant_ExpectedAllowed()
    {
        var evaluator = new PermissionEvaluator(PermissionMode.Enforce, ["*"]);
        var decision = evaluator.Evaluate(new PermissionRequest(
            PermissionCatalog.Network,
            new CapabilityId("tool.plugin"),
            new PluginOrigin("plugin.x", new CapabilityVersion("1.0.0"), "Plugin.X"),
            IsBuiltIn: false));

        Assert.True(decision.Allowed);
        Assert.False(decision.Audited);
    }

    [Fact]
    public void Evaluate_GivenAuditModeAndNoGrant_ExpectedAllowedButAuditedAndReported()
    {
        var audited = new List<PermissionDecision>();
        var evaluator = new PermissionEvaluator(
            PermissionMode.Audit,
            auditSink: decision => audited.Add(decision));

        var decision = evaluator.Evaluate(new PermissionRequest(
            PermissionCatalog.Process,
            new CapabilityId("tool.plugin"),
            new PluginOrigin("plugin.x", new CapabilityVersion("1.0.0"), "Plugin.X"),
            IsBuiltIn: false));

        Assert.True(decision.Allowed);
        Assert.True(decision.Audited);
        Assert.Contains("audit mode", decision.Reason, StringComparison.Ordinal);
        var reported = Assert.Single(audited);
        Assert.Equal(PermissionCatalog.Process, reported.Permission);
    }
}