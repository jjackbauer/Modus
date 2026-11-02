using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Wip.Architecture.Tests;

internal sealed record ArchitectureWaiver(string RuleId, string Source, string Target, string Owner, string Expiry, string Adr)
{
    public bool Matches(ArchitectureViolation violation) =>
        string.Equals(RuleId, violation.RuleId, StringComparison.Ordinal)
        && string.Equals(Source, violation.Source, StringComparison.Ordinal)
        && string.Equals(Target, violation.Target, StringComparison.Ordinal);
}

internal static class ArchitectureWaivers
{
    public static IReadOnlyList<ArchitectureWaiver> Load(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "tests", "Wip.Architecture.Tests", "architecture-waivers.json");
        var items = JsonSerializer.Deserialize<List<WaiverDto>>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var waivers = new List<ArchitectureWaiver>(items.Count);
        foreach (var item in items)
        {
            waivers.Add(new ArchitectureWaiver(item.Rule, item.Source, item.Target, item.Owner, item.Expiry, item.Adr));
        }

        return waivers;
    }

    private sealed record WaiverDto(string Rule, string Source, string Target, string Owner, string Expiry, string Adr);
}
