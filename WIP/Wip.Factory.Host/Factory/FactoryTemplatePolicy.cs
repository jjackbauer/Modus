using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Wip.Factory.Host.Factory;

/// <summary>An immutable, operator-approved contract template (v0.3 §4.3).</summary>
public sealed record TemplateManifest(
    string TemplateId,
    int TemplateVersion,
    int SchemaVersion,
    string SupportedClass,
    IReadOnlyDictionary<string, TemplateParameter> Parameters,
    JsonNode Scenarios,
    string ManifestDigest);

/// <summary>A parameter the template allows, with the values it may take.</summary>
public sealed record TemplateParameter(string Type, IReadOnlyList<string>? Allowed);

/// <summary>Outcome of a policy evaluation. Only a full match may auto-approve a contract.</summary>
public sealed record TemplateDecision(
    bool Approved,
    string? Reason,
    string? TemplateId,
    int TemplateVersion,
    string? ManifestDigest,
    string? ParametersDigest,
    string? ContractDigest,
    string ApprovalKind);

/// <summary>Operators and administrators only: register, approve, revoke and verify templates (v0.3 §4.3).</summary>
public sealed class FactoryTemplateRegistry
{
    public const string UnknownTemplate = "template-unknown";
    public const string VersionNotApproved = "template-version-not-approved";
    public const string Revoked = "template-revoked";
    public const string DigestMismatch = "template-digest-mismatch";

    private readonly string _root;
    private readonly Dictionary<string, (TemplateManifest Manifest, bool Approved)> _templates = new(StringComparer.Ordinal);

    public FactoryTemplateRegistry(string controlRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlRoot);
        _root = Path.Combine(controlRoot, "templates");
        Directory.CreateDirectory(_root);
        Load();
    }

    public static string ComputeDigest(JsonNode node)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString()))).ToLowerInvariant();

    /// <summary>Registers a manifest. It is not usable for approval until an operator approves it.</summary>
    public TemplateManifest Register(JsonNode manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var parameterNode = manifest["parameters"] as JsonObject;
        var parameters = new Dictionary<string, TemplateParameter>(StringComparer.Ordinal);
        if (parameterNode is not null)
        {
            foreach (var (name, spec) in parameterNode)
            {
                var allowed = (spec as JsonObject)?["allowed"]?.AsArray()
                    .Select(static v => v?.GetValue<string>() ?? string.Empty)
                    .ToArray();
                parameters[name] = new TemplateParameter(
                    (spec as JsonObject)?["type"]?.GetValue<string>() ?? "string",
                    allowed);
            }
        }

        var digest = ComputeDigest(manifest);
        var record = new TemplateManifest(
            manifest["templateId"]!.GetValue<string>(),
            manifest["templateVersion"]!.GetValue<int>(),
            manifest["schemaVersion"]?.GetValue<int>() ?? 1,
            manifest["supportedClass"]!.GetValue<string>(),
            parameters,
            manifest["scenarios"]!.DeepClone(),
            digest);

        var directory = Path.Combine(_root, record.TemplateId);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, record.TemplateVersion + ".json"), manifest.ToJsonString());

        _templates[Key(record.TemplateId, record.TemplateVersion)] = (record, Approved: false);
        return record;
    }

    /// <summary>Operator action. Requires the exact manifest digest, so approval cannot be attached to a different body.</summary>
    public bool Approve(string templateId, int templateVersion, string manifestDigest)
    {
        if (!_templates.TryGetValue(Key(templateId, templateVersion), out var entry))
        {
            return false;
        }

        if (!string.Equals(entry.Manifest.ManifestDigest, manifestDigest, StringComparison.Ordinal))
        {
            return false;
        }

        _templates[Key(templateId, templateVersion)] = (entry.Manifest, Approved: true);
        File.WriteAllText(
            Path.Combine(_root, templateId, templateVersion + ".approved"),
            manifestDigest + Environment.NewLine + Environment.UserName + Environment.NewLine + DateTimeOffset.UtcNow.ToString("O"));
        return true;
    }

    public void Revoke(string templateId, int templateVersion)
    {
        var key = Key(templateId, templateVersion);
        if (_templates.TryGetValue(key, out var entry))
        {
            _templates[key] = (entry.Manifest, Approved: false);
        }

        var marker = Path.Combine(_root, templateId, templateVersion + ".approved");
        if (File.Exists(marker))
        {
            File.Delete(marker);
        }
    }

    /// <summary>Looks up an approved template and re-verifies its digest against its stored body.</summary>
    public (TemplateManifest? Manifest, string? Error) Resolve(string templateId, int templateVersion)
    {
        var key = Key(templateId, templateVersion);
        if (!_templates.TryGetValue(key, out var entry))
        {
            return (null, UnknownTemplate + ":" + templateId + "@" + templateVersion);
        }

        var stored = Path.Combine(_root, templateId, templateVersion + ".json");
        if (!File.Exists(stored))
        {
            return (null, DigestMismatch + ":body-missing");
        }

        var node = JsonNode.Parse(File.ReadAllText(stored));
        if (node is null || !string.Equals(ComputeDigest(node), entry.Manifest.ManifestDigest, StringComparison.Ordinal))
        {
            return (null, DigestMismatch + ":" + templateId + "@" + templateVersion);
        }

        if (!entry.Approved)
        {
            return (null, Revoked + ":" + templateId + "@" + templateVersion);
        }

        return (entry.Manifest, null);
    }

    private static string Key(string id, int version) => id + "@" + version;

    private void Load()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (var idDirectory in Directory.EnumerateDirectories(_root))
        {
            var id = Path.GetFileName(idDirectory);
            foreach (var file in Directory.EnumerateFiles(idDirectory, "*.json"))
            {
                var versionText = Path.GetFileNameWithoutExtension(file);
                if (!int.TryParse(versionText, out var version))
                {
                    continue;
                }

                var node = JsonNode.Parse(File.ReadAllText(file));
                if (node is null)
                {
                    continue;
                }

                var approved = File.Exists(Path.Combine(idDirectory, versionText + ".approved"));
                var digest = ComputeDigest(node);
                _templates[Key(id, version)] = (new TemplateManifest(
                    id, version, node["schemaVersion"]?.GetValue<int>() ?? 1,
                    node["supportedClass"]?.GetValue<string>() ?? string.Empty,
                    new Dictionary<string, TemplateParameter>(), node["scenarios"]!.DeepClone(), digest), approved);
            }
        }
    }
}

/// <summary>
/// Strict template approval (v0.3 §4.3, D-09). A contract is auto-approved only when it names an approved template and
/// the scenarios it proposes are exactly the ones that template instantiates with its declared parameters. Any
/// deviation falls back to human approval; nothing fuzzy, nothing model-authored.
/// </summary>
public static class FactoryTemplatePolicy
{
    public const string ApprovalKind = "template-policy";
    public const string NotATemplateProposal = "template-policy:proposal-names-no-template";
    public const string ScenarioMismatch = "template-policy:scenarios-not-permitted";
    public const string ParameterRejected = "template-policy:parameter-not-permitted";

    public static TemplateDecision Evaluate(
        FactoryTemplateRegistry registry,
        JsonNode proposal)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(proposal);

        var templateId = proposal["templateId"]?.GetValue<string>();
        var templateVersion = proposal["templateVersion"]?.GetValue<int>();
        if (templateId is null || templateVersion is null)
        {
            return new TemplateDecision(false, NotATemplateProposal, null, 0, null, null, null, ApprovalKind);
        }

        var (manifest, error) = registry.Resolve(templateId, templateVersion.Value);
        if (manifest is null)
        {
            return new TemplateDecision(false, error, templateId, templateVersion.Value, null, null, null, ApprovalKind);
        }

        var parameters = (proposal["parameters"] as JsonObject) ?? [];
        foreach (var (name, spec) in manifest.Parameters)
        {
            var value = parameters[name]?.GetValue<string>();
            if (value is null)
            {
                return new TemplateDecision(false, ParameterRejected + ":missing:" + name, templateId, templateVersion.Value,
                    manifest.ManifestDigest, null, null, ApprovalKind);
            }

            if (spec.Allowed is { Count: > 0 } && !spec.Allowed.Contains(value, StringComparer.Ordinal))
            {
                return new TemplateDecision(false, ParameterRejected + ":out-of-range:" + name, templateId, templateVersion.Value,
                    manifest.ManifestDigest, null, null, ApprovalKind);
            }
        }

        foreach (var property in parameters)
        {
            if (!manifest.Parameters.ContainsKey(property.Key))
            {
                return new TemplateDecision(false, ParameterRejected + ":unexpected:" + property.Key, templateId,
                    templateVersion.Value, manifest.ManifestDigest, null, null, ApprovalKind);
            }
        }

        var instantiated = Instantiate(manifest, parameters);
        var proposed = proposal["scenarios"]?.DeepClone();
        if (proposed is null || !JsonNode.DeepEquals(instantiated, proposed))
        {
            return new TemplateDecision(false, ScenarioMismatch, templateId, templateVersion.Value,
                manifest.ManifestDigest, null, null, ApprovalKind);
        }

        var parametersDigest = FactoryTemplateRegistry.ComputeDigest(parameters);
        var contractDigest = FactoryTemplateRegistry.ComputeDigest(instantiated);

        return new TemplateDecision(true, null, templateId, templateVersion.Value, manifest.ManifestDigest,
            parametersDigest, contractDigest, ApprovalKind);
    }

    /// <summary>Builds the only contract this template permits, with its declared parameters substituted.</summary>
    public static JsonNode Instantiate(TemplateManifest manifest, JsonObject parameters)
    {
        var text = manifest.Scenarios.ToJsonString();

        foreach (var (name, value) in parameters)
        {
            var replacement = value?.GetValue<string>() ?? string.Empty;
            text = text.Replace("${" + name + "}", replacement, StringComparison.Ordinal);
        }

        if (text.Contains("${", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("template-unsubstituted-parameter");
        }

        return JsonNode.Parse(text)
            ?? throw new InvalidOperationException("template-instantiation-failed");
    }
}
