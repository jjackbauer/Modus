using Wip.Abstractions.Capabilities;

namespace Wip.Factory.Agents.Core.Tools;

/// <summary>A file to write into the session worktree, relative to its root.</summary>
public sealed record WriteFileRequest(string Path, string Content);

/// <summary>What was written.</summary>
public sealed record WriteFileResult(string Path, int BytesWritten);

/// <summary>
/// Writes a file inside the session's own worktree (the capability an implementations role needs).
/// It refuses absolute paths, traversal above the worktree root, and any session without a worktree,
/// so a model-authored path can never escape the workspace it was given.
/// </summary>
public sealed class WriteWorkspaceFileTool : ITool<WriteFileRequest, WriteFileResult>
{
    public const string OutsideWorktreeCategory = "path-outside-worktree";
    public const string NoWorktreeCategory = "no-worktree";

    public async ValueTask<WriteFileResult> ExecuteAsync(
        WriteFileRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Path))
        {
            throw new ArgumentException("A file path is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(context.WorktreePath))
        {
            throw new InvalidOperationException($"{NoWorktreeCategory}: the session has no worktree to write into.");
        }

        // One shared guard for normalized, physically resolved confinement: absolute paths, traversal, existing
        // symlinks and junctions are all refused before anything is created (v0.4 FAC-P1-06.2).
        var target = WorkspacePaths.Resolve(context.WorktreePath, request.Path);
        var root = Path.GetFullPath(context.WorktreePath);

        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var content = request.Content ?? string.Empty;
        await File.WriteAllTextAsync(target, content, cancellationToken).ConfigureAwait(false);

        var relative = Path.GetRelativePath(root, target).Replace(Path.DirectorySeparatorChar, '/');
        return new WriteFileResult(relative, System.Text.Encoding.UTF8.GetByteCount(content));
    }
}
