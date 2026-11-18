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

        if (Path.IsPathRooted(request.Path))
        {
            throw new InvalidOperationException($"{OutsideWorktreeCategory}: '{request.Path}' is an absolute path.");
        }

        var root = Path.GetFullPath(context.WorktreePath);
        var target = Path.GetFullPath(Path.Combine(root, request.Path));

        var separator = Path.DirectorySeparatorChar;
        if (!target.StartsWith(root.TrimEnd(separator) + separator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{OutsideWorktreeCategory}: '{request.Path}' resolves outside the worktree.");
        }

        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var content = request.Content ?? string.Empty;
        await File.WriteAllTextAsync(target, content, cancellationToken).ConfigureAwait(false);

        var relative = Path.GetRelativePath(root, target).Replace(separator, '/');
        return new WriteFileResult(relative, System.Text.Encoding.UTF8.GetByteCount(content));
    }
}
