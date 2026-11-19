using System.Text.Json;
using System.Text.Json.Serialization;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Plugins;
using Wip.Factory.Contracts.Profiles;

namespace Wip.Factory.Adaptation;

/// <summary>
/// File-backed immutable profile registry (F-D.1–F-D.5). A published version is written once and never
/// rewritten; the active version is a separate promotion pointer so evolution never mutates history.
/// </summary>
public sealed class LocalAgentProfileRegistry : IAgentProfileRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    private readonly string _profilesRoot;
    private readonly string _activeRoot;
    private readonly ICapabilityCatalog _catalog;
    private readonly IReadOnlyDictionary<AgentDefinitionId, FactoryAgentDefinition> _definitions;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LocalAgentProfileRegistry(
        string rootDirectory,
        ICapabilityCatalog catalog,
        IEnumerable<FactoryAgentDefinition> definitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        ArgumentNullException.ThrowIfNull(definitions);

        _profilesRoot = Path.Combine(rootDirectory, "profiles");
        _activeRoot = Path.Combine(rootDirectory, "active");
        _definitions = definitions.ToDictionary(static definition => definition.AgentDefinitionId);

        Directory.CreateDirectory(_profilesRoot);
        Directory.CreateDirectory(_activeRoot);
    }

    public async ValueTask<ProfilePublishResult> PublishAsync(
        AgentProfileDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var validation = Validate(draft);
            if (validation is not null)
            {
                return validation;
            }

            var digest = AgentProfileDigest.Compute(draft);
            var path = ProfilePath(draft.DefinitionId, draft.AgentProfileId, draft.Version);

            if (File.Exists(path))
            {
                var existing = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
                if (existing is not null && string.Equals(existing.ContentDigest, digest, StringComparison.Ordinal))
                {
                    // Publishing identical content for an existing version is idempotent.
                    return ProfilePublishResult.Accepted(existing);
                }

                return ProfilePublishResult.Refused(
                    ProfilePublishRejection.VersionAlreadyPublishedWithDifferentContent,
                    $"Profile '{draft.AgentProfileId.Value}' version {draft.Version.Value} is already published with different content.");
            }

            var profile = new AgentProfile(
                AgentProfile.SupportedApiVersion,
                AgentProfile.ProfileKind,
                draft,
                digest,
                DateTimeOffset.UtcNow);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(profile, SerializerOptions);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);

            return ProfilePublishResult.Accepted(profile);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<AgentProfile?> GetAsync(
        AgentProfileId profileId,
        AgentProfileVersion version,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var definitionDirectory in Directory.EnumerateDirectories(_profilesRoot))
            {
                var candidate = Path.Combine(definitionDirectory, profileId.Value, $"{version.Value}.json");
                if (File.Exists(candidate))
                {
                    return await ReadAsync(candidate, cancellationToken).ConfigureAwait(false);
                }
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<AgentProfile>> ListAsync(
        AgentDefinitionId definitionId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var definitionDirectory = Path.Combine(_profilesRoot, definitionId.Value);
            if (!Directory.Exists(definitionDirectory))
            {
                return [];
            }

            var profiles = new List<AgentProfile>();
            foreach (var profileDirectory in Directory.EnumerateDirectories(definitionDirectory))
            {
                foreach (var path in Directory.EnumerateFiles(profileDirectory, "*.json"))
                {
                    if (!int.TryParse(Path.GetFileNameWithoutExtension(path), out _))
                    {
                        continue;
                    }

                    var profile = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
                    if (profile is not null
                        && string.Equals(profile.DefinitionId.Value, definitionId.Value, StringComparison.Ordinal))
                    {
                        profiles.Add(profile);
                    }
                }
            }

            return profiles
                .OrderBy(static profile => profile.AgentProfileId.Value, StringComparer.Ordinal)
                .ThenBy(static profile => profile.Version.Value)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<AgentProfile?> ResolveActiveAsync(
        AgentDefinitionId definitionId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pointer = await ReadPointerAsync(definitionId, cancellationToken).ConfigureAwait(false);
            if (pointer?.ActiveVersion is not { } activeVersion)
            {
                return null;
            }

            var path = ProfilePathFromDefinition(definitionId, new AgentProfileVersion(activeVersion));
            return path is not null && File.Exists(path)
                ? await ReadAsync(path, cancellationToken).ConfigureAwait(false)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> PromoteAsync(
        AgentDefinitionId definitionId,
        AgentProfileVersion version,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = ProfilePathFromDefinition(definitionId, version);
            if (path is null || !File.Exists(path))
            {
                return false;
            }

            var existing = await ReadPointerAsync(definitionId, cancellationToken).ConfigureAwait(false);
            var pointer = new ActiveProfilePointer(
                definitionId.Value,
                version.Value,
                existing?.ActiveVersion);

            await WritePointerAsync(definitionId, pointer, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> RollBackAsync(
        AgentDefinitionId definitionId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await ReadPointerAsync(definitionId, cancellationToken).ConfigureAwait(false);
            if (existing?.PreviousVersion is not { } previous)
            {
                return false;
            }

            var path = ProfilePathFromDefinition(definitionId, new AgentProfileVersion(previous));
            if (path is null || !File.Exists(path))
            {
                return false;
            }

            var pointer = new ActiveProfilePointer(definitionId.Value, previous, existing.ActiveVersion);
            await WritePointerAsync(definitionId, pointer, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private ProfilePublishResult? Validate(AgentProfileDraft draft)
    {
        if (!_definitions.TryGetValue(draft.DefinitionId, out var definition))
        {
            return ProfilePublishResult.Refused(
                ProfilePublishRejection.UnknownDefinition,
                $"Definition '{draft.DefinitionId.Value}' is not registered.");
        }

        foreach (var reference in draft.Skills.Concat(draft.Tools))
        {
            if (!_catalog.TryResolve(reference.CapabilityId, reference.Version, out _))
            {
                return ProfilePublishResult.Refused(
                    ProfilePublishRejection.UnresolvedCapability,
                    $"Capability '{reference.CapabilityId.Value}@{reference.Version.Value}' is not admitted.");
            }
        }

        if (!definition.AllowedEvaluationPolicies.Contains(draft.Evaluation.SuiteRef))
        {
            return ProfilePublishResult.Refused(
                ProfilePublishRejection.DisallowedEvaluationPolicy,
                $"Evaluation policy '{draft.Evaluation.SuiteRef.Value}' is not allowed for definition '{definition.AgentDefinitionId.Value}'.");
        }

        var missingGates = definition.EffectiveRequiredGates
            .Where(gate => !draft.Evaluation.HardGates.Contains(gate))
            .ToArray();

        if (missingGates.Length > 0)
        {
            return ProfilePublishResult.Refused(
                ProfilePublishRejection.WeakerEvaluationGates,
                "Profile omits required evaluation gates: " + string.Join(", ", missingGates) + ".");
        }

        var outsideCeiling = draft.Permissions.Requested
            .Where(permission => !definition.PermissionCeiling.Contains(permission, StringComparer.Ordinal))
            .ToArray();

        if (outsideCeiling.Length > 0)
        {
            return ProfilePublishResult.Refused(
                ProfilePublishRejection.PermissionOutsideCeiling,
                "Profile requests permissions outside the definition ceiling: " + string.Join(", ", outsideCeiling) + ".");
        }

        if (draft.ParentVersion is { } parent)
        {
            var parentPath = ProfilePath(draft.DefinitionId, draft.AgentProfileId, parent);
            if (!File.Exists(parentPath))
            {
                return ProfilePublishResult.Refused(
                    ProfilePublishRejection.MissingParentVersion,
                    $"Parent version {parent.Value} of '{draft.AgentProfileId.Value}' does not exist.");
            }
        }

        return null;
    }

    private string ProfilePath(AgentDefinitionId definitionId, AgentProfileId profileId, AgentProfileVersion version)
        => Path.Combine(_profilesRoot, definitionId.Value, profileId.Value, $"{version.Value}.json");

    private string? ProfilePathFromDefinition(AgentDefinitionId definitionId, AgentProfileVersion version)
    {
        var definitionDirectory = Path.Combine(_profilesRoot, definitionId.Value);
        if (!Directory.Exists(definitionDirectory))
        {
            return null;
        }

        foreach (var profileDirectory in Directory.EnumerateDirectories(definitionDirectory))
        {
            var candidate = Path.Combine(profileDirectory, $"{version.Value}.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async ValueTask<AgentProfile?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<AgentProfile>(bytes, SerializerOptions);
    }

    private async ValueTask<ActiveProfilePointer?> ReadPointerAsync(
        AgentDefinitionId definitionId,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_activeRoot, $"{definitionId.Value}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ActiveProfilePointer>(bytes, SerializerOptions);
    }

    private async ValueTask WritePointerAsync(
        AgentDefinitionId definitionId,
        ActiveProfilePointer pointer,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_activeRoot);
        var path = Path.Combine(_activeRoot, $"{definitionId.Value}.json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(pointer, SerializerOptions);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The active pointer and the version it replaced, which is what rollback restores.</summary>
    private sealed record ActiveProfilePointer(string DefinitionId, int ActiveVersion, int? PreviousVersion);
}
