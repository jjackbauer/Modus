using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Profiles;

namespace Wip.Factory.Contracts.Plugins;

/// <summary>
/// Declarative description of a logical factory role (M-09, source §3). A definition states the
/// obligations and the authority ceiling; the per-version profile states how the obligations are
/// currently satisfied.
/// </summary>
public sealed record FactoryAgentDefinition(
    AgentDefinitionId AgentDefinitionId,
    string Role,
    string Objective,
    IReadOnlyList<string> Responsibilities,
    IReadOnlyList<string> PermissionCeiling,
    IReadOnlyList<EvaluationPolicyId> AllowedEvaluationPolicies,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<EvaluationGate>? RequiredEvaluationGates = null)
{
    /// <summary>
    /// The gates this role must always pass. A profile that omits any of them would be judging itself
    /// against an easier bar, so publication is refused (F-D.5).
    /// </summary>
    public IReadOnlyList<EvaluationGate> EffectiveRequiredGates => RequiredEvaluationGates ?? AllGates;

    /// <summary>Every gate the factory knows about.</summary>
    public static IReadOnlyList<EvaluationGate> AllGates { get; } =
    [
        EvaluationGate.Compilation,
        EvaluationGate.Tests,
        EvaluationGate.RequirementCoverage,
        EvaluationGate.Architecture,
        EvaluationGate.GovernanceIntegrity,
        EvaluationGate.Security
    ];

    /// <summary>True when the definition asks for dispatch or supervisor authority (F-N.7).</summary>
    public bool RequestsDispatchAuthority =>
        PermissionCeiling.Any(IsDispatchAuthority) || RequiredCapabilities.Any(IsDispatchAuthority);

    private static bool IsDispatchAuthority(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Contains("dispatch", StringComparison.OrdinalIgnoreCase)
            || value.Contains("supervisor", StringComparison.OrdinalIgnoreCase)
            || value.Contains("orchestrat", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Enforces F-N.7: no factory agent definition may hold dispatch or supervisor authority, so the
/// dispatcher stays deterministic infrastructure rather than becoming an agent. The guard runs at
/// composition (publish) time and the dispatcher applies the same rule at dispatch time.
/// </summary>
public static class FactoryAgentDefinitionGuard
{
    public const string DispatchAuthorityCategory = "dispatch-authority-forbidden";

    public static IReadOnlyList<FactoryAgentDefinition> DispatchAuthorityOffenders(
        IEnumerable<FactoryAgentDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return definitions.Where(static definition => definition.RequestsDispatchAuthority).ToArray();
    }

    public static void EnsureNoDispatchAuthority(IEnumerable<FactoryAgentDefinition> definitions)
    {
        var offenders = DispatchAuthorityOffenders(definitions);
        if (offenders.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            DispatchAuthorityCategory + ": " + string.Join(", ", offenders.Select(static definition => definition.AgentDefinitionId.Value)));
    }
}