using Wip.Abstractions.Identifiers;
using Wip.Workspaces.Git;

namespace Wip.Factory.Wip.Workspaces;

/// <summary>A provisioned, isolated workspace for one modifying session (F-F.1).</summary>
public sealed record FactoryWorkspaceLease(
    SessionId SessionId,
    string RepositoryPath,
    string WorktreePath,
    string SessionBranch,
    string TargetBranch,
    string TargetCommitAtCreation);

/// <summary>
/// Provisions one Git worktree per modifying session and refuses to hand the same worktree to two
/// sessions (F-F.1: "do not share a mutable worktree between agents"). The worktree engine itself is
/// WIP's; this type only enforces the factory's isolation rule around it.
/// </summary>
public sealed class FactoryWorktreeProvisioner
{
    private readonly WipWorkspaceProviderGit _provider;
    private readonly Dictionary<string, SessionId> _worktreeOwners = new(StringComparer.OrdinalIgnoreCase);

    private readonly string? _worktreeRoot;

    public FactoryWorktreeProvisioner(WipWorkspaceProviderGit? provider = null, string? worktreeRoot = null)
    {
        _provider = provider ?? new WipWorkspaceProviderGit();
        _worktreeRoot = worktreeRoot;
    }

    public const string WorktreeAlreadyLeasedCategory = "worktree-already-leased";
    public const string DuplicateSessionCategory = "session-already-provisioned";

    public async ValueTask<(FactoryWorkspaceLease? Lease, string? Reason)> ProvisionAsync(
        string repositoryPath,
        SessionId sessionId,
        string targetBranch,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetBranch);

        var session = await _provider
            .CreateAsync(new CreateWorkspaceRequest(repositoryPath, sessionId, targetBranch, _worktreeRoot), cancellationToken)
            .ConfigureAwait(false);

        var normalized = Path.GetFullPath(session.WorktreePath);

        if (_worktreeOwners.TryGetValue(normalized, out var owner) && !owner.Equals(sessionId))
        {
            return (null, $"{WorktreeAlreadyLeasedCategory}:{normalized}:held-by-{owner.Value}");
        }

        _worktreeOwners[normalized] = sessionId;

        return (new FactoryWorkspaceLease(
            session.SessionId,
            session.RepositoryPath,
            session.WorktreePath,
            session.SessionBranch,
            session.TargetBranch,
            session.TargetCommitAtCreation), null);
    }

    /// <summary>The sessions currently holding a worktree, keyed by normalized path.</summary>
    public IReadOnlyDictionary<string, SessionId> OutstandingLeases
        => new Dictionary<string, SessionId>(_worktreeOwners, StringComparer.OrdinalIgnoreCase);
}
