using Wip.Abstractions.Identifiers;
using Wip.Factory.Adaptation;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Plugins;
using Wip.Factory.Contracts.Profiles;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class DispatchAuthorityTests
{
    [Fact]
    public void Definitions_GivenDispatchAuthorityRequest_IsRejectedAtPublishAndDispatch()
    {
        // None of the nine shipped roles may hold dispatch authority.
        Assert.All(FactoryRolePack.Definitions, static definition =>
            Assert.False(definition.RequestsDispatchAuthority, definition.Role));

        var supervised = new FactoryAgentDefinition(
            new AgentDefinitionId("factory.supervisor"),
            "Supervisor",
            "pick the next role",
            ["dispatch work"],
            ["dispatch.everything"],
            [new EvaluationPolicyId("eval://supervisor/v1")],
            []);

        Assert.True(supervised.RequestsDispatchAuthority);

        // Publish-time refusal: a profile bound to a definition without dispatch authority cannot ask
        // for it, and the guard rejects the definition itself.
        var offenders = FactoryAgentDefinitionGuard.DispatchAuthorityOffenders([.. FactoryRolePack.Definitions, supervised]);
        Assert.Single(offenders);
        Assert.Equal("factory.supervisor", offenders[0].AgentDefinitionId.Value);

        Assert.Throws<InvalidOperationException>(
            () => FactoryAgentDefinitionGuard.EnsureNoDispatchAuthority([supervised]));

        // The shipped pack passes the guard.
        FactoryAgentDefinitionGuard.EnsureNoDispatchAuthority(FactoryRolePack.Definitions);
    }
}