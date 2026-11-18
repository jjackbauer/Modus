using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;
using Wip.Factory.Adaptation;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Plugins;
using Wip.Factory.Contracts.Profiles;
using Xunit;

namespace Wip.Factory.Agents.Tests;

public sealed class ProfileRegistryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "wip-factory-profile-tests", Guid.NewGuid().ToString("N"));

    private static readonly CapabilityCatalog Catalog = new(
    [
        new CapabilityCatalogEntry(new CapabilityId("tool.repo-read"), new CapabilityVersion("1.0.0"))
    ]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private LocalAgentProfileRegistry Registry(IEnumerable<FactoryAgentDefinition>? definitions = null) =>
        new(_directory, Catalog, definitions ?? FactoryRolePack.Definitions);

    private static AgentProfileDraft Copy(AgentProfileDraft source) => source with { };

    [Fact]
    public async Task Registry_GivenNineDefinitionsAndProfiles_V1ResolvesForEveryRole()
    {
        var registry = Registry();
        var publishedVersions = new List<CapabilityVersion>();

        Assert.Equal(9, FactoryRolePack.Definitions.Count);
        Assert.Equal(9, FactoryRolePack.Profiles.Count);

        foreach (var draft in FactoryRolePack.Profiles)
        {
            var result = await registry.PublishAsync(draft);
            Assert.True(result.Published, $"{draft.AgentProfileId.Value}: {result.Reason}");
            Assert.Equal(AgentProfile.SupportedApiVersion, result.Profile!.ApiVersion);
            Assert.Equal(AgentProfile.ProfileKind, result.Profile.Kind);
            Assert.StartsWith("sha256:", result.Profile.ContentDigest, StringComparison.Ordinal);

            Assert.True(await registry.PromoteAsync(draft.DefinitionId, draft.Version));
            var active = await registry.ResolveActiveAsync(draft.DefinitionId);
            Assert.NotNull(active);
            Assert.Equal(draft.AgentProfileId.Value, active!.AgentProfileId.Value);
            Assert.Equal(1, active.Version.Value);
        }

        Assert.Empty(publishedVersions);

        // Promotion is per definition: each of the nine now resolves a distinct active profile.
        var activeRoles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in FactoryRolePack.Definitions)
        {
            var active = await registry.ResolveActiveAsync(definition.AgentDefinitionId);
            Assert.True(activeRoles.Add(active!.AgentProfileId.Value), "duplicate active profile id");
        }

        Assert.Equal(9, activeRoles.Count);
    }

    [Fact]
    public async Task ProfileRegistry_GivenPublishedV2_LeavesV1ByteIdenticalAndDigestStable()
    {
        var registry = Registry();
        var v1 = FactoryRolePack.Profiles[0];

        var publishedV1 = await registry.PublishAsync(v1);
        Assert.True(publishedV1.Published, publishedV1.Reason);

        var v1Path = Directory
            .EnumerateFiles(_directory, "1.json", SearchOption.AllDirectories)
            .Single();
        var v1BytesBefore = await File.ReadAllBytesAsync(v1Path);
        var digestBefore = publishedV1.Profile!.ContentDigest;

        var v2 = Copy(v1) with
        {
            Version = new AgentProfileVersion(2),
            ParentVersion = new AgentProfileVersion(1),
            ChangeReason = "Reduce repeated test failures.",
            Objective = v1.Objective + " (revised)"
        };

        var publishedV2 = await registry.PublishAsync(v2);
        Assert.True(publishedV2.Published, publishedV2.Reason);
        Assert.NotEqual(digestBefore, publishedV2.Profile!.ContentDigest);

        // v1 must be untouched byte for byte, and its digest must still verify from its own content.
        var v1BytesAfter = await File.ReadAllBytesAsync(v1Path);
        Assert.Equal(v1BytesBefore, v1BytesAfter);

        var rereadV1 = await registry.GetAsync(v1.AgentProfileId, v1.Version);
        Assert.Equal(digestBefore, rereadV1!.ContentDigest);
        Assert.Equal(digestBefore, AgentProfileDigest.Compute(rereadV1.Draft));

        // Publishing v2 must not move the active pointer by itself.
        Assert.Null(await registry.ResolveActiveAsync(v1.DefinitionId));

        Assert.True(await registry.PromoteAsync(v1.DefinitionId, v1.Version));
        Assert.Equal(1, (await registry.ResolveActiveAsync(v1.DefinitionId))!.Version.Value);

        Assert.True(await registry.PromoteAsync(v1.DefinitionId, new AgentProfileVersion(2)));
        Assert.Equal(2, (await registry.ResolveActiveAsync(v1.DefinitionId))!.Version.Value);

        // Rollback restores v1 and keeps v2 published.
        Assert.True(await registry.RollBackAsync(v1.DefinitionId));
        Assert.Equal(1, (await registry.ResolveActiveAsync(v1.DefinitionId))!.Version.Value);
        Assert.NotNull(await registry.GetAsync(v1.AgentProfileId, new AgentProfileVersion(2)));
    }

    [Fact]
    public async Task ProfilePublish_GivenUnresolvableCapabilityVersion_RejectsPublication()
    {
        var registry = Registry();
        var draft = Copy(FactoryRolePack.Profiles[0]) with
        {
            Tools =
            [
                new AgentProfileCapabilityRef(
                    new CapabilityId("tool.does-not-exist"),
                    new CapabilityVersion("9.9.9"))
            ]
        };

        var result = await registry.PublishAsync(draft);

        Assert.False(result.Published);
        Assert.Equal(ProfilePublishRejection.UnresolvedCapability, result.Rejection);
        Assert.Contains("tool.does-not-exist@9.9.9", result.Reason, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ProfilePublish_GivenWeakerEvaluationGatesThanDefinition_RejectsPublication()
    {
        var registry = Registry();
        var baseline = FactoryRolePack.Profiles[0];

        var weakened = Copy(baseline) with
        {
            Evaluation = baseline.Evaluation with
            {
                HardGates = baseline.Evaluation.HardGates
                    .Where(static gate => gate != EvaluationGate.Security)
                    .ToArray()
            }
        };

        var result = await registry.PublishAsync(weakened);

        Assert.False(result.Published);
        Assert.Equal(ProfilePublishRejection.WeakerEvaluationGates, result.Rejection);
        Assert.Contains("Security", result.Reason, StringComparison.Ordinal);

        // A gate set that covers every required gate publishes.
        var complete = Copy(baseline) with
        {
            Evaluation = baseline.Evaluation with { HardGates = FactoryAgentDefinition.AllGates }
        };
        Assert.True((await registry.PublishAsync(complete)).Published);
    }

    [Fact]
    public async Task Dispatch_GivenRequestedPermissionOutsideCeiling_DeniesBeforeSessionCreation()
    {
        var definition = new FactoryAgentDefinition(
            new AgentDefinitionId("factory.test-role"),
            "Test Role",
            "Exercise authorization",
            ["do nothing"],
            [PermissionCatalog.WorkspaceRead, PermissionCatalog.ArtifactRead],
            [new EvaluationPolicyId("eval://test-role/v1")],
            []);

        var profile = new AgentProfileDraft(
            new AgentProfileId("factory.test-role"),
            definition.AgentDefinitionId,
            new AgentProfileVersion(1),
            null,
            "authorization probe",
            definition.Role,
            definition.Objective,
            "artifact://agent-prompts/test-role@1",
            new AgentProfileModelPolicy("model.deepseek", "deepseek-chat"),
            [],
            [],
            new AgentProfileContextPolicy("p", "m", 1000),
            new AgentProfileExecutionPolicy(1, 10, 100, 1),
            new AgentProfileEvaluation(
                new EvaluationPolicyId("eval://test-role/v1"),
                FactoryAgentDefinition.AllGates,
                "shadow-before-promotion"),
            new AgentProfilePermissions(
                [PermissionCatalog.WorkspaceRead, PermissionCatalog.Process],
                "policy://factory/test-role@1"));

        var published = await Registry([definition]).PublishAsync(profile);

        // The ceiling check refuses at publish time, so an over-reaching profile never exists.
        Assert.False(published.Published);
        Assert.Equal(ProfilePublishRejection.PermissionOutsideCeiling, published.Rejection);
        Assert.Contains(PermissionCatalog.Process, published.Reason, StringComparison.Ordinal);

        // Even for a legitimately published profile, the host evaluator can still deny, and the
        // denial must happen before any session is created.
        var inCeiling = profile with
        {
            Permissions = new AgentProfilePermissions([PermissionCatalog.WorkspaceRead], "policy://factory/test-role@1")
        };
        var publishedInCeiling = await Registry([definition]).PublishAsync(inCeiling);
        Assert.True(publishedInCeiling.Published, publishedInCeiling.Reason);

        var sessionCreated = false;
        var resolver = new AgentProfilePermissionResolver(new DenyAllEvaluator());
        var context = new AgentProfileAuthorizationContext(
            publishedInCeiling.Profile!,
            definition,
            SessionId: new SessionId("session-1"));

        var authorization = await TryAuthorizeAndCreateSessionAsync(
            resolver,
            context,
            () =>
            {
                sessionCreated = true;
                return Task.FromResult<object>(new object());
            });

        Assert.Null(authorization);
        Assert.False(sessionCreated);
    }

    private static Task<object?> TryAuthorizeAndCreateSessionAsync(
        AgentProfilePermissionResolver resolver,
        AgentProfileAuthorizationContext context,
        Func<Task<object>> createSession)
    {
        var decision = resolver.Resolve(context);
        return decision.Allowed ? CreateAsync() : Task.FromResult<object?>(null);

        async Task<object?> CreateAsync() => await createSession();
    }

    private sealed class DenyAllEvaluator : IPermissionEvaluator
    {
        public PermissionDecision Evaluate(PermissionRequest request)
            => new(request.Permission, Allowed: false, Audited: false, BuiltIn: request.IsBuiltIn, Reason: "denied by policy");
    }
}
