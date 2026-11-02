using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Wip.Architecture.Tests;

internal static class ProjectReferenceGraph
{
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> Load(string repositoryRoot)
    {
        var wipRoot = Path.Combine(repositoryRoot, "WIP");
        var separator = Path.DirectorySeparatorChar;
        var projects = Directory.EnumerateFiles(wipRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains(separator + "bin" + separator, StringComparison.OrdinalIgnoreCase)
                && !path.Contains(separator + "obj" + separator, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
        foreach (var projectPath in projects)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var references = new HashSet<string>(StringComparer.Ordinal);
            var document = XDocument.Load(projectPath);
            foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
            {
                var include = element.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, include.Replace('\\', '/')));
                if (!resolved.StartsWith(wipRoot + separator, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                references.Add(Path.GetFileNameWithoutExtension(resolved));
            }

            result[projectName] = references.ToArray();
        }

        return result;
    }
}
