namespace HarnessSync;

public static class StubTemplates
{
    public static string CursorCommand(HarnessBody body) =>
        $"Follow `@{body.RelativePath}` and execute it completely.\n";

    public static string ClaudeCommand(HarnessBody body) =>
        $"Follow @{body.RelativePath} and execute it completely.\n";

    public static string CopilotPrompt(HarnessBody body)
    {
        var mode = string.IsNullOrWhiteSpace(body.Mode) ? "agent" : body.Mode;
        return $"---\nagent: {mode}\ndescription: {body.Description}\n---\n\n#file:{body.RelativePath}\n";
    }

    public static IReadOnlyDictionary<string, string> ExpectedStubs(string repositoryRoot, HarnessBody body)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(repositoryRoot, ".cursor", "commands", $"{body.Name}.md")] = CursorCommand(body),
            [Path.Combine(repositoryRoot, ".claude", "commands", $"{body.Name}.md")] = ClaudeCommand(body),
            [Path.Combine(repositoryRoot, ".github", "prompts", $"{body.Name}.prompt.md")] = CopilotPrompt(body)
        };
    }
}