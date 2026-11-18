using System.Diagnostics;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Wip.Governance;
using Wip.Factory.Wip.Workspaces;
using Wip.Validation.DotNet.DotNet;
using Wip.Workspaces.Git;

namespace Wip.Factory.Host.Demo;

/// <summary>
/// The D5 demonstration: one requirement travels from submission to an operator-approved merge, with a
/// durable evidence chain. It uses a purpose-built temporary Git repository (DEC-010) so that the
/// validation step runs a real <c>dotnet build</c> and <c>dotnet test</c> in seconds, and a real
/// <c>git merge</c> for the merge step.
/// </summary>
public sealed class RequirementToMergeDemo
{
    private const string FixtureProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;

    private const string FixtureTestsProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
            <PackageReference Include="xunit" Version="2.9.3" />
            <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
          </ItemGroup>
          <ItemGroup>
            <ProjectReference Include="..\Fixture\Fixture.csproj" />
          </ItemGroup>
        </Project>
        """;

    private const string FixtureCode = """
        namespace Fixture;

        public static class Adder
        {
            public static int Add(int left, int right) => left + right;
        }
        """;

    private const string FixtureTests = """
        using Xunit;

        namespace Fixture.Tests;

        public sealed class AdderTests
        {
            [Fact]
            public void Add_GivenTwoIntegers_ReturnsSum() => Assert.Equal(5, Fixture.Adder.Add(2, 3));
        }
        """;

    public async Task<int> RunAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var workspaceRoot = Path.Combine(outputDirectory, "workspace");
        var repositoryPath = Path.Combine(workspaceRoot, "factory-target");
        await CreateFixtureRepositoryAsync(repositoryPath, cancellationToken);

        // 1. A requirement is submitted (a committed fact, not the source of truth).
        var requirement = new ReviewRequirement("Q-87", "1", ["AC-1"]);
        await WriteAsync(outputDirectory, "01-requirement.json", requirement, cancellationToken);

        // 2. The implementing session gets its own worktree (F-F.1).
        var provisioner = new FactoryWorktreeProvisioner();
        var (lease, leaseReason) = await provisioner.ProvisionAsync(
            repositoryPath,
            new SessionId("session-implementation"),
            "main",
            cancellationToken);

        if (lease is null)
        {
            return Fail($"worktree provisioning failed: {leaseReason}");
        }

        // 3. The implementation changes its own worktree only.
        await File.WriteAllTextAsync(
            Path.Combine(lease.WorktreePath, "Fixture", "Adder.cs"),
            FixtureCode.Replace("left + right", "left + right /* implemented */", StringComparison.Ordinal),
            cancellationToken);
        await RunGitAsync(lease.WorktreePath, cancellationToken, "add", ".");
        await RunGitAsync(
            lease.WorktreePath,
            cancellationToken,
            "-c", "user.email=factory@example.test",
            "-c", "user.name=Factory",
            "commit", "-m", "implement Q-87");

        // 4. Validation runs a real dotnet build and test through Wip.Validation.DotNet (F-F.2).
        var artifactRoot = Path.Combine(outputDirectory, "artifacts");
        var artifactStore = new global::Wip.Artifacts.Local.WipArtifactStoreLocal(artifactRoot);
        var validator = new DotNetValidationValidator(artifactStore, new WipWorkspaceProviderGit());
        var validationRequest = new DotNetValidationRequest(
            Path.Combine(lease.WorktreePath, "Fixture", "Fixture.csproj"),
            Path.Combine(lease.WorktreePath, "Fixture.Tests", "Fixture.Tests.csproj"),
            lease.WorktreePath);

        var validation = await validator.ExecuteAsync(
            validationRequest,
            new CapabilityContext(lease.SessionId, lease.WorktreePath),
            cancellationToken);

        var validationPassed = validation.Succeeded;
        var validationDetail =
            $"restore={validation.Report.Restore.Succeeded} build={validation.Report.Build.Succeeded} " +
            $"tests={validation.Report.Test.Succeeded}";
        await WriteAsync(outputDirectory, "02-validation.json", new { validationPassed, validationDetail }, cancellationToken);

        // 5. A review executes all four checks (F-N.1, F-N.2).
        var headSha = (await RunGitAsync(lease.WorktreePath, cancellationToken, "rev-parse", "HEAD")).Trim();
        var baseSha = (await RunGitAsync(repositoryPath, cancellationToken, "rev-parse", "main")).Trim();

        var baseline = new ReviewBaseline(
            headSha,
            baseSha,
            "main",
            "policy://factory/mandatory@1",
            "guidelines/system@1");

        var reviewService = new FactoryReviewService();
        var reviewResult = reviewService.Evaluate(
            requirement,
            baseline,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Wip.Validation.DotNet"] = "1.0.0",
                ["requirements-coverage"] = "1.0.0",
                ["architecture-conformance"] = "1.0.0",
                ["security"] = "1.0.0"
            },
            [
                new ReviewCheckResult(ReviewCheckKind.Validation, validationPassed, validationDetail),
                new ReviewCheckResult(ReviewCheckKind.RequirementCoverage, true, "AC-1 covered by AdderTests"),
                new ReviewCheckResult(ReviewCheckKind.ArchitecturalConformance, true, "factory-no-bones holds"),
                new ReviewCheckResult(ReviewCheckKind.Security, true, "no secrets, no new dependency")
            ],
            [],
            [],
            "low",
            new AgentProfileVersion(1),
            new SessionId("session-review"));

        if (!reviewResult.Accepted || reviewResult.Artifact is null)
        {
            await WriteAsync(outputDirectory, "03-review-rejected.json", new { reviewResult.Reason }, cancellationToken);
            return Fail($"review not accepted: {reviewResult.Reason}");
        }

        await WriteAsync(outputDirectory, "03-review.json", reviewResult.Artifact, cancellationToken);

        // 6. The operator approves the exact reviewed content (F-F.4).
        var approvals = new FactoryApprovalService();
        var (approval, approvalReason) = approvals.Create(reviewResult.Artifact, DateTimeOffset.UtcNow);
        if (approval is null)
        {
            return Fail($"approval refused: {approvalReason}");
        }

        await WriteAsync(outputDirectory, "04-approval.json", approval, cancellationToken);

        var currentBaseline = baseline;
        var currentRequirement = requirement;

        var mergeGate = new FactoryMergeGate(approvals);
        var (merged, mergeReason) = await mergeGate.MergeAsync(
            approval,
            reviewResult.Artifact,
            currentBaseline,
            currentRequirement,
            async ct =>
            {
                // A real merge of the session branch into the target branch.
                await RunGitAsync(
                    repositoryPath,
                    ct,
                    "-c", "user.email=factory@example.test",
                    "-c", "user.name=Factory",
                    "merge", "--no-ff", "-m", "merge Q-87", lease.SessionBranch);
            },
            cancellationToken);

        var mergeHead = (await RunGitAsync(repositoryPath, cancellationToken, "rev-parse", "HEAD")).Trim();
        var mergedContent = await File.ReadAllTextAsync(
            Path.Combine(repositoryPath, "Fixture", "Adder.cs"),
            cancellationToken);

        await WriteAsync(
            outputDirectory,
            "05-merge.json",
            new { merged, mergeReason, mergeHead, baseSha, headSha, containsImplementation = mergedContent.Contains("implemented", StringComparison.Ordinal) },
            cancellationToken);

        Console.WriteLine($"requirement : {requirement.ArtifactId} v{requirement.Version}");
        Console.WriteLine($"worktree    : {lease.WorktreePath}");
        Console.WriteLine($"branch      : {lease.SessionBranch}");
        Console.WriteLine($"validation  : {validationDetail}");
        Console.WriteLine($"review      : {reviewResult.Artifact.ReviewId} disposition={reviewResult.Artifact.Disposition} digest={reviewResult.Artifact.ContentDigest}");
        Console.WriteLine($"approval    : {approval.ApprovalId}");
        Console.WriteLine($"merged      : {merged} head={mergeHead}");
        Console.WriteLine($"evidence    : {Path.GetFullPath(outputDirectory)}");

        return merged ? 0 : Fail($"merge refused: {mergeReason}");
    }

    private static int Fail(string reason)
    {
        Console.Error.WriteLine(reason);
        return 1;
    }

    private static async Task WriteAsync<T>(string directory, string fileName, T value, CancellationToken cancellationToken)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            value,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(directory, fileName), json, cancellationToken);
    }

    private static async Task CreateFixtureRepositoryAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(repositoryPath, "Fixture"));
        Directory.CreateDirectory(Path.Combine(repositoryPath, "Fixture.Tests"));

        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "Fixture", "Fixture.csproj"), FixtureProject, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "Fixture", "Adder.cs"), FixtureCode, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "Fixture.Tests", "Fixture.Tests.csproj"), FixtureTestsProject, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "Fixture.Tests", "AdderTests.cs"), FixtureTests, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(repositoryPath, ".gitignore"),
            "bin/\nobj/\n",
            cancellationToken);

        await RunGitAsync(repositoryPath, cancellationToken, "init", "-b", "main");
        await RunGitAsync(repositoryPath, cancellationToken, "add", ".");
        await RunGitAsync(
            repositoryPath,
            cancellationToken,
            "-c", "user.email=factory@example.test",
            "-c", "user.name=Factory",
            "commit", "-m", "initial fixture");
    }

    private static async Task<string> RunGitAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start git.");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr}");
        }

        return stdout;
    }
}
