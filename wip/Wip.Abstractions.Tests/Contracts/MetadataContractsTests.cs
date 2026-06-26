using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workspaces;
using Xunit;

namespace Wip.Abstractions.Tests.Contracts;

public sealed class MetadataContractsTests
{
    [Theory]
    [InlineData(CapabilityKind.Agent)]
    [InlineData(CapabilityKind.Tool)]
    [InlineData(CapabilityKind.Validator)]
    [InlineData(CapabilityKind.Workflow)]
    [InlineData(CapabilityKind.Policy)]
    [InlineData(CapabilityKind.WorkspaceProvider)]
    [InlineData(CapabilityKind.ArtifactStore)]
    [InlineData(CapabilityKind.ModelProvider)]
    [InlineData(CapabilityKind.ContextProvider)]
    [InlineData(CapabilityKind.Reviewer)]
    public void CapabilityMetadata_GivenFirstClassCapabilityKind_PreservesKind(CapabilityKind kind)
    {
        var metadata = new CapabilityMetadata(
            capabilityId: new CapabilityId($"capability.{kind.ToString().ToLowerInvariant()}"),
            displayName: $"{kind} capability",
            kind: kind,
            capabilityType: typeof(TestCapability),
            version: new CapabilityVersion("1.0.0"),
            permissions: []);

        Assert.Equal(kind, metadata.Kind);
    }

    [Fact]
    public void CapabilityMetadata_GivenTypedRegistration_StoresVersionPermissionsAndPluginOrigin()
    {
        var metadata = new CapabilityMetadata(
            capabilityId: new CapabilityId("capability.plan"),
            displayName: "Plan capability",
            kind: CapabilityKind.Agent,
            capabilityType: typeof(TestCapability),
            version: new CapabilityVersion("1.2.3"),
            permissions:
            [
                new CapabilityPermission("workspace.read"),
                new CapabilityPermission("artifact.write")
            ],
            pluginOrigin: new PluginOrigin("plugin.shell", new CapabilityVersion("2.0.0"), "Plugin.Shell"),
            workspace: new WorkspaceDescriptor("C:/repo/modus", "C:/repo/modus/.wip/worktree"),
            artifact: new ArtifactDescriptor(
                new ArtifactId("artifact.plan"),
                new SessionId("session-1"),
                ArtifactKind.Json,
                "artifacts/plan.json",
                "Wip.Runtime.Runtime.WipRuntimeOrchestrator",
                "1.0.0",
                DateTimeOffset.UnixEpoch));

        Assert.Equal(new CapabilityId("capability.plan"), metadata.CapabilityId);
        Assert.Equal("Plan capability", metadata.DisplayName);
        Assert.Equal(CapabilityKind.Agent, metadata.Kind);
        Assert.Equal(typeof(TestCapability), metadata.CapabilityType);
        Assert.Equal(new CapabilityVersion("1.2.3"), metadata.Version);
        Assert.Equal(new[] { new CapabilityPermission("workspace.read"), new CapabilityPermission("artifact.write") }, metadata.Permissions);
        Assert.Equal(new PluginOrigin("plugin.shell", new CapabilityVersion("2.0.0"), "Plugin.Shell"), metadata.PluginOrigin);
        Assert.Equal(new WorkspaceDescriptor("C:/repo/modus", "C:/repo/modus/.wip/worktree"), metadata.Workspace);
        Assert.Equal(new ArtifactId("artifact.plan"), metadata.Artifact!.ArtifactId);
    }

    [Fact]
    public void WorkspaceDescriptor_GivenRepositoryAndWorktreePaths_StoresWorkspaceMetadata()
    {
        var descriptor = new WorkspaceDescriptor(
            repositoryPath: "C:/repo/modus",
            worktreePath: "C:/repo/modus/.wip/worktree",
            artifactDirectory: "C:/repo/modus/.wip/artifacts");

        Assert.Equal("C:/repo/modus", descriptor.RepositoryPath);
        Assert.Equal("C:/repo/modus/.wip/worktree", descriptor.WorktreePath);
        Assert.Equal("C:/repo/modus/.wip/artifacts", descriptor.ArtifactDirectory);
    }

    [Fact]
    public void SessionState_GivenLifecycleVocabulary_ContainsFullMvpStateSet()
    {
        var states = Enum.GetValues<SessionState>();

        Assert.Contains(SessionState.Created, states);
        Assert.Contains(SessionState.Editing, states);
        Assert.Contains(SessionState.Checkpointed, states);
        Assert.Contains(SessionState.Validating, states);
        Assert.Contains(SessionState.AwaitingApproval, states);
        Assert.Contains(SessionState.Approved, states);
        Assert.Contains(SessionState.Merged, states);
        Assert.Contains(SessionState.Archived, states);
        Assert.Contains(SessionState.Aborted, states);
    }

    private sealed class TestCapability : ICapability<object, object>
    {
        public ValueTask<object> ExecuteAsync(object request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult<object>(request);
    }
}