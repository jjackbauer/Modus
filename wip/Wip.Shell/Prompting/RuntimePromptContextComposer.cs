using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Builder;
using Wip.Modus.Hosting;

namespace Wip.Shell.Prompting;

public sealed class RuntimePromptContextComposer
{
    private const string PromptFragmentCapabilityPrefix = "prompt.system.fragment:";
    private const int DefaultPromptFragmentPrecedence = 500;
    private const int MaxPromptFragmentLength = 180;

    private static readonly IReadOnlyList<string> DefaultSystemInstructions =
    [
        "Produce deterministic implementation plan steps.",
        "Return plain text numbered steps only.",
        "Honor repository and worktree boundaries from EnvironmentFacts.",
        "Do not emit commands from ForbiddenEcosystems."
    ];

    private static readonly IReadOnlyList<string> DefaultForbiddenEcosystems =
    [
        "cargo",
        "npm",
        "pip",
        "yarn"
    ];

    public RuntimePromptContext Compose(
        SessionSnapshot snapshot,
        WipBuilder builder,
        string repositoryRoot,
        string selectedPolicyId,
        IReadOnlyList<string>? validationCommands,
        IModusWipBridge? diagnosticsBridge,
        WorkflowId? selectedWorkflowId = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(builder);

        var effectiveRepositoryRoot = RuntimePromptContextNormalization.RequireValue(repositoryRoot, nameof(repositoryRoot));
        var effectiveWorkflowId = selectedWorkflowId?.Value ?? snapshot.WorkflowId.Value;
        var effectivePolicyId = ResolvePolicyId(builder, selectedPolicyId);

        var toolCapabilityIds = builder.CapabilityDescriptors
            .Where(static descriptor => descriptor.Kind == CapabilityKind.Tool)
            .Select(static descriptor => descriptor.CapabilityId.Value)
            .OrderBy(static capabilityId => capabilityId, StringComparer.Ordinal)
            .ToArray();

        var validatorCapabilityIds = builder.CapabilityDescriptors
            .Where(static descriptor => descriptor.Kind == CapabilityKind.Validator)
            .Select(static descriptor => descriptor.CapabilityId.Value)
            .OrderBy(static capabilityId => capabilityId, StringComparer.Ordinal)
            .ToArray();

        var runManifestPlugins = diagnosticsBridge?.GetRunManifest().Plugins
            ?? Array.Empty<PluginManifestEntry>();

        var pluginFacts = runManifestPlugins
            .Select(RuntimePromptPluginFact.FromManifestEntry)
            .ToArray();

        var loadDiagnostics = diagnosticsBridge?.GetLoadDiagnostics() ?? Array.Empty<string>();
        var promptFragmentContributions = BuildPluginPromptFragmentContributions(runManifestPlugins, out var promptFragmentDiagnostics);
        var systemInstructions = ComposeSystemInstructions(promptFragmentContributions);
        var mergedDiagnostics = loadDiagnostics
            .Concat(promptFragmentDiagnostics)
            .ToArray();

        return new RuntimePromptContext(
            systemInstructions,
            new RuntimePromptEnvironmentFacts(
                snapshot.SessionId.Value,
                effectiveRepositoryRoot,
                snapshot.WorktreePath,
                validationCommands),
            pluginFacts,
            new RuntimePromptWorkflowPolicyScope(effectiveWorkflowId, effectivePolicyId),
            new RuntimePromptCapabilityCatalog(toolCapabilityIds, validatorCapabilityIds),
            DefaultForbiddenEcosystems,
            mergedDiagnostics);
    }

    private static IReadOnlyList<string> ComposeSystemInstructions(
        IReadOnlyList<PluginPromptFragmentContribution> promptFragmentContributions)
    {
        var instructions = new List<string>(DefaultSystemInstructions.Count + promptFragmentContributions.Count);
        instructions.AddRange(DefaultSystemInstructions);

        foreach (var contribution in promptFragmentContributions)
        {
            instructions.Add($"Plugin[{contribution.PluginId}] {contribution.Fragment}");
        }

        return instructions;
    }

    private static IReadOnlyList<PluginPromptFragmentContribution> BuildPluginPromptFragmentContributions(
        IReadOnlyList<PluginManifestEntry> plugins,
        out IReadOnlyList<string> diagnostics)
    {
        var contributions = new List<PluginPromptFragmentContribution>();
        var diagnosticLines = new List<string>();

        foreach (var plugin in plugins
                     .OrderBy(static value => value.PluginId, StringComparer.Ordinal)
                     .ThenBy(static value => value.PluginVersion, StringComparer.Ordinal)
                     .ThenBy(static value => value.PluginName, StringComparer.Ordinal))
        {
            foreach (var capability in plugin.Capabilities.OrderBy(static value => value, StringComparer.Ordinal))
            {
                if (!TryParsePromptFragmentCapability(capability, out var precedence, out var rawFragment))
                    continue;

                if (!TrySanitizePromptFragment(rawFragment, out var sanitizedFragment, out var rejectionReason))
                {
                    diagnosticLines.Add($"[prompt-fragment][plugin:{plugin.PluginId}][status=rejected][reason={rejectionReason}] capability={PromptFragmentCapabilityPrefix}<redacted>");
                    continue;
                }

                contributions.Add(new PluginPromptFragmentContribution(
                    plugin.PluginId,
                    plugin.PluginVersion,
                    precedence,
                    sanitizedFragment));

                diagnosticLines.Add($"[prompt-fragment][plugin:{plugin.PluginId}][status=accepted][precedence={precedence:D4}] {sanitizedFragment}");
            }
        }

        diagnostics = diagnosticLines;

        return contributions
            .OrderBy(static value => value.Precedence)
            .ThenBy(static value => value.PluginId, StringComparer.Ordinal)
            .ThenBy(static value => value.PluginVersion, StringComparer.Ordinal)
            .ThenBy(static value => value.Fragment, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryParsePromptFragmentCapability(
        string capability,
        out int precedence,
        out string rawFragment)
    {
        precedence = DefaultPromptFragmentPrecedence;
        rawFragment = string.Empty;

        if (string.IsNullOrWhiteSpace(capability))
            return false;

        var trimmed = capability.Trim();
        if (!trimmed.StartsWith(PromptFragmentCapabilityPrefix, StringComparison.Ordinal))
            return false;

        var metadata = trimmed[PromptFragmentCapabilityPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(metadata))
            return false;

        var separatorIndex = metadata.IndexOf('|');
        if (separatorIndex <= 0)
        {
            rawFragment = metadata;
            return true;
        }

        var precedenceValue = metadata[..separatorIndex].Trim();
        var fragmentValue = metadata[(separatorIndex + 1)..].Trim();

        if (int.TryParse(precedenceValue, out var parsedPrecedence) && parsedPrecedence >= 0)
        {
            precedence = parsedPrecedence;
            rawFragment = fragmentValue;
            return true;
        }

        rawFragment = metadata;
        return true;
    }

    private static bool TrySanitizePromptFragment(
        string rawFragment,
        out string sanitizedFragment,
        out string rejectionReason)
    {
        sanitizedFragment = string.Empty;
        rejectionReason = string.Empty;

        if (string.IsNullOrWhiteSpace(rawFragment))
        {
            rejectionReason = "empty-fragment";
            return false;
        }

        if (rawFragment.Contains("```", StringComparison.Ordinal)
            || rawFragment.Contains("<script", StringComparison.OrdinalIgnoreCase))
        {
            rejectionReason = "unsafe-token";
            return false;
        }

        var buffer = new char[rawFragment.Length];
        var writeIndex = 0;
        var previousWasWhitespace = false;

        foreach (var character in rawFragment)
        {
            if (char.IsControl(character))
            {
                if (!previousWasWhitespace)
                {
                    buffer[writeIndex++] = ' ';
                    previousWasWhitespace = true;
                }

                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    buffer[writeIndex++] = ' ';
                    previousWasWhitespace = true;
                }

                continue;
            }

            buffer[writeIndex++] = character;
            previousWasWhitespace = false;
        }

        var normalized = new string(buffer, 0, writeIndex).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            rejectionReason = "empty-after-sanitization";
            return false;
        }

        if (normalized.Length > MaxPromptFragmentLength)
            normalized = normalized[..MaxPromptFragmentLength];

        sanitizedFragment = normalized;
        return true;
    }

    private static string ResolvePolicyId(WipBuilder builder, string selectedPolicyId)
    {
        if (!string.IsNullOrWhiteSpace(selectedPolicyId))
        {
            var candidatePolicyId = selectedPolicyId.Trim();
            var hasRegisteredCandidate = builder.PolicyRegistrations
                .Any(registration => string.Equals(registration.PolicyId.Value, candidatePolicyId, StringComparison.Ordinal));

            if (hasRegisteredCandidate)
                return candidatePolicyId;
        }

        var fallbackPolicyId = builder.PolicyRegistrations
            .Select(static registration => registration.PolicyId.Value)
            .OrderBy(static policyId => policyId, StringComparer.Ordinal)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(fallbackPolicyId))
            throw new InvalidOperationException("Cannot compose runtime prompt context without a registered policy id.");

        return fallbackPolicyId;
    }

    private sealed record PluginPromptFragmentContribution(
        string PluginId,
        string PluginVersion,
        int Precedence,
        string Fragment);
}