using Modus.Host.Plugins.Compliance;
using Xunit;

namespace Modus.Host.IntegrationTests;

[Trait("MigrationRegression", "true")]
public sealed class PluginAuthoringStandardsInventoryTests
{
    [Fact]
    public void DiscoverLegacyPluginProjects_GivenWorkspaceScan_ExpectedAllProjectsMeetBaselineMigrationRules()
    {
        var repositoryRoot = FindRepositoryRoot();
        var inventoryService = new PluginAuthoringStandardsInventoryService();

        var inventory = inventoryService.DiscoverNonCompliantProjects(repositoryRoot);

        Assert.Equal(10, inventory.TotalProjectsScanned);

        Assert.DoesNotContain(inventory.Violations, violation => string.Equals(violation.Rule, "target-framework-net10", StringComparison.Ordinal));
        Assert.DoesNotContain(inventory.Violations, violation => string.Equals(violation.Rule, "nullable-enabled", StringComparison.Ordinal));
        Assert.DoesNotContain(inventory.Violations, violation => string.Equals(violation.Rule, "implicit-usings-enabled", StringComparison.Ordinal));
        Assert.DoesNotContain(inventory.Violations, violation => string.Equals(violation.Rule, "valid-modus-core-reference", StringComparison.Ordinal));
        Assert.DoesNotContain(inventory.Violations, violation => string.Equals(violation.Rule, "one-plugin-per-assembly-compile-isolation", StringComparison.Ordinal));
    }

    [Fact]
    public void DiscoverLegacyPluginProjects_GivenKnownBaselineRules_ExpectedNoProjectMetadataLoadFailures()
    {
        var repositoryRoot = FindRepositoryRoot();
        var inventoryService = new PluginAuthoringStandardsInventoryService();

        var inventory = inventoryService.DiscoverNonCompliantProjects(repositoryRoot);

        var metadataLoadFailures = inventory.Violations
            .Where(static violation => string.Equals(violation.Category, "project-metadata", StringComparison.Ordinal))
            .Where(static violation => string.Equals(violation.RuntimeRisk, "project-load-failure", StringComparison.Ordinal)
                || string.Equals(violation.RuntimeRisk, "runtime-load-failure", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(metadataLoadFailures);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Modus.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}