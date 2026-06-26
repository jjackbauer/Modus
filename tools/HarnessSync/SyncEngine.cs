namespace HarnessSync;

public sealed class SyncEngine
{
    private readonly string _repositoryRoot;

    public SyncEngine(string repositoryRoot)
    {
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
    }

    public IReadOnlyList<SyncIssue> Run(SyncMode mode)
    {
        var bodies = HarnessCatalog.Discover(_repositoryRoot);
        var issues = new List<SyncIssue>();
        var bodyNames = bodies.Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var body in bodies)
        {
            var stubs = StubTemplates.ExpectedStubs(_repositoryRoot, body);
            foreach (var (stubPath, expectedContent) in stubs)
            {
                if (!File.Exists(stubPath))
                {
                    if (mode == SyncMode.Check)
                    {
                        issues.Add(new SyncIssue("missing-stub", $"Missing stub for '{body.Name}': {ToRepoRelative(stubPath)}"));
                    }
                    else
                    {
                        WriteStub(stubPath, expectedContent);
                    }

                    continue;
                }

                var actual = File.ReadAllText(stubPath);
                if (!string.Equals(NormalizeNewlines(actual), NormalizeNewlines(expectedContent), StringComparison.Ordinal))
                {
                    if (mode == SyncMode.Check)
                    {
                        issues.Add(new SyncIssue("stale-stub", $"Stale stub for '{body.Name}': {ToRepoRelative(stubPath)}"));
                    }
                    else
                    {
                        WriteStub(stubPath, expectedContent);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(body.AssertMdc))
            {
                var mdcPath = Path.Combine(_repositoryRoot, ".cursor", "rules", $"{body.AssertMdc}.mdc");
                if (!File.Exists(mdcPath))
                {
                    issues.Add(new SyncIssue("missing-mdc", $"Missing assert-only MDC rule for '{body.Name}': {ToRepoRelative(mdcPath)}"));
                }
            }

            foreach (var subagent in body.Subagents)
            {
                var agentPath = Path.Combine(_repositoryRoot, ".claude", "agents", $"{subagent}.md");
                if (!File.Exists(agentPath))
                {
                    issues.Add(new SyncIssue("missing-subagent", $"Missing assert-only subagent for '{body.Name}': {ToRepoRelative(agentPath)}"));
                }
            }
        }

        PruneOrphans(bodyNames, issues, mode);
        return issues.OrderBy(i => i.Code).ThenBy(i => i.Message).ToArray();
    }

    private void PruneOrphans(HashSet<string> bodyNames, List<SyncIssue> issues, SyncMode mode)
    {
        var managedDirectories = new[]
        {
            Path.Combine(_repositoryRoot, ".cursor", "commands"),
            Path.Combine(_repositoryRoot, ".claude", "commands"),
            Path.Combine(_repositoryRoot, ".github", "prompts")
        };

        foreach (var directory in managedDirectories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(directory))
            {
                var fileName = Path.GetFileName(filePath);
                if (directory.EndsWith("prompts", StringComparison.OrdinalIgnoreCase)
                    && !fileName.EndsWith(".prompt.md", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = directory.EndsWith("prompts", StringComparison.OrdinalIgnoreCase)
                    ? fileName[..^".prompt.md".Length]
                    : Path.GetFileNameWithoutExtension(fileName);

                if (bodyNames.Contains(name))
                {
                    continue;
                }

                if (mode == SyncMode.Check)
                {
                    issues.Add(new SyncIssue("orphan-stub", $"Orphan stub with no canonical body: {ToRepoRelative(filePath)}"));
                }
                else
                {
                    File.Delete(filePath);
                }
            }
        }
    }

    public IReadOnlyList<string> ValidatePointerIntegrity()
    {
        var missing = new List<string>();
        var stubRoots = new[]
        {
            Path.Combine(_repositoryRoot, ".cursor", "commands"),
            Path.Combine(_repositoryRoot, ".claude", "commands"),
            Path.Combine(_repositoryRoot, ".github", "prompts")
        };

        foreach (var root in stubRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var stubPath in Directory.EnumerateFiles(root))
            {
                var text = File.ReadAllText(stubPath);
                foreach (var reference in ExtractReferences(text))
                {
                    var target = Path.Combine(_repositoryRoot, reference.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(target))
                    {
                        missing.Add($"{ToRepoRelative(stubPath)} -> {reference}");
                    }
                }
            }
        }

        return missing;
    }

    private static IEnumerable<string> ExtractReferences(string text)
    {
        const string marker = "harness/";
        var index = 0;
        while ((index = text.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var start = index;
            index += marker.Length;
            while (index < text.Length)
            {
                var ch = text[index];
                if (char.IsWhiteSpace(ch) || ch is '`' or ')' or ']' or '"' or '\'' or '.')
                {
                    break;
                }

                index++;
            }

            var reference = text[start..index].TrimEnd('`', ')', ']', '"', '\'', '.');
            if (reference.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                yield return reference;
            }
        }
    }

    private static void WriteStub(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }

    private string ToRepoRelative(string absolutePath)
    {
        return Path.GetRelativePath(_repositoryRoot, absolutePath).Replace('\\', '/');
    }

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n");
}