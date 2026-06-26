using Wip.Modus.Hosting;

namespace Wip.Shell.Prompting;

public sealed record RuntimePromptEnvironmentFacts
{
    public RuntimePromptEnvironmentFacts(
        string sessionId,
        string repositoryPath,
        string worktreePath,
        IReadOnlyList<string>? validationCommands = null)
    {
        SessionId = RuntimePromptContextNormalization.RequireValue(sessionId, nameof(sessionId));
        RepositoryPath = RuntimePromptContextNormalization.RequireValue(repositoryPath, nameof(repositoryPath));
        WorktreePath = RuntimePromptContextNormalization.RequireValue(worktreePath, nameof(worktreePath));
        ValidationCommands = RuntimePromptContextNormalization.Canonicalize(validationCommands);
    }

    public string SessionId { get; }

    public string RepositoryPath { get; }

    public string WorktreePath { get; }

    public IReadOnlyList<string> ValidationCommands { get; }
}

public sealed record RuntimePromptPluginFact
{
    public RuntimePromptPluginFact(
        string pluginId,
        string pluginName,
        string pluginVersion,
        IReadOnlyList<string>? capabilities = null,
        IReadOnlyList<string>? requiredPermissions = null)
    {
        PluginId = RuntimePromptContextNormalization.RequireValue(pluginId, nameof(pluginId));
        PluginName = RuntimePromptContextNormalization.RequireValue(pluginName, nameof(pluginName));
        PluginVersion = RuntimePromptContextNormalization.RequireValue(pluginVersion, nameof(pluginVersion));
        Capabilities = RuntimePromptContextNormalization.Canonicalize(capabilities);
        RequiredPermissions = RuntimePromptContextNormalization.Canonicalize(requiredPermissions);
    }

    public string PluginId { get; }

    public string PluginName { get; }

    public string PluginVersion { get; }

    public IReadOnlyList<string> Capabilities { get; }

    public IReadOnlyList<string> RequiredPermissions { get; }

    public static RuntimePromptPluginFact FromManifestEntry(PluginManifestEntry plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        return new RuntimePromptPluginFact(
            plugin.PluginId,
            plugin.PluginName,
            plugin.PluginVersion,
            plugin.Capabilities,
            plugin.RequiredPermissions);
    }
}

public sealed record RuntimePromptWorkflowPolicyScope
{
    public RuntimePromptWorkflowPolicyScope(string workflowId, string policyId)
    {
        WorkflowId = RuntimePromptContextNormalization.RequireValue(workflowId, nameof(workflowId));
        PolicyId = RuntimePromptContextNormalization.RequireValue(policyId, nameof(policyId));
    }

    public string WorkflowId { get; }

    public string PolicyId { get; }
}

public sealed record RuntimePromptCapabilityCatalog
{
    public RuntimePromptCapabilityCatalog(
        IReadOnlyList<string>? toolCapabilityIds = null,
        IReadOnlyList<string>? validatorCapabilityIds = null)
    {
        ToolCapabilityIds = RuntimePromptContextNormalization.Canonicalize(toolCapabilityIds);
        ValidatorCapabilityIds = RuntimePromptContextNormalization.Canonicalize(validatorCapabilityIds);
    }

    public IReadOnlyList<string> ToolCapabilityIds { get; }

    public IReadOnlyList<string> ValidatorCapabilityIds { get; }
}

public sealed record RuntimePromptContext
{
    public RuntimePromptContext(
        IReadOnlyList<string>? systemInstructions,
        RuntimePromptEnvironmentFacts environmentFacts,
        IReadOnlyList<RuntimePromptPluginFact>? pluginFacts,
        RuntimePromptWorkflowPolicyScope workflowPolicyScope,
        RuntimePromptCapabilityCatalog capabilityCatalog,
        IReadOnlyList<string>? forbiddenEcosystems,
        IReadOnlyList<string>? pluginManifestDiagnostics = null)
    {
        SystemInstructions = RuntimePromptContextNormalization.CanonicalizePreservingOrder(systemInstructions);
        EnvironmentFacts = environmentFacts ?? throw new ArgumentNullException(nameof(environmentFacts));
        PluginFacts = RuntimePromptContextNormalization.CanonicalizePlugins(pluginFacts);
        WorkflowPolicyScope = workflowPolicyScope ?? throw new ArgumentNullException(nameof(workflowPolicyScope));
        CapabilityCatalog = capabilityCatalog ?? throw new ArgumentNullException(nameof(capabilityCatalog));
        ForbiddenEcosystems = RuntimePromptContextNormalization.Canonicalize(forbiddenEcosystems);
        PluginManifestDiagnostics = RuntimePromptContextNormalization.Canonicalize(pluginManifestDiagnostics);

        if (SystemInstructions.Count == 0)
        {
            throw new InvalidOperationException(
                "Runtime prompt context requires at least one system instruction for provider plan generation.");
        }

        if (ForbiddenEcosystems.Count == 0)
        {
            throw new InvalidOperationException(
                "Runtime prompt context requires at least one forbidden ecosystem for provider plan generation.");
        }
    }

    public IReadOnlyList<string> SystemInstructions { get; }

    public RuntimePromptEnvironmentFacts EnvironmentFacts { get; }

    public IReadOnlyList<RuntimePromptPluginFact> PluginFacts { get; }

    public RuntimePromptWorkflowPolicyScope WorkflowPolicyScope { get; }

    public RuntimePromptCapabilityCatalog CapabilityCatalog { get; }

    public IReadOnlyList<string> ForbiddenEcosystems { get; }

    public IReadOnlyList<string> PluginManifestDiagnostics { get; }

    public string RenderSystemInstructions()
    {
        return string.Join(Environment.NewLine, SystemInstructions.Select(static value => $"- {value}"));
    }

    public string RenderComposedProviderContextPayload(string task)
    {
        var requiredTask = RuntimePromptContextNormalization.RequireValue(task, nameof(task));
        var lines = new List<string>
        {
            $"Task: {requiredTask}",
            "SystemInstructions:",
        };

        lines.AddRange(SystemInstructions.Select(static instruction => $"- {instruction}"));
        lines.Add("EnvironmentFacts:");
        lines.Add($"- SessionId: {EnvironmentFacts.SessionId}");
        lines.Add($"- RepositoryPath: {EnvironmentFacts.RepositoryPath}");
        lines.Add($"- WorktreePath: {EnvironmentFacts.WorktreePath}");
        lines.Add($"- ValidationCommands: {RuntimePromptContextNormalization.JoinOrNone(EnvironmentFacts.ValidationCommands)}");
        lines.Add("WorkflowPolicyScope:");
        lines.Add($"- WorkflowId: {WorkflowPolicyScope.WorkflowId}");
        lines.Add($"- PolicyId: {WorkflowPolicyScope.PolicyId}");
        lines.Add("CapabilityCatalog:");
        lines.Add($"- Tools: {RuntimePromptContextNormalization.JoinOrNone(CapabilityCatalog.ToolCapabilityIds)}");
        lines.Add($"- Validators: {RuntimePromptContextNormalization.JoinOrNone(CapabilityCatalog.ValidatorCapabilityIds)}");

        if (PluginFacts.Count == 0)
        {
            lines.Add("PluginFacts: none");
        }
        else
        {
            lines.Add("PluginFacts:");
            foreach (var plugin in PluginFacts)
            {
                lines.Add($"- {plugin.PluginId} [{plugin.PluginName}] v{plugin.PluginVersion}");
                lines.Add($"  capabilities: {RuntimePromptContextNormalization.JoinOrNone(plugin.Capabilities)}");
                lines.Add($"  ownedConstraints.requiredPermissions: {RuntimePromptContextNormalization.JoinOrNone(plugin.RequiredPermissions)}");
            }
        }

        if (PluginManifestDiagnostics.Count == 0)
        {
            lines.Add("PluginManifestDiagnostics: none");
        }
        else
        {
            lines.Add("PluginManifestDiagnostics:");
            lines.AddRange(PluginManifestDiagnostics.Select(static diagnostic => $"- {diagnostic}"));
        }

        lines.Add($"ForbiddenEcosystems: {string.Join(", ", ForbiddenEcosystems)}");
        return string.Join(Environment.NewLine, lines);
    }

    public string RenderProviderPlanningPrompt(string task)
    {
        var requiredTask = RuntimePromptContextNormalization.RequireValue(task, nameof(task));
        var lines = new List<string>
        {
            $"Task: {requiredTask}",
            "EnvironmentFacts:",
            $"- SessionId: {EnvironmentFacts.SessionId}",
            $"- RepositoryPath: {EnvironmentFacts.RepositoryPath}",
            $"- WorktreePath: {EnvironmentFacts.WorktreePath}",
            $"- ValidationCommands: {RuntimePromptContextNormalization.JoinOrNone(EnvironmentFacts.ValidationCommands)}",
            "WorkflowPolicyScope:",
            $"- WorkflowId: {WorkflowPolicyScope.WorkflowId}",
            $"- PolicyId: {WorkflowPolicyScope.PolicyId}",
            "CapabilityCatalog:",
            $"- Tools: {RuntimePromptContextNormalization.JoinOrNone(CapabilityCatalog.ToolCapabilityIds)}",
            $"- Validators: {RuntimePromptContextNormalization.JoinOrNone(CapabilityCatalog.ValidatorCapabilityIds)}"
        };

        if (PluginFacts.Count == 0)
        {
            lines.Add("PluginFacts: none");
        }
        else
        {
            lines.Add("PluginFacts:");
            foreach (var plugin in PluginFacts)
            {
                lines.Add($"- {plugin.PluginId} [{plugin.PluginName}] v{plugin.PluginVersion}");
                lines.Add($"  capabilities: {RuntimePromptContextNormalization.JoinOrNone(plugin.Capabilities)}");
                lines.Add($"  permissions: {RuntimePromptContextNormalization.JoinOrNone(plugin.RequiredPermissions)}");
            }
        }

        if (PluginManifestDiagnostics.Count == 0)
        {
            lines.Add("PluginManifestDiagnostics: none");
        }
        else
        {
            lines.Add("PluginManifestDiagnostics:");
            lines.AddRange(PluginManifestDiagnostics.Select(static diagnostic => $"- {diagnostic}"));
        }

        lines.Add($"ForbiddenEcosystems: {string.Join(", ", ForbiddenEcosystems)}");
        return string.Join(Environment.NewLine, lines);
    }
}

internal static class RuntimePromptContextNormalization
{
    public static IReadOnlyList<string> CanonicalizePreservingOrder(IReadOnlyList<string>? values)
    {
        if (values is null)
            return Array.Empty<string>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>(values.Count);

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var trimmed = value.Trim();
            if (seen.Add(trimmed))
                normalized.Add(trimmed);
        }

        return normalized.ToArray();
    }

    public static IReadOnlyList<RuntimePromptPluginFact> CanonicalizePlugins(IReadOnlyList<RuntimePromptPluginFact>? values)
    {
        if (values is null)
            return Array.Empty<RuntimePromptPluginFact>();

        return values
            .OrderBy(static value => value.PluginId, StringComparer.Ordinal)
            .ThenBy(static value => value.PluginVersion, StringComparer.Ordinal)
            .ThenBy(static value => value.PluginName, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> Canonicalize(IReadOnlyList<string>? values)
    {
        if (values is null)
            return Array.Empty<string>();

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    public static string JoinOrNone(IReadOnlyList<string> values)
        => values.Count == 0 ? "(none)" : string.Join(", ", values);

    public static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);

        return value.Trim();
    }
}
