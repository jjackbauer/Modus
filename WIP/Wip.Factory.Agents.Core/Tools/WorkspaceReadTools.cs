using System.Text.RegularExpressions;
using Wip.Abstractions.Capabilities;

namespace Wip.Factory.Agents.Core.Tools;

/// <summary>Shared path confinement for every workspace tool (v0.3 §6.1).</summary>
public static class WorkspacePaths
{
    public const string OutsideWorktreeCategory = "path-outside-worktree";
    public const string NoWorktreeCategory = "no-worktree";
    public const string ReparseCategory = "path-reparse-point";

    public static string Resolve(string worktreePath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(worktreePath))
        {
            throw new InvalidOperationException($"{NoWorktreeCategory}: the session has no worktree.");
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException($"{OutsideWorktreeCategory}: a path is required.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"{OutsideWorktreeCategory}: '{relativePath}' is absolute.");
        }

        var separator = Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(worktreePath).TrimEnd(separator);
        var target = Path.GetFullPath(Path.Combine(root, relativePath));

        if (!target.Equals(root, StringComparison.OrdinalIgnoreCase)
            && !target.StartsWith(root + separator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{OutsideWorktreeCategory}: '{relativePath}' resolves outside the worktree.");
        }

        // A symlink or junction inside the worktree could still point outside it.
        var probe = new FileInfo(target);
        if (probe.Exists && probe.LinkTarget is not null)
        {
            throw new InvalidOperationException($"{ReparseCategory}: '{relativePath}' is a link.");
        }

        var directory = new DirectoryInfo(Path.GetDirectoryName(target) ?? root);
        while (directory is not null && directory.FullName.Length >= root.Length)
        {
            if (directory.Exists && directory.LinkTarget is not null)
            {
                throw new InvalidOperationException($"{ReparseCategory}: '{relativePath}' traverses a link.");
            }

            directory = directory.Parent;
        }

        return Path.GetRelativePath(root, target).Replace(separator, '/') is var relative
            ? target
            : target;
    }

    public static string Relative(string worktreePath, string absolutePath)
        => Path.GetRelativePath(Path.GetFullPath(worktreePath), absolutePath).Replace(Path.DirectorySeparatorChar, '/');
}

public sealed record ListFilesRequest(string? Path, int MaxEntries = 200);

public sealed record ListFilesResult(IReadOnlyList<string> Entries, bool Truncated);

/// <summary>Lists files under a worktree-relative directory, predictably sorted.</summary>
public sealed class ListFilesTool : ITool<ListFilesRequest, ListFilesResult>
{
    public ValueTask<ListFilesResult> ExecuteAsync(ListFilesRequest request, CapabilityContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var root = Path.GetFullPath(context.WorktreePath ?? throw new InvalidOperationException(WorkspacePaths.NoWorktreeCategory));
        var target = WorkspacePaths.Resolve(context.WorktreePath!, request.Path ?? ".");
        var max = Math.Clamp(request.MaxEntries, 1, 1000);

        if (!Directory.Exists(target))
        {
            return ValueTask.FromResult(new ListFilesResult([], false));
        }

        var entries = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                  && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => WorkspacePaths.Relative(root, path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        var truncated = entries.Length > max;
        return ValueTask.FromResult(new ListFilesResult(entries.Take(max).ToArray(), truncated));
    }
}

public sealed record ReadFileRequest(string Path, int MaxCharacters = 20000);

public sealed record ReadFileResult(string Path, string Content, bool Truncated, int TotalCharacters);

/// <summary>Reads a worktree file with a hard output bound.</summary>
public sealed class ReadFileTool : ITool<ReadFileRequest, ReadFileResult>
{
    public async ValueTask<ReadFileResult> ExecuteAsync(ReadFileRequest request, CapabilityContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var absolute = WorkspacePaths.Resolve(context.WorktreePath!, request.Path);
        var text = await File.ReadAllTextAsync(absolute, cancellationToken).ConfigureAwait(false);
        var limit = Math.Clamp(request.MaxCharacters, 1, 200_000);
        var truncated = text.Length > limit;

        return new ReadFileResult(
            WorkspacePaths.Relative(context.WorktreePath!, absolute),
            truncated ? text[..limit] : text,
            truncated,
            text.Length);
    }
}

public sealed record SearchTextRequest(string Pattern, string? Glob = null, int MaxMatches = 50, bool Regex = false);

public sealed record SearchTextResult(IReadOnlyList<string> Matches, bool Truncated);

/// <summary>Bounded literal or regex search across the worktree, reporting path:line.</summary>
public sealed class SearchTextTool : ITool<SearchTextRequest, SearchTextResult>
{
    public ValueTask<SearchTextResult> ExecuteAsync(SearchTextRequest request, CapabilityContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(request.Pattern))
        {
            throw new ArgumentException("A search pattern is required.", nameof(request));
        }

        var root = Path.GetFullPath(context.WorktreePath ?? throw new InvalidOperationException(WorkspacePaths.NoWorktreeCategory));
        var glob = string.IsNullOrWhiteSpace(request.Glob) ? "*.cs" : request.Glob!;
        var max = Math.Clamp(request.MaxMatches, 1, 500);

        var matches = new List<string>();
        var truncated = false;

        foreach (var file in Directory.EnumerateFiles(root, glob, SearchOption.AllDirectories)
                     .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch (IOException)
            {
                continue;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var hit = request.Regex
                    ? Regex.IsMatch(lines[i], request.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(250))
                    : lines[i].Contains(request.Pattern, StringComparison.Ordinal);

                if (!hit)
                {
                    continue;
                }

                if (matches.Count >= max)
                {
                    truncated = true;
                    break;
                }

                matches.Add($"{WorkspacePaths.Relative(root, file)}:{i + 1}: {lines[i].Trim()}");
            }

            if (truncated)
            {
                break;
            }
        }

        return ValueTask.FromResult(new SearchTextResult(matches, truncated));
    }
}
