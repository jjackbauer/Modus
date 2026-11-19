using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

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
/// <summary>Read-only inventory entry for one registered template version, including approval state.</summary>
public sealed record TemplateListItem(
    TemplateManifest Manifest,
    bool Approved,
    bool Revoked,
    string? ApprovalMarkerJson,
    string? Error);

public sealed record TemplateDecision(
    bool Approved,
    string? Reason,
    string? TemplateId,
    int TemplateVersion,
    string? ManifestDigest,
    string? ParametersDigest,
    string? ContractDigest,
    string ApprovalKind);

/// <summary>Raised when re-registering a template version would change its immutable body.</summary>
public sealed class TemplateVersionConflictException : InvalidOperationException
{
    public TemplateVersionConflictException(string templateId, int templateVersion)
        : base($"template-version-conflict:{templateId}@{templateVersion}")
    {
        TemplateId = templateId;
        TemplateVersion = templateVersion;
    }

    public string TemplateId { get; }

    public int TemplateVersion { get; }
}

/// <summary>
/// Operators and administrators only: register, approve, revoke and verify templates (v0.3 §4.3).
///
/// v0.4 FAC-P0-03 makes (templateId, version) immutable and binds approval to the digest of the <b>stored body</b>.
/// A marker never grants approval on its own: recovery re-reads the body and compares the recorded digest, revoked
/// versions stay revoked across restart, and the template store refuses identities that would escape its root.
/// </summary>
public sealed class FactoryTemplateRegistry
{
    public const string UnknownTemplate = "template-unknown";
    public const string VersionNotApproved = "template-version-not-approved";
    public const string Revoked = "template-revoked";
    public const string DigestMismatch = "template-digest-mismatch";
    public const string VersionConflict = "template-version-conflict";
    public const string InvalidIdentity = "template-identity-invalid";
    public const string BodyCorrupt = "template-body-corrupt";

    private static readonly Regex IdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly string _controlRoot;
    private readonly string _root;
    private readonly Dictionary<string, (TemplateManifest Manifest, bool Approved)> _templates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);

    public FactoryTemplateRegistry(string controlRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlRoot);
        _controlRoot = controlRoot;
        _root = Path.GetFullPath(Path.Combine(controlRoot, "templates"));
        Directory.CreateDirectory(_root);
        Reload();
    }

    public static string ComputeDigest(JsonNode node)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString()))).ToLowerInvariant();

    /// <summary>
    /// Registers a new manifest. Registering the exact same canonical body is idempotent; registering changed content
    /// for an existing (id, version) refuses and leaves the original body and approval untouched.
    /// </summary>
    public TemplateManifest Register(JsonNode manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var record = ParseManifest(manifest);

        using var lease = FactoryMutationLock.Acquire(_controlRoot);
        Reload();

        var key = Key(record.TemplateId, record.TemplateVersion);
        if (_templates.TryGetValue(key, out var existing))
        {
            if (string.Equals(existing.Manifest.ManifestDigest, record.ManifestDigest, StringComparison.Ordinal))
            {
                return existing.Manifest;
            }

            throw new TemplateVersionConflictException(record.TemplateId, record.TemplateVersion);
        }

        var directory = DirectoryFor(record.TemplateId);
        Directory.CreateDirectory(directory);
        var bodyPath = BodyPath(record.TemplateId, record.TemplateVersion);

        if (File.Exists(bodyPath))
        {
            // A body exists even though it was not in the loaded view (for example, a corrupt or unparseable file):
            // never overwrite it. An identical digest is still idempotent.
            var storedDigest = TryDigestOfFile(bodyPath);
            if (string.Equals(storedDigest, record.ManifestDigest, StringComparison.Ordinal))
            {
                _templates[key] = (record, Approved: IsApprovedByMarker(record));
                _errors.Remove(key);
                return record;
            }

            throw new TemplateVersionConflictException(record.TemplateId, record.TemplateVersion);
        }

        WriteNewFile(bodyPath, manifest.ToJsonString());
        _templates[key] = (record, Approved: false);
        _errors.Remove(key);
        return record;
    }

    /// <summary>Operator action. Requires the exact stored-body digest, so approval cannot be attached to a different body.</summary>
    public bool Approve(string templateId, int templateVersion, string manifestDigest)
    {
        if (!TryKey(templateId, templateVersion, out var key))
        {
            return false;
        }

        using var lease = FactoryMutationLock.Acquire(_controlRoot);
        Reload();

        if (!_templates.TryGetValue(key, out var entry))
        {
            return false;
        }

        if (!string.Equals(entry.Manifest.ManifestDigest, manifestDigest, StringComparison.Ordinal))
        {
            return false;
        }

        // A revoked version cannot be revived by an approval, and the on-disk body must still be the approved bytes.
        if (File.Exists(RevokedPath(templateId, templateVersion)))
        {
            return false;
        }

        var bodyPath = BodyPath(templateId, templateVersion);
        if (!File.Exists(bodyPath) || !string.Equals(TryDigestOfFile(bodyPath), manifestDigest, StringComparison.Ordinal))
        {
            return false;
        }

        var marker = new JsonObject
        {
            ["templateId"] = templateId,
            ["templateVersion"] = templateVersion,
            ["manifestDigest"] = manifestDigest,
            ["approver"] = Environment.UserName,
            ["approvedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["operationId"] = "template-approve-" + Guid.NewGuid().ToString("N")
        };

        WriteReplace(MarkerPath(templateId, templateVersion), marker.ToJsonString());
        _templates[key] = (entry.Manifest, Approved: true);
        _errors.Remove(key);
        return true;
    }

    /// <summary>Operator action. Revocation is durable and survives restart; there is no stale-approval fallback.</summary>
    public void Revoke(string templateId, int templateVersion)
    {
        if (!TryKey(templateId, templateVersion, out var key))
        {
            return;
        }

        using var lease = FactoryMutationLock.Acquire(_controlRoot);
        Reload();

        var marker = new JsonObject
        {
            ["templateId"] = templateId,
            ["templateVersion"] = templateVersion,
            ["revokedBy"] = Environment.UserName,
            ["revokedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["reason"] = "operator-revocation"
        };

        WriteReplace(RevokedPath(templateId, templateVersion), marker.ToJsonString());

        var approved = MarkerPath(templateId, templateVersion);
        if (File.Exists(approved))
        {
            File.Delete(approved);
        }

        if (_templates.TryGetValue(key, out var entry))
        {
            _templates[key] = (entry.Manifest, Approved: false);
        }

        _errors[key] = Revoked + ":" + templateId + "@" + templateVersion;
    }

    /// <summary>
    /// Read-only inventory of every registered version, including unapproved and revoked bodies. The adapter needs
    /// to show an operator what exists before it asks them to approve a digest.
    /// </summary>
    public IReadOnlyList<TemplateListItem> List()
    {
        using var lease = FactoryMutationLock.Acquire(_controlRoot);
        Reload();

        return _templates
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .Select(entry =>
            {
                var (id, version) = entry.Value.Manifest.TemplateId.Length == 0
                    ? ("", 0)
                    : (entry.Value.Manifest.TemplateId, entry.Value.Manifest.TemplateVersion);

                _errors.TryGetValue(entry.Key, out var error);
                var revoked = File.Exists(RevokedPath(id, version));
                var marker = MarkerPath(id, version);
                return new TemplateListItem(
                    entry.Value.Manifest,
                    entry.Value.Approved && !revoked,
                    revoked,
                    File.Exists(marker) ? File.ReadAllText(marker) : null,
                    error);
            })
            .ToArray();
    }

    /// <summary>Looks up an approved template and re-verifies its digest against its stored body and approval marker.</summary>
    public (TemplateManifest? Manifest, string? Error) Resolve(string templateId, int templateVersion)
    {
        if (!TryKey(templateId, templateVersion, out var key))
        {
            return (null, InvalidIdentity + ":" + templateId + "@" + templateVersion);
        }

        if (_errors.TryGetValue(key, out var recordedError))
        {
            return (null, recordedError);
        }

        if (!_templates.TryGetValue(key, out var entry))
        {
            return (null, UnknownTemplate + ":" + templateId + "@" + templateVersion);
        }

        var stored = BodyPath(templateId, templateVersion);
        if (!File.Exists(stored))
        {
            return (null, DigestMismatch + ":body-missing");
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(stored));
        }
        catch (JsonException)
        {
            return (null, BodyCorrupt + ":" + templateId + "@" + templateVersion);
        }

        if (node is null || !string.Equals(ComputeDigest(node), entry.Manifest.ManifestDigest, StringComparison.Ordinal))
        {
            return (null, DigestMismatch + ":" + templateId + "@" + templateVersion);
        }

        if (File.Exists(RevokedPath(templateId, templateVersion)))
        {
            return (null, Revoked + ":" + templateId + "@" + templateVersion);
        }

        if (!entry.Approved)
        {
            return (null, Revoked + ":" + templateId + "@" + templateVersion);
        }

        if (!IsApprovedByMarker(entry.Manifest))
        {
            return (null, DigestMismatch + ":approval-marker:" + templateId + "@" + templateVersion);
        }

        return (entry.Manifest, null);
    }

    private TemplateManifest ParseManifest(JsonNode manifest)
    {
        var templateId = manifest["templateId"]?.GetValue<string>() ?? string.Empty;
        var templateVersion = manifest["templateVersion"]?.GetValue<int>() ?? 0;

        if (!IsValidId(templateId) || templateVersion < 1)
        {
            throw new InvalidOperationException(InvalidIdentity + ":" + templateId + "@" + templateVersion);
        }

        var supportedClass = manifest["supportedClass"]?.GetValue<string>() ?? string.Empty;
        if (!string.Equals(supportedClass, SupportedAppClass.AspNetCoreHttpNet10, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(InvalidIdentity + ":supportedClass:" + supportedClass);
        }

        var scenarios = manifest["scenarios"];
        if (scenarios is null)
        {
            throw new InvalidOperationException(InvalidIdentity + ":scenarios-missing");
        }

        var parameters = new Dictionary<string, TemplateParameter>(StringComparer.Ordinal);
        if (manifest["parameters"] is JsonObject parameterNode)
        {
            foreach (var (name, spec) in parameterNode)
            {
                var allowed = (spec as JsonObject)?["allowed"]?.AsArray()
                    .Select(static value => value?.GetValue<string>() ?? string.Empty)
                    .ToArray();

                parameters[name] = new TemplateParameter(
                    (spec as JsonObject)?["type"]?.GetValue<string>() ?? "string",
                    allowed);
            }
        }

        return new TemplateManifest(
            templateId,
            templateVersion,
            manifest["schemaVersion"]?.GetValue<int>() ?? 1,
            supportedClass,
            parameters,
            scenarios.DeepClone(),
            ComputeDigest(manifest));
    }

    /// <summary>Reads a manifest from its stored body, including the parameter schema.</summary>
    private static TemplateManifest FromNode(JsonNode node, string templateId, int templateVersion)
    {
        var parameters = new Dictionary<string, TemplateParameter>(StringComparer.Ordinal);
        if (node["parameters"] is JsonObject parameterNode)
        {
            foreach (var (name, spec) in parameterNode)
            {
                var allowed = (spec as JsonObject)?["allowed"]?.AsArray()
                    .Select(static value => value?.GetValue<string>() ?? string.Empty)
                    .ToArray();
                parameters[name] = new TemplateParameter(
                    (spec as JsonObject)?["type"]?.GetValue<string>() ?? "string",
                    allowed);
            }
        }

        return new TemplateManifest(
            templateId,
            templateVersion,
            node["schemaVersion"]?.GetValue<int>() ?? 1,
            node["supportedClass"]?.GetValue<string>() ?? string.Empty,
            parameters,
            node["scenarios"]!.DeepClone(),
            ComputeDigest(node));
    }

    private static bool IsValidId(string templateId)
        => !string.IsNullOrWhiteSpace(templateId)
           && IdPattern.IsMatch(templateId)
           && !templateId.Contains("..", StringComparison.Ordinal);

    private static bool TryKey(string templateId, int templateVersion, out string key)
    {
        key = string.Empty;
        if (!IsValidId(templateId) || templateVersion < 1)
        {
            return false;
        }

        key = Key(templateId, templateVersion);
        return true;
    }

    private static string Key(string id, int version) => id + "@" + version;

    private string DirectoryFor(string templateId)
    {
        var directory = Path.GetFullPath(Path.Combine(_root, templateId));
        var prefix = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(InvalidIdentity + ":escape:" + templateId);
        }

        return directory;
    }

    private string BodyPath(string templateId, int templateVersion)
        => Path.Combine(DirectoryFor(templateId), templateVersion + ".json");

    private string MarkerPath(string templateId, int templateVersion)
        => Path.Combine(DirectoryFor(templateId), templateVersion + ".approved");

    private string RevokedPath(string templateId, int templateVersion)
        => Path.Combine(DirectoryFor(templateId), templateVersion + ".revoked");

    private bool IsApprovedByMarker(TemplateManifest manifest)
    {
        var marker = MarkerPath(manifest.TemplateId, manifest.TemplateVersion);
        if (!File.Exists(marker))
        {
            return false;
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(marker));
            var recorded = node?["manifestDigest"]?.GetValue<string>();
            return string.Equals(recorded, manifest.ManifestDigest, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    private string? TryDigestOfFile(string path)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path));
            return node is null ? null : ComputeDigest(node);
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void WriteNewFile(string path, string content)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            WriteDurably(temporary, content);
            File.Move(temporary, path);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static void WriteReplace(string path, string content)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            WriteDurably(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static void WriteDurably(string path, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private void Reload()
    {
        _templates.Clear();
        _errors.Clear();
        LoadCore();
    }

    private void LoadCore()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (var idDirectory in Directory.EnumerateDirectories(_root))
        {
            var id = Path.GetFileName(idDirectory);
            if (!IsValidId(id))
            {
                // An unexpected directory is not a template. Never follow it.
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(idDirectory, "*.json"))
            {
                var versionText = Path.GetFileNameWithoutExtension(file);
                if (!int.TryParse(versionText, System.Globalization.CultureInfo.InvariantCulture, out var version)
                    || version < 1)
                {
                    continue;
                }

                var key = Key(id, version);
                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(File.ReadAllText(file));
                }
                catch (Exception exception) when (exception is JsonException or IOException)
                {
                    node = null;
                }

                if (node is null)
                {
                    _errors[key] = BodyCorrupt + ":" + id + "@" + version;
                    continue;
                }

                TemplateManifest manifest;
                try
                {
                    manifest = FromNode(node, id, version);
                }
                catch (Exception exception) when (exception is InvalidOperationException or NullReferenceException)
                {
                    _errors[key] = BodyCorrupt + ":" + id + "@" + version;
                    continue;
                }

                var approved = false;
                var marker = MarkerPath(id, version);
                if (File.Exists(marker))
                {
                    try
                    {
                        var markerNode = JsonNode.Parse(File.ReadAllText(marker));
                        var recorded = markerNode?["manifestDigest"]?.GetValue<string>();
                        if (string.Equals(recorded, manifest.ManifestDigest, StringComparison.Ordinal))
                        {
                            approved = true;
                        }
                        else
                        {
                            // The marker belongs to a different body: never inherit the old approval.
                            _errors[key] = DigestMismatch + ":approval-marker:" + id + "@" + version;
                        }
                    }
                    catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
                    {
                        _errors[key] = DigestMismatch + ":approval-marker-unreadable:" + id + "@" + version;
                    }
                }

                if (File.Exists(RevokedPath(id, version)))
                {
                    approved = false;
                    _errors[key] = Revoked + ":" + id + "@" + version;
                }

                _templates[key] = (manifest, approved);
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
