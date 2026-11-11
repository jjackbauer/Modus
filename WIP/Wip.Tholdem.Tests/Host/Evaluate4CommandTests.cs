using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;
using Xunit;

namespace Wip.Tholdem.Tests.Host;

/// <summary>
/// The additive <c>evaluate4</c> command: it reconstructs a pre-registered four-seat plan from disk,
/// writes the canonical paired report, and optionally proves the anchor/gate/verification chain while
/// never writing a champion pointer.
/// </summary>
public sealed class Evaluate4CommandTests
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public void Run_GivenPlanAndArtifacts_WritesCanonicalReport()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-evaluate4-report");
        try
        {
            var (challenger, incumbent) = WriteStrategies(dataDirectory);
            var plan = CreatePlan(FrozenOpponentPool.SelectionPoolId, "primary-mixed");
            var planPath = WritePlan(dataDirectory, "plan.json", plan);

            var result = HostCliTestSupport.Run(
                "evaluate4",
                "--plan",
                planPath,
                "--challenger",
                challenger.Id,
                "--incumbent",
                incumbent.Id,
                "--data-dir",
                dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);
            Assert.Contains("plan hash: " + plan.PlanHash, result.Output, StringComparison.Ordinal);

            var outputDirectory = Path.Combine(dataDirectory, "evaluations4");
            var reportPath = Assert.Single(Directory.GetFiles(outputDirectory, "report-*.json"));
            var bytes = File.ReadAllBytes(reportPath);
            Assert.False(HasUtf8Bom(bytes), "the report must be UTF-8 without a BOM.");
            var canonical = File.ReadAllText(reportPath, Utf8);
            Assert.DoesNotContain("\n", canonical, StringComparison.Ordinal);

            using var document = JsonDocument.Parse(canonical);
            var root = document.RootElement;
            Assert.Equal("tholdem.population-report.v2", root.GetProperty("schema").GetString());
            Assert.Equal(plan.PlanHash, root.GetProperty("planHash").GetString());
            Assert.Equal(4, root.GetProperty("units").GetArrayLength());
            Assert.False(
                File.Exists(Path.Combine(dataDirectory, "active-champion.json")),
                "evaluate4 must not write a champion pointer.");

            // --json prints the canonical report, and --out redirects the artifact directory.
            var customOutputDirectory = Path.Combine(dataDirectory, "custom-out");
            var jsonResult = HostCliTestSupport.Run(
                "evaluate4",
                "--plan",
                planPath,
                "--challenger",
                challenger.Id,
                "--incumbent",
                incumbent.Id,
                "--out",
                customOutputDirectory,
                "--json",
                "--data-dir",
                dataDirectory);

            Assert.Equal(0, jsonResult.ExitCode);
            Assert.Equal(string.Empty, jsonResult.Error);
            using (var jsonDocument = JsonDocument.Parse(jsonResult.Output.Trim()))
            {
                Assert.Equal(
                    "tholdem.population-report.v2",
                    jsonDocument.RootElement.GetProperty("schema").GetString());
                Assert.Equal(
                    plan.PlanHash,
                    jsonDocument.RootElement.GetProperty("planHash").GetString());
            }

            Assert.Single(Directory.GetFiles(customOutputDirectory, "report-*.json"));

            Console.WriteLine(
                "observed evaluate4 report: planHash="
                + root.GetProperty("planHash").GetString()
                + " units="
                + root.GetProperty("units").GetArrayLength()
                + " file="
                + Path.GetFileName(reportPath));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenEquityPolicyChallenger_WritesReportWithPolicyHash()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-evaluate4-equity-policy");
        try
        {
            var (_, incumbent) = WriteStrategies(dataDirectory);
            var policy = new EquityPolicy(EquitySamples: 50);
            var policyJson = EquityPolicyJson.Write(policy);
            var policyHash = EquityPolicyJson.Hash(policy);
            var policyId = "tholdem-equity-policy-v1-" + policyHash[..8];
            var policyDirectory = Path.Combine(dataDirectory, "equity-policies");
            Directory.CreateDirectory(policyDirectory);
            File.WriteAllText(Path.Combine(policyDirectory, policyId + ".json"), policyJson, Utf8);

            var plan = CreatePlan(FrozenOpponentPool.SelectionPoolId, "primary-mixed");
            var planPath = WritePlan(dataDirectory, "plan.json", plan);

            var result = HostCliTestSupport.Run(
                "evaluate4",
                "--plan",
                planPath,
                "--challenger",
                policyId,
                "--challenger-kind",
                "equity-policy",
                "--incumbent",
                incumbent.Id,
                "--incumbent-kind",
                "strategy",
                "--data-dir",
                dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);

            var outputDirectory = Path.Combine(dataDirectory, "evaluations4");
            var reportPath = Assert.Single(Directory.GetFiles(outputDirectory, "report-*.json"));
            var canonicalReport = File.ReadAllText(reportPath, Utf8);
            var reportHash = StrategyArtifact.ComputeHash(canonicalReport);
            using var document = JsonDocument.Parse(canonicalReport);
            var root = document.RootElement;
            Assert.Equal("tholdem.population-report.v2", root.GetProperty("schema").GetString());
            Assert.Equal(policyId, root.GetProperty("challengerId").GetString());
            Assert.Equal(policyHash, root.GetProperty("challengerPolicyHash").GetString());
            Assert.Equal(incumbent.Hash, root.GetProperty("incumbentPolicyHash").GetString());
            Assert.Equal(4, root.GetProperty("units").GetArrayLength());
            Assert.Equal("report-" + reportHash[..8] + ".json", Path.GetFileName(reportPath));
            Assert.False(
                File.Exists(Path.Combine(dataDirectory, "active-champion.json")),
                "evaluate4 must not write a champion pointer.");

            Console.WriteLine(
                "observed equity-policy report: challengerPolicyHash="
                + root.GetProperty("challengerPolicyHash").GetString()
                + " reportHash="
                + reportHash
                + " report="
                + Path.GetFileName(reportPath));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenAnchorPlan_WritesDecisionAndVerifiedVerification()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-evaluate4-anchor");
        try
        {
            var (challenger, incumbent) = WriteStrategies(dataDirectory);
            var plan = CreatePlan(FrozenOpponentPool.SelectionPoolId, "primary-mixed");
            var anchorPlan = CreatePlan(FrozenOpponentPool.HoldoutPoolId, "primary-mixed");
            var planPath = WritePlan(dataDirectory, "plan.json", plan);
            var anchorPlanPath = WritePlan(dataDirectory, "anchor-plan.json", anchorPlan);

            var result = HostCliTestSupport.Run(
                "evaluate4",
                "--plan",
                planPath,
                "--anchor-plan",
                anchorPlanPath,
                "--challenger",
                challenger.Id,
                "--incumbent",
                incumbent.Id,
                "--data-dir",
                dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);

            var outputDirectory = Path.Combine(dataDirectory, "evaluations4");
            var decisionPath = Assert.Single(Directory.GetFiles(outputDirectory, "decision-*.json"));
            var verificationPath = Assert.Single(Directory.GetFiles(outputDirectory, "verification-*.json"));

            using var decisionDocument = JsonDocument.Parse(File.ReadAllText(decisionPath, Utf8));
            var decisionRoot = decisionDocument.RootElement;
            Assert.Equal("tholdem.evaluate4-decision.v1", decisionRoot.GetProperty("schema").GetString());
            Assert.False(string.IsNullOrWhiteSpace(decisionRoot.GetProperty("verdict").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(decisionRoot.GetProperty("reason").GetString()));

            using var verificationDocument = JsonDocument.Parse(File.ReadAllText(verificationPath, Utf8));
            var verificationRoot = verificationDocument.RootElement;
            Assert.Equal(
                "tholdem.evaluate4-verification.v1",
                verificationRoot.GetProperty("schema").GetString());
            Assert.True(verificationRoot.GetProperty("verified").GetBoolean());
            Assert.Equal(
                PopulationPromotionVerifier.VerifiedReason,
                verificationRoot.GetProperty("reason").GetString());
            Assert.Equal(
                decisionRoot.GetProperty("verdict").GetString(),
                verificationRoot.GetProperty("verdict").GetString());
            Assert.Equal(
                decisionRoot.GetProperty("reason").GetString(),
                verificationRoot.GetProperty("gateReason").GetString());
            Assert.False(
                File.Exists(Path.Combine(dataDirectory, "active-champion.json")),
                "evaluate4 must not write a champion pointer.");

            Console.WriteLine(
                "observed evaluate4 gate: verdict="
                + decisionRoot.GetProperty("verdict").GetString()
                + " reason="
                + decisionRoot.GetProperty("reason").GetString()
                + " verifier="
                + verificationRoot.GetProperty("reason").GetString()
                + " decision="
                + Path.GetFileName(decisionPath)
                + " verification="
                + Path.GetFileName(verificationPath));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenUnknownPool_ExitArtifact()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-evaluate4-unknown-pool");
        try
        {
            var (challenger, incumbent) = WriteStrategies(dataDirectory);
            var plan = CreatePlan("tholdem.opponents.unknown.v1", "primary-mixed");
            var planPath = WritePlan(dataDirectory, "plan.json", plan);

            var result = HostCliTestSupport.Run(
                "evaluate4",
                "--plan",
                planPath,
                "--challenger",
                challenger.Id,
                "--incumbent",
                incumbent.Id,
                "--data-dir",
                dataDirectory);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("unknown-opponent-pool", result.Error, StringComparison.Ordinal);
            Assert.Contains("tholdem.opponents.unknown.v1", result.Error, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(dataDirectory, "evaluations4")));

            Console.WriteLine("observed evaluate4 unknown-pool stderr: " + result.Error.Trim());
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenUnknownComposition_ExitArtifact()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-evaluate4-unknown-composition");
        try
        {
            var (challenger, incumbent) = WriteStrategies(dataDirectory);
            var plan = CreatePlan(FrozenOpponentPool.SelectionPoolId, "missing-composition");
            var planPath = WritePlan(dataDirectory, "plan.json", plan);

            var result = HostCliTestSupport.Run(
                "evaluate4",
                "--plan",
                planPath,
                "--challenger",
                challenger.Id,
                "--incumbent",
                incumbent.Id,
                "--data-dir",
                dataDirectory);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("evaluation-plan-invalid", result.Error, StringComparison.Ordinal);
            Assert.Contains("missing-composition", result.Error, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(dataDirectory, "evaluations4")));

            Console.WriteLine("observed evaluate4 unknown-composition stderr: " + result.Error.Trim());
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenMalformedPlan_ExitArtifact()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-evaluate4-malformed");
        try
        {
            var (challenger, incumbent) = WriteStrategies(dataDirectory);
            var planPath = Path.Combine(dataDirectory, "plan.json");
            File.WriteAllText(planPath, "{", Utf8);

            var result = HostCliTestSupport.Run(
                "evaluate4",
                "--plan",
                planPath,
                "--challenger",
                challenger.Id,
                "--incumbent",
                incumbent.Id,
                "--data-dir",
                dataDirectory);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("plan-not-json", result.Error, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(dataDirectory, "evaluations4")));

            Console.WriteLine("observed evaluate4 malformed-plan stderr: " + result.Error.Trim());
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static EvaluationPlanV2 CreatePlan(string opponentPoolId, string composition) =>
        EvaluationPlanV2.Create(
            GameConfiguration.FourSeatCash(),
            opponentPoolId,
            new[] { composition },
            new[]
            {
                new SeedDomain("training", SeedPurpose.Training, 0x2001UL, 2),
                new SeedDomain("selection", SeedPurpose.Selection, 0x2002UL, 2),
                new SeedDomain("holdout", SeedPurpose.Holdout, 0x2003UL, 4),
                new SeedDomain("audit", SeedPurpose.Audit, 0x2004UL, 2),
            },
            handsPerSeatMatch: 2,
            minimumPracticalEffectBbPer100: 2.0,
            confidenceLevel: 0.95,
            plannedCandidates: 5,
            anchorRegressionToleranceBbPer100: 2.0);

    private static (StrategyArtifact Challenger, StrategyArtifact Incumbent) WriteStrategies(
        string dataDirectory)
    {
        var incumbent = StrategyArtifact.Create(
            new StrategyGenome(
                RaiseThreshold: 0.60,
                CallThreshold: 0.40,
                BluffFrequency: 0.10,
                SizingMultiplier: 1.0,
                PromptText: "incumbent prompt"),
            iteration: 0,
            parent: null);
        var challenger = StrategyArtifact.Create(
            new StrategyGenome(
                RaiseThreshold: 0.80,
                CallThreshold: 0.55,
                BluffFrequency: 0.20,
                SizingMultiplier: 1.5,
                PromptText: "challenger prompt"),
            iteration: 1,
            parent: incumbent);
        HostCliTestSupport.WriteStrategy(dataDirectory, incumbent);
        HostCliTestSupport.WriteStrategy(dataDirectory, challenger);
        return (challenger, incumbent);
    }

    private static string WritePlan(string dataDirectory, string fileName, EvaluationPlanV2 plan)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, fileName);
        File.WriteAllText(path, EvaluationPlanV2Json.Write(plan), Utf8);
        return path;
    }

    private static bool HasUtf8Bom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
}
