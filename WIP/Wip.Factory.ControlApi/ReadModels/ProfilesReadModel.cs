using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Adaptation;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Profiles;
using Wip.Factory.ControlApi.Contracts;
using Wip.Factory.ControlApi.Infrastructure;
using Wip.Factory.Host.Factory;

namespace Wip.Factory.ControlApi.ReadModels;

public sealed partial class FactoryReadModel
{
    public async Task<ProfileDto> CreateProfileAsync(
        string definitionId,
        ProfileDraftInputDto input,
        CancellationToken cancellationToken)
    {
        var definition = FindDefinition(definitionId)
                         ?? throw ApiException.NotFound("agent-definition-not-found", "No registered factory role has that definition id.");
        if (input.Version < 1)
        {
            throw ApiException.BadRequest("profile-version-invalid", "A profile version must be a positive integer.");
        }

        if (string.IsNullOrWhiteSpace(input.ChangeReason))
        {
            throw ApiException.BadRequest("profile-change-reason-required", "Every new profile version must state why it exists.");
        }

        var existing = await _service.Profiles.ListAsync(definition.AgentDefinitionId, cancellationToken).ConfigureAwait(false);
        if (existing.Any(profile => profile.Version.Value == input.Version))
        {
            throw ApiException.Conflict("profile-version-exists", "That version is already published. Published versions are immutable.");
        }

        var active = await _service.Profiles.ResolveActiveAsync(definition.AgentDefinitionId, cancellationToken).ConfigureAwait(false);
        var baseline = active?.Draft
                       ?? FactoryRolePack.Profiles.FirstOrDefault(profile =>
                           string.Equals(profile.DefinitionId.Value, definition.AgentDefinitionId.Value, StringComparison.Ordinal))
                       ?? throw ApiException.Conflict("profile-baseline-unavailable", "No active or declared baseline profile exists for this definition.");

        var tools = baseline.Tools;
        if (input.Tools is { Count: > 0 })
        {
            tools = input.Tools.Select(ParseCapability).ToArray();
        }

        var permissions = input.RequestedPermissions ?? baseline.Permissions.Requested;
        var execution = baseline.ExecutionPolicy with
        {
            MaxSteps = input.MaxSteps ?? baseline.ExecutionPolicy.MaxSteps,
            TimeoutSeconds = input.TimeoutSeconds ?? baseline.ExecutionPolicy.TimeoutSeconds,
            MaxTotalTokens = input.MaxTotalTokens ?? baseline.ExecutionPolicy.MaxTotalTokens,
            MaxConsecutiveToolFailures = input.MaxConsecutiveToolFailures ?? baseline.ExecutionPolicy.MaxConsecutiveToolFailures
        };

        var draft = baseline with
        {
            Version = new AgentProfileVersion(input.Version),
            ParentVersion = baseline.Version,
            ChangeReason = input.ChangeReason.Trim(),
            Model = input.PreferredModel is null
                ? baseline.Model
                : baseline.Model with { PreferredModel = input.PreferredModel.Trim() },
            Tools = tools,
            ExecutionPolicy = execution,
            Permissions = baseline.Permissions with { Requested = permissions }
        };

        var published = await _service.Profiles.PublishAsync(draft, cancellationToken).ConfigureAwait(false);
        if (!published.Published || published.Profile is null)
        {
            throw ApiException.Conflict(
                "profile-publish-refused",
                published.Rejection + ": " + (published.Reason ?? "the factory refused the profile draft"));
        }

        return ToProfileDto(published.Profile, active);
    }

    public async Task<ProfileDto> PromoteProfileAsync(
        string definitionId,
        int version,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        if (!confirmed)
        {
            throw ApiException.BadRequest("confirmation-required", "Promoting a profile changes the configuration of future sessions; confirm the exact version.");
        }

        var definition = FindDefinition(definitionId)
                         ?? throw ApiException.NotFound("agent-definition-not-found", "No registered factory role has that definition id.");
        var profiles = await _service.Profiles.ListAsync(definition.AgentDefinitionId, cancellationToken).ConfigureAwait(false);
        var target = profiles.FirstOrDefault(profile => profile.Version.Value == version)
                     ?? throw ApiException.NotFound("profile-version-not-found", "That immutable profile version has not been published.");

        if (!await _service.Profiles.PromoteAsync(definition.AgentDefinitionId, target.Version, cancellationToken).ConfigureAwait(false))
        {
            throw ApiException.Conflict("profile-promote-refused", "The factory refused to move the active pointer.");
        }

        return ToProfileDto(target, target);
    }

    public async Task<ProfileDto> RollBackProfileAsync(string definitionId, CancellationToken cancellationToken)
    {
        var definition = FindDefinition(definitionId)
                         ?? throw ApiException.NotFound("agent-definition-not-found", "No registered factory role has that definition id.");
        if (!await _service.Profiles.RollBackAsync(definition.AgentDefinitionId, cancellationToken).ConfigureAwait(false))
        {
            throw ApiException.Conflict("profile-rollback-refused", "There is no previous active version to restore.");
        }

        var active = await _service.Profiles.ResolveActiveAsync(definition.AgentDefinitionId, cancellationToken).ConfigureAwait(false)
                     ?? throw ApiException.Unavailable("profile-active-missing", "The active pointer does not resolve to a published version.");
        return ToProfileDto(active, active);
    }

    private static AgentProfileCapabilityRef ParseCapability(string reference)
    {
        var parts = reference.Split('@', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw ApiException.BadRequest("profile-capability-invalid", "Capability references must use capability@version: " + reference);
        }

        return new AgentProfileCapabilityRef(
            new CapabilityId(parts[0]),
            new CapabilityVersion(parts[1]));
    }
}
