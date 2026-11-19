using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Wip.Factory.Host.Factory;

/// <summary>
/// Durable factory control root (D-06, v0.3 §9). It must never resolve inside the target repository or a
/// worktree, because factory state inside the operator's tree would be an invitation to commit it.
/// </summary>
public sealed record FactoryControlRoot(string Root, string TargetRepository)
{
    public const string InsideTargetCategory = "control-root-inside-target";

    public string LogPath => Path.Combine(Root, "factory.log");

    public string WorkItemsPath => Path.Combine(Root, "work-items");

    public string ContractsPath => Path.Combine(Root, "contracts");

    public string EvidencePath => Path.Combine(Root, "evidence");

    public string WorktreesPath => Path.Combine(Root, "worktrees");

    public string ReleasesPath => Path.Combine(Root, "releases");

    public string LocksPath => Path.Combine(Root, "locks");

    public static string DefaultFor(string targetRepository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRepository);

        var full = Path.GetFullPath(targetRepository).TrimEnd(Path.DirectorySeparatorChar);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..12].ToLowerInvariant();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "wip-factory", id);
    }

    /// <summary>Resolves and validates the control root, or returns the refusal reason.</summary>
    public static (FactoryControlRoot? Root, string? Error) Resolve(
        string targetRepository,
        string? explicitRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRepository);

        var target = Path.GetFullPath(targetRepository).TrimEnd(Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(explicitRoot ?? DefaultFor(target)).TrimEnd(Path.DirectorySeparatorChar);

        if (IsInside(root, target) || IsInside(target, root))
        {
            return (null, $"{InsideTargetCategory}: control root '{root}' overlaps target repository '{target}'.");
        }

        return (new FactoryControlRoot(root, target), null);
    }

    /// <summary>True when <paramref name="candidate"/> is the same path as, or nested under, <paramref name="parent"/>.</summary>
    public static bool IsInside(string candidate, string parent)
    {
        var separator = Path.DirectorySeparatorChar;
        var a = Path.GetFullPath(candidate).TrimEnd(separator);
        var b = Path.GetFullPath(parent).TrimEnd(separator);

        return a.Equals(b, StringComparison.OrdinalIgnoreCase)
               || a.StartsWith(b + separator, StringComparison.OrdinalIgnoreCase);
    }

    public void EnsureCreated()
    {
        foreach (var path in new[] { Root, WorkItemsPath, ContractsPath, EvidencePath, WorktreesPath, ReleasesPath, LocksPath })
        {
            Directory.CreateDirectory(path);
        }
    }
}

/// <summary>The supported application class (v0.3 §0.2). Anything else is refused at intake.</summary>
public static class SupportedAppClass
{
    public const string AspNetCoreHttpNet10 = "aspnetcore-http-net10";
}

/// <summary>A parsed requirement revision: immutable text plus the observable expectations it states.</summary>
public sealed record RequirementDocument(
    string Title,
    string SupportedClassId,
    string Body,
    IReadOnlyList<string> Expectations,
    string Digest,
    string RawText,
    string? AcceptanceContractReference = null,
    string? TemplateReference = null,
    string? TemplateParametersJson = null)
{
    public const string MissingTitleCategory = "requirement-missing-title";
    public const string UnsupportedClassCategory = "unsupported-class";
    public const string NoExpectationsCategory = "requirement-without-acceptance-expectations";
    public const string UnknownMetadataCategory = "requirement-unknown-metadata";

    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "supportedClass", "acceptanceContract", "template", "parameters"
    };

    public static (RequirementDocument? Document, IReadOnlyList<string> Errors) Parse(string text, string? titleOverride = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var errors = new List<string>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bodyStart = 0;

        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            var end = Array.FindIndex(lines, 1, static line => line.Trim() == "---");
            if (end < 0)
            {
                errors.Add("requirement-unterminated-front-matter");
            }
            else
            {
                for (var i = 1; i < end; i++)
                {
                    var line = lines[i];
                    var separator = line.IndexOf(':', StringComparison.Ordinal);
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var key = line[..separator].Trim();
                    var value = line[(separator + 1)..].Trim();

                    if (!KnownKeys.Contains(key))
                    {
                        errors.Add($"{UnknownMetadataCategory}:{key}");
                        continue;
                    }

                    metadata[key] = value;
                }

                bodyStart = end + 1;
            }
        }

        var body = string.Join('\n', lines[bodyStart..]).Trim();
        var title = titleOverride ?? (metadata.TryGetValue("title", out var t) ? t : null);

        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add(MissingTitleCategory);
        }

        var supportedClass = metadata.TryGetValue("supportedClass", out var sc) ? sc : string.Empty;
        if (!string.Equals(supportedClass, SupportedAppClass.AspNetCoreHttpNet10, StringComparison.Ordinal))
        {
            errors.Add($"{UnsupportedClassCategory}:{supportedClass}");
        }

        // Expectations are the bullet lines the requirement states as observable behaviour.
        var expectations = body
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            .Select(static line => line[2..].Trim())
            .Where(static line => line.Length > 0)
            .ToArray();

        if (expectations.Length == 0)
        {
            errors.Add(NoExpectationsCategory);
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var digest = ComputeDigest(title!, supportedClass, body, expectations);
        var contractRef = metadata.TryGetValue("acceptanceContract", out var reference) ? reference : null;
        var templateRef = metadata.TryGetValue("template", out var template) ? template : null;
        var templateParameters = metadata.TryGetValue("parameters", out var parameters) ? parameters : null;
        return (new RequirementDocument(title!, supportedClass, body, expectations, digest, text, contractRef, templateRef, templateParameters), []);
    }

    public static string ComputeDigest(
        string title,
        string supportedClassId,
        string body,
        IReadOnlyList<string> expectations)
    {
        var canonical = string.Join(
            '\n',
            "title:" + title.Trim(),
            "class:" + supportedClassId.Trim(),
            "body:" + body.Trim(),
            "expectations:" + string.Join('|', expectations.Select(static e => e.Trim())));

        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>
/// The structured contract proposal derived at intake. In this milestone the scenarios carry the stated
/// expectations verbatim; the executable HTTP form and its runner arrive with the acceptance milestone.
/// Approval freezes this exact digest, so a later executable form must extend the proposal visibly rather
/// than quietly reinterpret it.
/// </summary>
public sealed record ContractProposal(
    int SchemaVersion,
    string SupportedClassId,
    IReadOnlyList<ContractScenario> Scenarios,
    string RequirementDigest,
    string ProposalDigest,
    string? ExecutableContractDigest = null,
    IReadOnlyList<string>? UnresolvedQuestions = null)
{
    public bool Executable => ExecutableContractDigest is not null;

    /// <summary>True when an expectation cannot be checked as an observable HTTP outcome.</summary>
    public bool HasUnresolvedQuestions => UnresolvedQuestions is { Count: > 0 };

    private static readonly Regex ObservableCue = new(
        @"(?i)\b(returns|status|responds|GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\b|\b[1-5]\d{2}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ContractProposal From(RequirementDocument requirement, string? executableContractDigest = null)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        var scenarios = requirement.Expectations
            .Select((expectation, index) => new ContractScenario(
                $"AC-{index + 1}",
                "pending-http-form",
                "/",
                expectation))
            .ToArray();

        // A prose expectation with no observable outcome must be resolved before freeze rather than being
        // silently converted into a scenario nobody can execute.
        var unresolved = requirement.Expectations
            .Select((expectation, index) => (Expectation: expectation, Id: "AC-" + (index + 1)))
            .Where(static pair => !ObservableCue.IsMatch(pair.Expectation))
            .Select(static pair => pair.Id + ":" + pair.Expectation)
            .ToArray();

        var canonical = string.Join(
            '\n',
            "schema:" + 1,
            "class:" + requirement.SupportedClassId,
            "requirement:" + requirement.Digest,
            string.Join('|', scenarios.Select(static s => $"{s.Id}:{s.Expectation}")));

        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

        return new ContractProposal(
            1,
            requirement.SupportedClassId,
            scenarios,
            requirement.Digest,
            digest,
            executableContractDigest,
            unresolved);
    }

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static ContractProposal? FromJson(string json) => JsonSerializer.Deserialize<ContractProposal>(json, SerializerOptions);

    public static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
}

/// <summary>One acceptance scenario, stable by id.</summary>
public sealed record ContractScenario(string Id, string Kind, string Path, string Expectation);
