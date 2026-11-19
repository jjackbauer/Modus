using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Identifiers;

namespace Wip.Factory.Contracts.Profiles;

/// <summary>One independent check that must pass before a change is accepted (source §7.4, F-N.2).</summary>
public enum EvaluationGate
{
    Compilation = 1,
    Tests = 2,
    RequirementCoverage = 3,
    Architecture = 4,
    GovernanceIntegrity = 5,
    Security = 6
}

/// <summary>A version-pinned capability reference. An unresolvable reference blocks publication (F-D.4).</summary>
public sealed record AgentProfileCapabilityRef(CapabilityId CapabilityId, CapabilityVersion Version);

/// <summary>Model selection for a profile. The profile requests; the host resolves.</summary>
public sealed record AgentProfileModelPolicy(string ProviderCapability, string PreferredModel);

/// <summary>Bounded context policy for a profile's sessions.</summary>
public sealed record AgentProfileContextPolicy(string RetrievalProfile, string MemoryScope, int MaxContextTokens);

/// <summary>Execution budgets. These bound a session; they do not grant capability.</summary>
public sealed record AgentProfileExecutionPolicy(
    int MaxSteps,
    int TimeoutSeconds,
    int MaxTotalTokens,
    int MaxConsecutiveToolFailures);

/// <summary>
/// Evaluation binding. <see cref="HardGates"/> must cover the definition's required gates; a profile
/// may not lower the bar it will be judged by (F-D.5).
/// </summary>
public sealed record AgentProfileEvaluation(
    EvaluationPolicyId SuiteRef,
    IReadOnlyList<EvaluationGate> HardGates,
    string CandidateRollout);

/// <summary>
/// The permissions a profile <i>requests</i>. A request is an input to the host's evaluator and can
/// never widen the definition's ceiling (F-D.6, source §3.1).
/// </summary>
public sealed record AgentProfilePermissions(IReadOnlyList<string> Requested, string PolicyRef);

/// <summary>
/// An authored, not-yet-published profile. The content digest is assigned by the registry, so an
/// author cannot choose the identity of their own artifact.
/// </summary>
public sealed record AgentProfileDraft(
    AgentProfileId AgentProfileId,
    AgentDefinitionId DefinitionId,
    AgentProfileVersion Version,
    AgentProfileVersion? ParentVersion,
    string ChangeReason,
    string Role,
    string Objective,
    string InstructionArtifactRef,
    AgentProfileModelPolicy Model,
    IReadOnlyList<AgentProfileCapabilityRef> Skills,
    IReadOnlyList<AgentProfileCapabilityRef> Tools,
    AgentProfileContextPolicy ContextPolicy,
    AgentProfileExecutionPolicy ExecutionPolicy,
    AgentProfileEvaluation Evaluation,
    AgentProfilePermissions Permissions);

/// <summary>A published, immutable profile version. <see cref="ContentDigest"/> is the registry's.</summary>
public sealed record AgentProfile(
    string ApiVersion,
    string Kind,
    AgentProfileDraft Draft,
    string ContentDigest,
    DateTimeOffset PublishedAtUtc)
{
    public const string SupportedApiVersion = "factory.wip/v1";

    public const string ProfileKind = "AgentProfile";

    public AgentProfileId AgentProfileId => Draft.AgentProfileId;

    public AgentDefinitionId DefinitionId => Draft.DefinitionId;

    public AgentProfileVersion Version => Draft.Version;
}

/// <summary>Canonical serialization and digest for an authored profile.</summary>
public static class AgentProfileDigest
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>Serializes the draft canonically. Stable across processes for the same draft.</summary>
    public static string Canonicalize(AgentProfileDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return JsonSerializer.Serialize(draft, CanonicalOptions);
    }

    /// <summary>SHA-256 over the canonical form, prefixed so the algorithm is never ambiguous.</summary>
    public static string Compute(AgentProfileDraft draft)
    {
        var bytes = Encoding.UTF8.GetBytes(Canonicalize(draft));
        var hash = SHA256.HashData(bytes);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>A capability the host has admitted, used to validate profile references (F-D.4).</summary>
public sealed record CapabilityCatalogEntry(CapabilityId CapabilityId, CapabilityVersion Version);

/// <summary>Admitted capability versions available for profiles to reference.</summary>
public interface ICapabilityCatalog
{
    bool TryResolve(CapabilityId capabilityId, CapabilityVersion version, out CapabilityCatalogEntry entry);
}

/// <summary>In-memory catalog. The host builds it from the WIP builder's descriptors.</summary>
public sealed class CapabilityCatalog : ICapabilityCatalog
{
    private readonly Dictionary<string, CapabilityCatalogEntry> _entries;

    public CapabilityCatalog(IEnumerable<CapabilityCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.ToDictionary(
            static entry => Key(entry.CapabilityId, entry.Version),
            StringComparer.Ordinal);
    }

    public IReadOnlyCollection<CapabilityCatalogEntry> Entries => _entries.Values.ToArray();

    public bool TryResolve(CapabilityId capabilityId, CapabilityVersion version, out CapabilityCatalogEntry entry)
        => _entries.TryGetValue(Key(capabilityId, version), out entry!);

    private static string Key(CapabilityId capabilityId, CapabilityVersion version)
        => capabilityId.Value + "@" + version.Value;
}

/// <summary>Why a publication was refused. Each value maps to a requirement clause.</summary>
public enum ProfilePublishRejection
{
    None = 0,

    /// <summary>F-D.4 — a referenced capability version is not in the admitted catalog.</summary>
    UnresolvedCapability = 1,

    /// <summary>F-D.5 — the profile's evaluation gates are weaker than the definition's.</summary>
    WeakerEvaluationGates = 2,

    /// <summary>F-D.5 — the evaluation suite is not among the definition's allowed policies.</summary>
    DisallowedEvaluationPolicy = 3,

    /// <summary>F-D.6 — the profile requests a permission outside the definition ceiling.</summary>
    PermissionOutsideCeiling = 4,

    /// <summary>The same version was already published with different content.</summary>
    VersionAlreadyPublishedWithDifferentContent = 5,

    /// <summary>The parent version does not exist.</summary>
    MissingParentVersion = 6,

    /// <summary>The profile names a definition the host does not know.</summary>
    UnknownDefinition = 7
}

/// <summary>Outcome of a publish attempt. On refusal nothing was written.</summary>
public sealed record ProfilePublishResult(
    bool Published,
    ProfilePublishRejection Rejection,
    AgentProfile? Profile,
    string? Reason)
{
    public static ProfilePublishResult Accepted(AgentProfile profile) => new(true, ProfilePublishRejection.None, profile, null);

    public static ProfilePublishResult Refused(ProfilePublishRejection rejection, string reason)
        => new(false, rejection, null, reason);
}

/// <summary>Immutable, versioned profile storage with a separate active pointer (F-D.3).</summary>
public interface IAgentProfileRegistry
{
    /// <summary>
    /// Validates and publishes a new immutable version. Republishing identical content for an existing
    /// version is idempotent; different content for the same version is refused.
    /// </summary>
    ValueTask<ProfilePublishResult> PublishAsync(AgentProfileDraft draft, CancellationToken cancellationToken = default);

    ValueTask<AgentProfile?> GetAsync(
        AgentProfileId profileId,
        AgentProfileVersion version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every published immutable version for a definition, ordered by profile id and version. Read-only: this is
    /// the operator-visible history of what may be activated, not a mutation surface.
    /// </summary>
    ValueTask<IReadOnlyList<AgentProfile>> ListAsync(
        AgentDefinitionId definitionId,
        CancellationToken cancellationToken = default);

    /// <summary>The version selected by the promotion pointer, or null when nothing is promoted.</summary>
    ValueTask<AgentProfile?> ResolveActiveAsync(
        AgentDefinitionId definitionId,
        CancellationToken cancellationToken = default);

    /// <summary>Moves the active pointer. Returns false when the version is not published.</summary>
    ValueTask<bool> PromoteAsync(
        AgentDefinitionId definitionId,
        AgentProfileVersion version,
        CancellationToken cancellationToken = default);

    /// <summary>Restores the previous pointer without deleting experiment history (F-J.7).</summary>
    ValueTask<bool> RollBackAsync(AgentDefinitionId definitionId, CancellationToken cancellationToken = default);
}
