using HarnessSync;

namespace HarnessSync.Tests;

public sealed class HarnessSyncFixture : IDisposable
{
    public string Root { get; }

    public HarnessSyncFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "harness-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        SeedMinimalRepository();
    }

    private void SeedMinimalRepository()
    {
        WriteBody(
            "harness/skills/sample-skill.md",
            """
            ---
            kind: skill
            mode: agent
            description: Sample skill for sync tests
            ---
            # Sample Skill
            """);

        WriteBody(
            "harness/workflows/sample-workflow.md",
            """
            ---
            kind: workflow
            mode: plan
            description: Sample workflow for sync tests
            ---
            # Sample Workflow
            """);

        WriteBody(
            "harness/rules/sample-rule.md",
            """
            ---
            kind: rule
            mode: agent
            description: Sample rule for sync tests
            assertMdc: sample-rule
            subagents:
              - verifier
            ---
            # Sample Rule
            """);

        WriteBody(
            "harness/schemas/reference-only.md",
            """
            ---
            kind: schema
            description: Reference schema should not emit commands
            ---
            # Schema
            """);

        Directory.CreateDirectory(Path.Combine(Root, ".cursor", "rules"));
        File.WriteAllText(
            Path.Combine(Root, ".cursor", "rules", "sample-rule.mdc"),
            "---\nalwaysApply: false\n---\n");

        Directory.CreateDirectory(Path.Combine(Root, ".claude", "agents"));
        File.WriteAllText(
            Path.Combine(Root, ".claude", "agents", "verifier.md"),
            "# Verifier subagent\n");
    }

    public void WriteBody(string relativePath, string content)
    {
        var fullPath = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }

    public SyncEngine CreateEngine() => new(Root);

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

public class HarnessSyncTests
{
    [Fact]
    public void Generate_GivenSkillBody_WritesCursorClaudeAndCopilotStubsPointingAtCanonicalPath()
    {
        using var fixture = new HarnessSyncFixture();
        var issues = fixture.CreateEngine().Run(SyncMode.Generate);

        var cursor = File.ReadAllText(Path.Combine(fixture.Root, ".cursor", "commands", "sample-skill.md"));
        var claude = File.ReadAllText(Path.Combine(fixture.Root, ".claude", "commands", "sample-skill.md"));
        var copilot = File.ReadAllText(Path.Combine(fixture.Root, ".github", "prompts", "sample-skill.prompt.md"));

        Assert.Contains("@harness/skills/sample-skill.md", cursor);
        Assert.Contains("@harness/skills/sample-skill.md", claude);
        Assert.Contains("#file:harness/skills/sample-skill.md", copilot);
        Assert.DoesNotContain(issues, i => i.Code == "missing-stub" && i.Message.Contains("sample-skill"));
    }

    [Fact]
    public void Generate_GivenWorkflowWithPlanMode_ExpectedCopilotStubFrontmatterAgentPlan()
    {
        using var fixture = new HarnessSyncFixture();
        fixture.CreateEngine().Run(SyncMode.Generate);

        var copilot = File.ReadAllText(Path.Combine(fixture.Root, ".github", "prompts", "sample-workflow.prompt.md"));
        Assert.Contains("agent: plan", copilot);
    }

    [Fact]
    public void Generate_GivenRunTwice_ExpectedByteIdenticalOutputs()
    {
        using var fixture = new HarnessSyncFixture();
        var engine = fixture.CreateEngine();
        engine.Run(SyncMode.Generate);

        var firstCursor = File.ReadAllBytes(Path.Combine(fixture.Root, ".cursor", "commands", "sample-skill.md"));
        var firstClaude = File.ReadAllBytes(Path.Combine(fixture.Root, ".claude", "commands", "sample-skill.md"));
        var firstCopilot = File.ReadAllBytes(Path.Combine(fixture.Root, ".github", "prompts", "sample-skill.prompt.md"));

        engine.Run(SyncMode.Generate);

        Assert.Equal(firstCursor, File.ReadAllBytes(Path.Combine(fixture.Root, ".cursor", "commands", "sample-skill.md")));
        Assert.Equal(firstClaude, File.ReadAllBytes(Path.Combine(fixture.Root, ".claude", "commands", "sample-skill.md")));
        Assert.Equal(firstCopilot, File.ReadAllBytes(Path.Combine(fixture.Root, ".github", "prompts", "sample-skill.prompt.md")));
    }

    [Fact]
    public void Generate_GivenOrphanStubWithNoCanonicalBody_ExpectedStubPrunedFromAllToolDirs()
    {
        using var fixture = new HarnessSyncFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, ".cursor", "commands"));
        Directory.CreateDirectory(Path.Combine(fixture.Root, ".claude", "commands"));
        Directory.CreateDirectory(Path.Combine(fixture.Root, ".github", "prompts"));

        File.WriteAllText(Path.Combine(fixture.Root, ".cursor", "commands", "orphan.md"), "orphan");
        File.WriteAllText(Path.Combine(fixture.Root, ".claude", "commands", "orphan.md"), "orphan");
        File.WriteAllText(Path.Combine(fixture.Root, ".github", "prompts", "orphan.prompt.md"), "orphan");

        fixture.CreateEngine().Run(SyncMode.Generate);

        Assert.False(File.Exists(Path.Combine(fixture.Root, ".cursor", "commands", "orphan.md")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".claude", "commands", "orphan.md")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".github", "prompts", "orphan.prompt.md")));
    }

    [Fact]
    public void Generate_GivenSchemaFile_ExpectedNoCommandStubsEmitted()
    {
        using var fixture = new HarnessSyncFixture();
        fixture.CreateEngine().Run(SyncMode.Generate);

        Assert.False(File.Exists(Path.Combine(fixture.Root, ".cursor", "commands", "reference-only.md")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".claude", "commands", "reference-only.md")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".github", "prompts", "reference-only.prompt.md")));
    }

    [Fact]
    public void Check_GivenMissingCursorStub_ExpectedNonZeroExitNamingTheBody()
    {
        using var fixture = new HarnessSyncFixture();
        var issues = fixture.CreateEngine().Run(SyncMode.Check);

        Assert.Contains(issues, i => i.Code == "missing-stub" && i.Message.Contains("sample-skill"));
    }

    [Fact]
    public void Check_GivenStubDriftedFromTemplate_ExpectedFailureIdentifyingStaleStub()
    {
        using var fixture = new HarnessSyncFixture();
        fixture.CreateEngine().Run(SyncMode.Generate);

        var cursorPath = Path.Combine(fixture.Root, ".cursor", "commands", "sample-skill.md");
        File.WriteAllText(cursorPath, "stale content");

        var issues = fixture.CreateEngine().Run(SyncMode.Check);
        Assert.Contains(issues, i => i.Code == "stale-stub" && i.Message.Contains("sample-skill"));
    }

    [Fact]
    public void Check_GivenOrphanStub_ExpectedFailureIdentifyingOrphan()
    {
        using var fixture = new HarnessSyncFixture();
        fixture.CreateEngine().Run(SyncMode.Generate);

        File.WriteAllText(Path.Combine(fixture.Root, ".cursor", "commands", "orphan.md"), "orphan");

        var issues = fixture.CreateEngine().Run(SyncMode.Check);
        Assert.Contains(issues, i => i.Code == "orphan-stub");
    }

    [Fact]
    public void Check_GivenRuleBodyWithoutMdcCounterpart_ExpectedAssertOnlyFailure()
    {
        using var fixture = new HarnessSyncFixture();
        File.Delete(Path.Combine(fixture.Root, ".cursor", "rules", "sample-rule.mdc"));
        fixture.CreateEngine().Run(SyncMode.Generate);

        var issues = fixture.CreateEngine().Run(SyncMode.Check);
        Assert.Contains(issues, i => i.Code == "missing-mdc" && i.Message.Contains("sample-rule"));
    }

    [Fact]
    public void Check_GivenSubagentDeclaredButMissingAgentFile_ExpectedAssertOnlyFailure()
    {
        using var fixture = new HarnessSyncFixture();
        File.Delete(Path.Combine(fixture.Root, ".claude", "agents", "verifier.md"));
        fixture.CreateEngine().Run(SyncMode.Generate);

        var issues = fixture.CreateEngine().Run(SyncMode.Check);
        Assert.Contains(issues, i => i.Code == "missing-subagent" && i.Message.Contains("verifier"));
    }

    [Fact]
    public void Generate_GivenUnsortedFilesystemEnumeration_ExpectedDeterministicStableStubOrdering()
    {
        using var fixture = new HarnessSyncFixture();
        fixture.WriteBody(
            "harness/skills/zebra.md",
            """
            ---
            kind: skill
            mode: agent
            description: Zebra
            ---
            # Z
            """);
        fixture.WriteBody(
            "harness/skills/alpha.md",
            """
            ---
            kind: skill
            mode: agent
            description: Alpha
            ---
            # A
            """);

        var engine = fixture.CreateEngine();
        engine.Run(SyncMode.Generate);
        var firstListing = Directory.GetFiles(Path.Combine(fixture.Root, ".cursor", "commands"))
            .Select(Path.GetFileName)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        engine.Run(SyncMode.Generate);
        var secondListing = Directory.GetFiles(Path.Combine(fixture.Root, ".cursor", "commands"))
            .Select(Path.GetFileName)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(firstListing, secondListing);
        Assert.Equal(new[] { "alpha.md", "sample-rule.md", "sample-skill.md", "sample-workflow.md", "zebra.md" }, firstListing);
    }

    [Fact]
    public void RoundTrip_GivenGenerateThenCheck_ExpectedCheckPassesWithZeroDrift()
    {
        using var fixture = new HarnessSyncFixture();
        var engine = fixture.CreateEngine();
        engine.Run(SyncMode.Generate);

        var issues = engine.Run(SyncMode.Check);
        var pointerIssues = engine.ValidatePointerIntegrity();

        Assert.Empty(issues);
        Assert.Empty(pointerIssues);
    }

    [Fact]
    public void PointerIntegrity_GivenGeneratedStubs_ExpectedEveryReferenceResolvesToExistingCanonicalBody()
    {
        using var fixture = new HarnessSyncFixture();
        var engine = fixture.CreateEngine();
        engine.Run(SyncMode.Generate);

        var pointerIssues = engine.ValidatePointerIntegrity();
        Assert.Empty(pointerIssues);
    }
}
