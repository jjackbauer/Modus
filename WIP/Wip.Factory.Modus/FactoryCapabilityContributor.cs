using Modus.Core.Plugins;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Builder;
using Wip.Factory.Contracts.Plugins;

namespace Wip.Factory.Modus;

/// <summary>
/// A factory-scoped contract an approved plugin pack implements so its capabilities become part of
/// the host-owned capability catalog (F-B.1, source §4.1).
/// </summary>
/// <remarks>
/// The source document proposes <c>RegisterCapabilities(WipBuilder builder)</c>. This contract adds
/// the resolved <see cref="PluginOrigin"/> as a parameter so provenance is enforced by the signature
/// rather than trusted to each contributor (F-B.3); the mechanism — a plugin contributes typed WIP
/// capabilities through <see cref="WipBuilder"/> — is unchanged.
/// </remarks>
public interface IFactoryCapabilityContributor
{
    void RegisterCapabilities(WipBuilder builder, PluginOrigin origin);

    IReadOnlyList<FactoryAgentDefinition> GetAgentDefinitions();
}

/// <summary>A discovered contributor together with the plugin identity it was loaded from.</summary>
public sealed record FactoryContributorRegistration(
    PluginId PluginId,
    CapabilityVersion ContractVersion,
    string AssemblyName,
    IFactoryCapabilityContributor Contributor)
{
    public PluginOrigin ToOrigin() => new(PluginId.Value, ContractVersion, AssemblyName);
}

/// <summary>One diagnostic emitted while binding contributors.</summary>
public sealed record ContributorBindingDiagnostic(string Stage, string PluginId, string Message);

/// <summary>Outcome of binding contributors into the host builder.</summary>
public sealed record ContributorBindingResult(
    IReadOnlyList<ICapabilityDescriptor> RegisteredDescriptors,
    IReadOnlyList<FactoryAgentDefinition> AgentDefinitions,
    IReadOnlyList<ContributorBindingDiagnostic> Diagnostics,
    IReadOnlyList<PluginId> FailedPluginIds,
    IReadOnlyList<PluginId> ActivePluginIds);

/// <summary>
/// Binds discovered contributors into the <b>host-owned</b> <see cref="WipBuilder"/> before the root
/// service provider is built, so plugin-contributed and first-party capabilities share one container
/// (F-B.2). A contributor that throws is isolated: its capabilities are absent, every other
/// contributor still binds, and a diagnostic names the plugin and the stage (F-B.4).
/// </summary>
public sealed class FactoryCapabilityBinder
{
    private const string RegistrationStage = "capability-registration";
    private const string DefinitionStage = "agent-definitions";

    /// <summary>
    /// Binds every contributor in discovery order. Duplicate capability ids are refused by the
    /// builder's existing duplicate-capability behaviour, never silently replaced (F-B.5).
    /// </summary>
    public ContributorBindingResult Bind(
        IEnumerable<FactoryContributorRegistration> registrations,
        WipBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(builder);

        var diagnostics = new List<ContributorBindingDiagnostic>();
        var definitions = new List<FactoryAgentDefinition>();
        var failed = new List<PluginId>();
        var active = new List<PluginId>();

        foreach (var registration in registrations)
        {
            var descriptorsBefore = builder.CapabilityDescriptors.Count;

            try
            {
                registration.Contributor.RegisterCapabilities(builder, registration.ToOrigin());
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed.Add(registration.PluginId);
                diagnostics.Add(new ContributorBindingDiagnostic(
                    RegistrationStage,
                    registration.PluginId.Value,
                    exception.Message));
                continue;
            }

            // Verify the contributor actually contributed, rather than trusting it to have done so.
            if (builder.CapabilityDescriptors.Count == descriptorsBefore)
            {
                failed.Add(registration.PluginId);
                diagnostics.Add(new ContributorBindingDiagnostic(
                    RegistrationStage,
                    registration.PluginId.Value,
                    "Contributor registered no capabilities."));
                continue;
            }

            try
            {
                var contributed = registration.Contributor.GetAgentDefinitions();
                definitions.AddRange(contributed);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed.Add(registration.PluginId);
                diagnostics.Add(new ContributorBindingDiagnostic(
                    DefinitionStage,
                    registration.PluginId.Value,
                    exception.Message));
                continue;
            }

            active.Add(registration.PluginId);
        }

        return new ContributorBindingResult(
            builder.CapabilityDescriptors.ToArray(),
            definitions,
            diagnostics,
            failed,
            active);
    }
}
