namespace Wip.Abstractions.Workspaces;

public sealed record WorkspaceDescriptor
{
    public WorkspaceDescriptor(string repositoryPath, string worktreePath, string? artifactDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(repositoryPath));

        if (string.IsNullOrWhiteSpace(worktreePath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(worktreePath));

        if (artifactDirectory is not null && string.IsNullOrWhiteSpace(artifactDirectory))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(artifactDirectory));

        RepositoryPath = repositoryPath;
        WorktreePath = worktreePath;
        ArtifactDirectory = artifactDirectory;
    }

    public string RepositoryPath { get; }

    public string WorktreePath { get; }

    public string? ArtifactDirectory { get; }
}