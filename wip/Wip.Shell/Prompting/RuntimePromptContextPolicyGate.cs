using Wip.Abstractions.Sessions;

namespace Wip.Shell.Prompting;

public sealed record RuntimePromptContextPolicyGateResult(bool IsAccepted, string ReasonCode)
{
    public static RuntimePromptContextPolicyGateResult Accept() => new(true, "accepted");

    public static RuntimePromptContextPolicyGateResult Reject(string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(reasonCode));

        return new RuntimePromptContextPolicyGateResult(false, reasonCode.Trim());
    }
}

public sealed class RuntimePromptContextPolicyGate
{
    public RuntimePromptContextPolicyGateResult Evaluate(
        SessionSnapshot snapshot,
        RuntimePromptContext context,
        IReadOnlyList<string>? registeredPolicyIds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.EnvironmentFacts.RepositoryPath))
            return RuntimePromptContextPolicyGateResult.Reject("missing-repository-root");

        if (!AreEquivalentPaths(context.EnvironmentFacts.RepositoryPath, snapshot.RepositoryPath))
            return RuntimePromptContextPolicyGateResult.Reject("inconsistent-repository-root");

        if (string.IsNullOrWhiteSpace(context.WorkflowPolicyScope.WorkflowId))
            return RuntimePromptContextPolicyGateResult.Reject("missing-workflow-id");

        if (!string.Equals(context.WorkflowPolicyScope.WorkflowId, snapshot.WorkflowId.Value, StringComparison.Ordinal))
            return RuntimePromptContextPolicyGateResult.Reject("inconsistent-workflow-id");

        if (string.IsNullOrWhiteSpace(context.WorkflowPolicyScope.PolicyId))
            return RuntimePromptContextPolicyGateResult.Reject("missing-policy-id");

        var policyCatalog = (registeredPolicyIds ?? Array.Empty<string>())
            .Where(static policyId => !string.IsNullOrWhiteSpace(policyId))
            .Select(static policyId => policyId.Trim())
            .ToArray();

        if (policyCatalog.Length == 0)
            return RuntimePromptContextPolicyGateResult.Reject("missing-policy-catalog");

        if (!policyCatalog.Contains(context.WorkflowPolicyScope.PolicyId, StringComparer.Ordinal))
            return RuntimePromptContextPolicyGateResult.Reject("inconsistent-policy-id");

        if (context.CapabilityCatalog.ValidatorCapabilityIds.Count == 0)
            return RuntimePromptContextPolicyGateResult.Reject("missing-validator-catalog");

        var pluginWithoutOwnership = context.PluginFacts
            .FirstOrDefault(static plugin => plugin.RequiredPermissions.Count == 0);
        if (pluginWithoutOwnership is not null)
            return RuntimePromptContextPolicyGateResult.Reject($"missing-plugin-ownership:{pluginWithoutOwnership.PluginId}");

        return RuntimePromptContextPolicyGateResult.Accept();
    }

    private static bool AreEquivalentPaths(string left, string right)
    {
        try
        {
            var normalizedLeft = NormalizePath(left);
            var normalizedRight = NormalizePath(right);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}