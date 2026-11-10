using System.Text.RegularExpressions;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// Absolute behavior-proof gate: the checklist is only compliant when every requirement row names
/// executable runtime proof on disk, not a type name or a plan. The validator reads the real
/// markdown and the named paths; it never calls a model and never opens a network connection.
/// </summary>
public sealed class BehaviorProofComplianceTests
{
    private const string DocumentRelativePath = "docs/goal-llm-harness/behavior-proof.md";

    private static readonly string[] ExpectedRequirementIds =
        ["M-01", "M-02", "M-03", "M-04", "M-05", "M-06", "M-07", "M-08", "M-09", "M-10", "M-11", "M-12", "M-13", "M-14", "M-15"];

    private static readonly string[] ApiFocusedRequirements = ["M-01", "M-04", "M-07", "M-14"];

    private static readonly string[] CategoryNames =
        ["Owner", "Proposal", "Lifetime/order", "Isolation", "Negative"];

    [Fact]
    public void BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEachItem()
    {
        var repositoryRoot = AgentTestSupport.RepositoryRoot();
        var table = BehaviorProofTable.Parse(ReadDocument(repositoryRoot));

        var result = BehaviorProofValidator.Validate(table.Rows, repositoryRoot);

        Assert.True(result.IsCompliant, result.Reason);
        Assert.Equal(15, table.Rows.Count);
        Assert.Equal(ExpectedRequirementIds, table.Rows.Select(static row => row.Requirement).ToArray());
    }

    [Fact]
    public void BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsPlanAsNonCompliant()
    {
        var repositoryRoot = AgentTestSupport.RepositoryRoot();
        var table = BehaviorProofTable.Parse(BuildDocument(
            "M-01",
            "WIP/Wip.Tholdem.ModelHarness/LlmRefinementPolicy.cs",
            "-",
            "-",
            "-",
            "-",
            "-"));

        var result = BehaviorProofValidator.Validate(table.Rows, repositoryRoot);

        Assert.False(result.IsCompliant);
        Assert.Contains("M-01", result.Reason, StringComparison.Ordinal);
        Assert.Contains("LlmRefinementPolicy.cs", result.Reason, StringComparison.Ordinal);
        Assert.Contains("not runtime proof", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BehaviorProofCompliance_GivenApiFocusedCoverage_RequiresOwnerSemanticsBusinessSemanticsLifetimeCorrelationIsolationAndNegativeContracts()
    {
        var repositoryRoot = AgentTestSupport.RepositoryRoot();
        const string TestProof = "WIP/Wip.Tholdem.Tests/ModelHarness/TrainingLoopLlmPolicyTests.cs";
        var synthetic = BehaviorProofTable.Parse(BuildDocument(
            "M-01",
            TestProof,
            TestProof + " (owner semantics)",
            TestProof + " (proposal semantics)",
            TestProof + " (lifetime and order)",
            string.Empty,
            TestProof + " (negative contract)"));

        var syntheticResult = BehaviorProofValidator.ValidateApiFocusedCategories(synthetic.Rows, repositoryRoot);
        Assert.False(syntheticResult.IsCompliant);
        Assert.Contains("M-01", syntheticResult.Reason, StringComparison.Ordinal);
        Assert.Contains("Isolation", syntheticResult.Reason, StringComparison.Ordinal);

        var real = BehaviorProofTable.Parse(ReadDocument(repositoryRoot));
        Assert.Equal(
            4,
            real.Rows.Count(static row => ApiFocusedRequirements.Contains(row.Requirement)));
        var realResult = BehaviorProofValidator.ValidateApiFocusedCategories(real.Rows, repositoryRoot);
        Assert.True(realResult.IsCompliant, realResult.Reason);
    }

    private static string ReadDocument(string repositoryRoot) =>
        File.ReadAllText(Path.Combine(
            repositoryRoot,
            DocumentRelativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string BuildDocument(params string[] cells) =>
        "# Synthetic behavior proof\n\n"
        + "| Requirement | Runtime proof | Owner | Proposal | Lifetime/order | Isolation | Negative |\n"
        + "|---|---|---|---|---|---|---|\n"
        + "| " + string.Join(" | ", cells) + " |\n";

    private sealed record BehaviorProofRow(
        string Requirement,
        string RuntimeProof,
        string Owner,
        string Proposal,
        string LifetimeOrder,
        string Isolation,
        string Negative)
    {
        public string Category(string name) => name switch
        {
            "Owner" => Owner,
            "Proposal" => Proposal,
            "Lifetime/order" => LifetimeOrder,
            "Isolation" => Isolation,
            "Negative" => Negative,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown category."),
        };
    }

    private sealed record BehaviorProofTable(IReadOnlyList<BehaviorProofRow> Rows)
    {
        private static readonly string[] Headers =
            ["Requirement", "Runtime proof", "Owner", "Proposal", "Lifetime/order", "Isolation", "Negative"];

        public static BehaviorProofTable Parse(string markdown)
        {
            var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var headerIndex = -1;
            for (var index = 0; index < lines.Length; index++)
            {
                var cells = SplitRow(lines[index]);
                if (cells.Count == Headers.Length && cells.SequenceEqual(Headers, StringComparer.Ordinal))
                {
                    headerIndex = index;
                    break;
                }
            }

            if (headerIndex < 0)
            {
                throw new InvalidOperationException(
                    "The behavior-proof table header was not found; expected exactly: "
                    + string.Join(" | ", Headers));
            }

            var rows = new List<BehaviorProofRow>();
            for (var index = headerIndex + 1; index < lines.Length; index++)
            {
                if (!lines[index].TrimStart().StartsWith('|'))
                {
                    break;
                }

                var cells = SplitRow(lines[index]);
                if (cells.Count != Headers.Length)
                {
                    throw new InvalidOperationException(
                        "Expected " + Headers.Length + " columns on line " + (index + 1)
                        + ", found " + cells.Count + ".");
                }

                if (IsSeparator(cells))
                {
                    continue;
                }

                rows.Add(new BehaviorProofRow(
                    cells[0],
                    cells[1],
                    cells[2],
                    cells[3],
                    cells[4],
                    cells[5],
                    cells[6]));
            }

            return new BehaviorProofTable(rows);
        }

        private static List<string> SplitRow(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('|'))
            {
                trimmed = trimmed[1..];
            }

            if (trimmed.EndsWith('|'))
            {
                trimmed = trimmed[..^1];
            }

            return trimmed.Split('|').Select(static cell => cell.Trim()).ToList();
        }

        private static bool IsSeparator(IReadOnlyList<string> cells) =>
            cells.All(static cell => cell.Length > 0 && cell.All(static character => character is '-' or ':'));
    }

    private sealed record BehaviorProofResult(bool IsCompliant, string Reason)
    {
        public static BehaviorProofResult Compliant(string reason) => new(true, reason);

        public static BehaviorProofResult NonCompliant(string reason) => new(false, reason);
    }

    private static class BehaviorProofValidator
    {
        private static readonly string[] AllowedRoots =
            ["WIP/Wip.Tholdem.Tests/", "docs/goal-llm-harness/", ".tmp/goal-llm-harness/"];

        private static readonly Regex ProofPathPattern = new(
            "(?:WIP/Wip\\.Tholdem\\.Tests/|docs/goal-llm-harness/|\\.tmp/goal-llm-harness/)"
            + "[A-Za-z0-9_./-]+\\.(?:cs|json|jsonl|log|txt)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static BehaviorProofResult Validate(
            IReadOnlyList<BehaviorProofRow> rows,
            string repositoryRoot)
        {
            foreach (var row in rows)
            {
                var proofResult = ValidateRuntimeProof(row, repositoryRoot);
                if (!proofResult.IsCompliant)
                {
                    return proofResult;
                }
            }

            var categoryResult = ValidateApiFocusedCategories(rows, repositoryRoot);
            if (!categoryResult.IsCompliant)
            {
                return categoryResult;
            }

            if (rows.Count != ExpectedRequirementIds.Length)
            {
                return BehaviorProofResult.NonCompliant(
                    "Expected exactly " + ExpectedRequirementIds.Length + " requirement rows, found " + rows.Count + ".");
            }

            var ids = rows.Select(static row => row.Requirement).ToArray();
            if (ids.Distinct(StringComparer.Ordinal).Count() != ExpectedRequirementIds.Length)
            {
                return BehaviorProofResult.NonCompliant(
                    "Requirement ids are not unique: " + string.Join(", ", ids) + ".");
            }

            foreach (var expected in ExpectedRequirementIds)
            {
                if (!ids.Contains(expected, StringComparer.Ordinal))
                {
                    return BehaviorProofResult.NonCompliant("Missing requirement row " + expected + ".");
                }
            }

            var unexpected = ids.Where(id => !ExpectedRequirementIds.Contains(id, StringComparer.Ordinal)).ToArray();
            if (unexpected.Length > 0)
            {
                return BehaviorProofResult.NonCompliant(
                    "Unexpected requirement ids: " + string.Join(", ", unexpected) + ".");
            }

            var m06 = rows.FirstOrDefault(static row => row.Requirement == "M-06");
            if (m06 is null)
            {
                return BehaviorProofResult.NonCompliant("Missing requirement row M-06.");
            }

            var m06Text = string.Join(
                " | ",
                m06.Requirement,
                m06.RuntimeProof,
                m06.Owner,
                m06.Proposal,
                m06.LifetimeOrder,
                m06.Isolation,
                m06.Negative);
            if (!m06Text.Contains("0 tool calls", StringComparison.OrdinalIgnoreCase)
                || !m06Text.Contains("33-turn", StringComparison.OrdinalIgnoreCase))
            {
                return BehaviorProofResult.NonCompliant(
                    "M-06 hides the measured tool-call gap: the row must state '0 tool calls' in the "
                    + "'33-turn' live match.");
            }

            return BehaviorProofResult.Compliant(
                "All " + rows.Count + " requirement rows carry executable runtime proof.");
        }

        public static BehaviorProofResult ValidateApiFocusedCategories(
            IReadOnlyList<BehaviorProofRow> rows,
            string repositoryRoot)
        {
            foreach (var row in rows)
            {
                if (!ApiFocusedRequirements.Contains(row.Requirement, StringComparer.Ordinal))
                {
                    continue;
                }

                foreach (var categoryName in CategoryNames)
                {
                    var value = row.Category(categoryName);
                    if (string.IsNullOrWhiteSpace(value) || value.Trim() == "-")
                    {
                        return BehaviorProofResult.NonCompliant(
                            row.Requirement + ": category '" + categoryName
                            + "' is empty; Owner, Proposal, Lifetime/order, Isolation and Negative must each "
                            + "name a test or artifact path.");
                    }

                    var pathMatch = ProofPathPattern.Match(value);
                    if (!pathMatch.Success)
                    {
                        return BehaviorProofResult.NonCompliant(
                            row.Requirement + ": category '" + categoryName
                            + "' does not name a runtime test or artifact path: " + value);
                    }

                    var pathResult = ValidateAllowedPath(
                        pathMatch.Value,
                        repositoryRoot,
                        row.Requirement + " category '" + categoryName + "'");
                    if (!pathResult.IsCompliant)
                    {
                        return pathResult;
                    }
                }
            }

            return BehaviorProofResult.Compliant(
                "Every present API-focused row names owner, proposal, lifetime/order, isolation and negative proof.");
        }

        private static BehaviorProofResult ValidateRuntimeProof(
            BehaviorProofRow row,
            string repositoryRoot)
        {
            var proofs = row.RuntimeProof.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (proofs.Length == 0)
            {
                return BehaviorProofResult.NonCompliant(
                    row.Requirement + ": runtime proof is empty; a type name or a plan is not runtime proof.");
            }

            foreach (var proof in proofs)
            {
                if (proof.Any(char.IsWhiteSpace))
                {
                    return BehaviorProofResult.NonCompliant(
                        row.Requirement + ": runtime proof entry '" + proof
                        + "' is not a single repo-relative file path.");
                }

                var pathResult = ValidateAllowedPath(proof, repositoryRoot, row.Requirement + " runtime proof");
                if (!pathResult.IsCompliant)
                {
                    return pathResult;
                }
            }

            return BehaviorProofResult.Compliant(row.Requirement + " runtime proof resolves.");
        }

        private static BehaviorProofResult ValidateAllowedPath(
            string path,
            string repositoryRoot,
            string label)
        {
            var normalized = path.Replace('\\', '/');
            if (!AllowedRoots.Any(root => normalized.StartsWith(root, StringComparison.Ordinal)))
            {
                return BehaviorProofResult.NonCompliant(
                    label + ": '" + normalized + "' is outside the executable runtime-proof roots ("
                    + string.Join(", ", AllowedRoots)
                    + "); a product source file under WIP/Wip.Tholdem.ModelHarness or "
                    + "WIP/Wip.Tholdem.Agents is not runtime proof.");
            }

            var absolutePath = Path.Combine(
                repositoryRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
            {
                return BehaviorProofResult.NonCompliant(
                    label + ": '" + normalized + "' does not exist at " + absolutePath + ".");
            }

            if (new FileInfo(absolutePath).Length == 0)
            {
                return BehaviorProofResult.NonCompliant(
                    label + ": '" + normalized + "' exists but is empty.");
            }

            return BehaviorProofResult.Compliant(label + " resolves.");
        }
    }
}
