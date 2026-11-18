using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Wip.Architecture.Tests;

/// <summary>
/// Reads the projects the solution actually declares, so the architecture gate can assert that the
/// on-disk project set and the solution agree instead of relying on a literal project count.
/// </summary>
internal static class SolutionProjects
{
    public static IReadOnlyList<string> LoadDeclaredWipProjectNames(string repositoryRoot)
    {
        var solutionPath = Path.Combine(repositoryRoot, "Wip.slnx");
        if (!File.Exists(solutionPath))
        {
            throw new InvalidOperationException($"Solution file not found: {solutionPath}");
        }

        var document = XDocument.Load(solutionPath);
        var names = new List<string>();
        foreach (var element in document.Descendants().Where(static element => element.Name.LocalName == "Project"))
        {
            var include = element.Attribute("Path")?.Value;
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            var normalized = include.Replace('\\', '/');
            if (!normalized.StartsWith("WIP/", StringComparison.Ordinal))
            {
                continue;
            }

            names.Add(Path.GetFileNameWithoutExtension(normalized));
        }

        return names
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
    }
}
