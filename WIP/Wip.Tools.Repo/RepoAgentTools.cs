using System.Diagnostics;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;
using Wip.Builder;

namespace Wip.Tools.Repo;

public sealed record RepoSearchRequest(string Pattern, int MaxResults = 50);

public sealed record RepoSearchMatch(string Path, int Line, string Text);

public sealed record RepoSearchResult(IReadOnlyList<RepoSearchMatch> Matches);

public sealed record RepoReadFileRequest(string Path, int MaxCharacters = 20_000);

public sealed record RepoReadFileResult(string Path, string Content, bool Truncated);

public sealed record RepoWriteFileRequest(string Path, string Content);

public sealed record RepoWriteFileResult(string Path, int CharactersWritten);

public sealed record RepoDeleteFileRequest(string Path);

public sealed record RepoDeleteFileResult(string Path, bool Deleted);

public sealed record RepoGitStatusRequest;

public sealed record RepoGitStatusResult(string Output);

/// <summary>
/// WP-3.6 write-capable repository tools. Every path is resolved inside the session worktree;
/// absolute paths, parent traversal and <c>.git</c> internals are rejected so a model-authored tool
/// call cannot escape the isolated worktree.
/// </summary>
public static class WorktreePathGuard
{
    private static readonly string[] DeniedSegments = [".git", ".."];

    public static string Resolve(CapabilityContext context, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new UnauthorizedAccessException("A repository-relative path is required.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new UnauthorizedAccessException("Absolute paths are not allowed; use a worktree-relative path.");
        }

        var segments = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => DeniedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
        {
            throw new UnauthorizedAccessException("Parent traversal and .git internals are not allowed.");
        }

        var root = Path.GetFullPath(context.WorktreePath);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The path escapes the session worktree.");
        }

        return fullPath;
    }
}

public sealed class RepoSearchTool : ITool<RepoSearchRequest, RepoSearchResult>
{
    public ValueTask<RepoSearchResult> ExecuteAsync(
        RepoSearchRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Pattern))
        {
            throw new ArgumentException("A search pattern is required.", nameof(request));
        }

        var root = Path.GetFullPath(context.WorktreePath);
        var matches = new List<RepoSearchMatch>();
        var limit = Math.Max(1, request.MaxResults);

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsExcluded(file, root))
            {
                continue;
            }

            var info = new FileInfo(file);
            if (info.Length > 1_000_000)
            {
                continue;
            }

            var lines = File.ReadLines(file);
            var lineNumber = 0;
            foreach (var line in lines)
            {
                lineNumber++;
                if (line.Contains(request.Pattern, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(new RepoSearchMatch(Path.GetRelativePath(root, file), lineNumber, line.Trim()));
                    if (matches.Count >= limit)
                    {
                        return ValueTask.FromResult(new RepoSearchResult(matches));
                    }
                }
            }
        }

        return ValueTask.FromResult(new RepoSearchResult(matches));
    }

    private static bool IsExcluded(string file, string root)
    {
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(static segment =>
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class RepoReadFileTool : ITool<RepoReadFileRequest, RepoReadFileResult>
{
    public ValueTask<RepoReadFileResult> ExecuteAsync(
        RepoReadFileRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = WorktreePathGuard.Resolve(context, request.Path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File '{request.Path}' was not found in the worktree.", request.Path);
        }

        var content = File.ReadAllText(path);
        var max = request.MaxCharacters > 0 ? request.MaxCharacters : 20_000;
        var truncated = content.Length > max;

        return ValueTask.FromResult(new RepoReadFileResult(
            request.Path,
            truncated ? content[..max] : content,
            truncated));
    }
}

public sealed class RepoWriteFileTool : ITool<RepoWriteFileRequest, RepoWriteFileResult>
{
    public ValueTask<RepoWriteFileResult> ExecuteAsync(
        RepoWriteFileRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Content);

        var path = WorktreePathGuard.Resolve(context, request.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, request.Content);

        return ValueTask.FromResult(new RepoWriteFileResult(request.Path, request.Content.Length));
    }
}

public sealed class RepoDeleteFileTool : ITool<RepoDeleteFileRequest, RepoDeleteFileResult>
{
    public ValueTask<RepoDeleteFileResult> ExecuteAsync(
        RepoDeleteFileRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = WorktreePathGuard.Resolve(context, request.Path);
        if (!File.Exists(path))
        {
            return ValueTask.FromResult(new RepoDeleteFileResult(request.Path, Deleted: false));
        }

        File.Delete(path);
        return ValueTask.FromResult(new RepoDeleteFileResult(request.Path, Deleted: true));
    }
}

public sealed class RepoGitStatusTool : ITool<RepoGitStatusRequest, RepoGitStatusResult>
{
    public async ValueTask<RepoGitStatusResult> ExecuteAsync(
        RepoGitStatusRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = Path.GetFullPath(context.WorktreePath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("status");
        startInfo.ArgumentList.Add("--porcelain");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git status.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return new RepoGitStatusResult(output);
    }
}

public static class RepoAgentToolRegistration
{
    public static WipBuilder AddRepoAgentTools(this WipBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddTool<RepoSearchTool, RepoSearchRequest, RepoSearchResult>(
            new CapabilityId("tool.repo.search"),
            "Repository search",
            requiredPermissions: [new CapabilityPermission(PermissionCatalog.WorkspaceRead)]);

        builder.AddTool<RepoReadFileTool, RepoReadFileRequest, RepoReadFileResult>(
            new CapabilityId("tool.repo.read_file"),
            "Repository read file",
            requiredPermissions: [new CapabilityPermission(PermissionCatalog.WorkspaceRead)]);

        builder.AddTool<RepoWriteFileTool, RepoWriteFileRequest, RepoWriteFileResult>(
            new CapabilityId("tool.repo.write_file"),
            "Repository write file",
            requiredPermissions: [new CapabilityPermission(PermissionCatalog.WorkspaceWrite)]);

        builder.AddTool<RepoDeleteFileTool, RepoDeleteFileRequest, RepoDeleteFileResult>(
            new CapabilityId("tool.repo.delete_file"),
            "Repository delete file",
            requiredPermissions: [new CapabilityPermission(PermissionCatalog.WorkspaceWrite)]);

        builder.AddTool<RepoGitStatusTool, RepoGitStatusRequest, RepoGitStatusResult>(
            new CapabilityId("tool.repo.git_status"),
            "Repository git status",
            requiredPermissions:
            [
                new CapabilityPermission(PermissionCatalog.WorkspaceRead),
                new CapabilityPermission(PermissionCatalog.Process),
            ]);

        return builder;
    }
}