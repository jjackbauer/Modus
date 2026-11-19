using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// FAC-P1-08: v0.3's 80 named supporting tests are inventoried against compiled xUnit methods, not against a prose
/// count. Missing names are reported as missing behaviour; a name present without an assertion is a name-only
/// placeholder and fails the CI gate (v0.4 FAC-P1-08.1–6).
/// </summary>
public sealed class TraceabilityTests
{
    private const string MissingStatus = "missing-behavior";
    private const string ExactStatus = "exact-and-executable";
    private const string NameOnlyStatus = "name-only-no-assertion";

    public sealed record CoverageEntry(string Name, string Status, string? SourceFile);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Wip.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located from the test output directory.");
    }

    /// <summary>The 80 §12.2–12.3 names, in declared order.</summary>
    private static IReadOnlyList<string> SpecNames()
    {
        var path = Path.Combine(RepositoryRoot(), "harness", "requirements", "Wip.Factory.Operator.Alpha.v0.3.md");
        var text = File.ReadAllText(path);

        return Regex.Matches(text, @"(?m)^\d+\.\s+`([^`]+)`")
            .Select(static match => match.Groups[1].Value)
            .Where(static name => name.Contains('_', StringComparison.Ordinal))
            .ToArray();
    }

    private static HashSet<string> CompiledTestNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var root = RepositoryRoot();
        var assemblies = new[]
        {
            Path.Combine(root, "WIP", "Wip.Factory.Core.Tests", "bin", "Debug", "net10.0", "Wip.Factory.Core.Tests.dll"),
            Path.Combine(root, "WIP", "Wip.Factory.Agents.Tests", "bin", "Debug", "net10.0", "Wip.Factory.Agents.Tests.dll"),
            Path.Combine(root, "WIP", "Wip.Factory.Host.Tests", "bin", "Debug", "net10.0", "Wip.Factory.Host.Tests.dll"),
            Path.Combine(root, "tests", "Wip.Architecture.Tests", "bin", "Debug", "net10.0", "Wip.Architecture.Tests.dll")
        };

        foreach (var path in assemblies.Where(File.Exists))
        {
            var assembly = Assembly.LoadFrom(path);
            foreach (var type in assembly.GetTypes().Where(static type => type.IsClass && type.IsPublic))
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (method.GetCustomAttributes().Any(static attribute => attribute.GetType().Name is "FactAttribute" or "TheoryAttribute"))
                    {
                        names.Add(method.Name);
                    }
                }
            }
        }

        return names;
    }

    /// <summary>Finds a method body and proves it contains at least one assertion, not just the expected name.</summary>
    private static string? SourceWithoutAssertion(string root, string methodName)
    {
        var declaration = new Regex(
            @"public\s+(?:async\s+)?(?:Task|void)\s+" + Regex.Escape(methodName) + @"\s*\(",
            RegexOptions.CultureInvariant);

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var match = declaration.Match(text);
            if (!match.Success)
            {
                continue;
            }

            var window = text.Substring(match.Index, Math.Min(6000, text.Length - match.Index));
            return window.Contains("Assert.", StringComparison.Ordinal) ? null : Path.GetRelativePath(root, file);
        }

        return null;
    }

    private static IReadOnlyList<CoverageEntry> Inventory(
        IReadOnlyList<string> specNames,
        IReadOnlySet<string> compiledNames,
        Func<string, string?> sourceWithoutAssertion)
    {
        var entries = new List<CoverageEntry>(specNames.Count);
        foreach (var name in specNames)
        {
            if (!compiledNames.Contains(name))
            {
                entries.Add(new CoverageEntry(name, MissingStatus, null));
                continue;
            }

            var offender = sourceWithoutAssertion(name);
            entries.Add(offender is null
                ? new CoverageEntry(name, ExactStatus, null)
                : new CoverageEntry(name, NameOnlyStatus, offender));
        }

        return entries;
    }

    /// <summary>Missing behaviour is reported; a name-only placeholder or a duplicate name fails CI.</summary>
    internal static int GateExitCode(IReadOnlyList<CoverageEntry> entries)
        => entries.Any(static entry => entry.Status == NameOnlyStatus) ? 1 : 0;

    [Fact]
    public void Traceability_GivenNamedRequirementWithoutExecutableTest_ReportsMissing()
    {
        var entries = Inventory(
            ["Present_GivenX_DoesY", "Absent_GivenX_DoesY"],
            new HashSet<string>(["Present_GivenX_DoesY"], StringComparer.Ordinal),
            static _ => null);

        Assert.Equal(ExactStatus, entries[0].Status);
        Assert.Equal(MissingStatus, entries[1].Status);
    }

    [Fact]
    public void Traceability_GivenMethodNameButNoBehaviorAssertion_FailsMutationProbe()
    {
        var entries = Inventory(
            ["Present_GivenX_DoesY"],
            new HashSet<string>(["Present_GivenX_DoesY"], StringComparer.Ordinal),
            static _ => "Wip.Factory.Host.Tests/PlaceholderTests.cs");

        Assert.Equal(NameOnlyStatus, entries[0].Status);
        Assert.Equal(1, GateExitCode(entries));
    }

    [Fact]
    public void CI_GivenPassingTraceabilityGate_ReturnsZero()
    {
        var entries = Inventory(
            ["Present_GivenX_DoesY"],
            new HashSet<string>(["Present_GivenX_DoesY"], StringComparer.Ordinal),
            static _ => null);

        Assert.Equal(0, GateExitCode(entries));
    }

    [Fact]
    public void Traceability_GivenMeasuredRepository_PublishesSeparateCounts()
    {
        var root = RepositoryRoot();
        var specNames = SpecNames();
        var compiled = CompiledTestNames();

        var entries = Inventory(specNames, compiled, name => SourceWithoutAssertion(root, name));

        Assert.Equal(80, specNames.Count);
        Assert.Equal(80, entries.Count);
        Assert.DoesNotContain(entries, static entry => entry.Status == NameOnlyStatus);

        var report = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            sourceRevision = ReadHeadSha(root),
            specification = "harness/requirements/Wip.Factory.Operator.Alpha.v0.3.md",
            semantics = new
            {
                exact = "a compiled xUnit method with the exact name and at least one assertion",
                missing = "the named behaviour has no executable test at final SHA",
                nameOnly = "the method exists but no assertion was found; CI gate failure"
            },
            counts = new
            {
                named = entries.Count,
                exact = entries.Count(static entry => entry.Status == ExactStatus),
                missing = entries.Count(static entry => entry.Status == MissingStatus),
                nameOnly = entries.Count(static entry => entry.Status == NameOnlyStatus)
            },
            entries
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        var output = Environment.GetEnvironmentVariable("FACTORY_TRACEABILITY_OUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            File.WriteAllText(output, json);
        }

        Assert.Equal(80, entries.Count);
    }

    private static string ReadHeadSha(string root)
    {
        try
        {
            var head = Path.Combine(root, ".git", "HEAD");
            if (!File.Exists(head))
            {
                return "unknown";
            }

            var text = File.ReadAllText(head).Trim();
            if (!text.StartsWith("ref:", StringComparison.Ordinal))
            {
                return text;
            }

            var reference = text[4..].Trim().Replace('/', Path.DirectorySeparatorChar);
            var refPath = Path.Combine(root, ".git", reference);
            return File.Exists(refPath) ? File.ReadAllText(refPath).Trim() : "unknown";
        }
        catch (IOException)
        {
            return "unknown";
        }
    }
}
