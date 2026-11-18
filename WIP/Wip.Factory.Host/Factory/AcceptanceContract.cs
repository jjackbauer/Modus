using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wip.Factory.Host.Factory;

/// <summary>
/// A frozen HTTP acceptance contract (v0.3 §4.1). Scenarios are ordered and stable; the canonical bytes are
/// digest-pinned at intake so an agent cannot quietly reinterpret what "done" means.
/// </summary>
public sealed record AcceptanceContract(
    int SchemaVersion,
    string SupportedClass,
    IReadOnlyList<HttpScenario> Scenarios)
{
    public const string InvalidCategory = "contract-invalid";
    public const string UnsupportedFormCategory = "contract-unsupported-form";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Canonical form: the exact bytes a digest covers.</summary>
    public string ToCanonicalJson() => JsonSerializer.Serialize(this, SerializerOptions);

    [JsonIgnore]
    public string Digest => "sha256:" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(ToCanonicalJson()))).ToLowerInvariant();

    public static string DigestOf(string canonicalJson)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();

    public static (AcceptanceContract? Contract, IReadOnlyList<string> Errors) Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return (null, [$"{InvalidCategory}:json:{exception.Message}"]);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, [$"{InvalidCategory}:root-must-be-an-object"]);
            }

            var errors = new List<string>();

            if (!root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number)
            {
                errors.Add($"{InvalidCategory}:schemaVersion");
            }

            var supportedClass = root.TryGetProperty("supportedClass", out var sc) && sc.ValueKind == JsonValueKind.String
                ? sc.GetString() ?? string.Empty
                : string.Empty;

            if (!string.Equals(supportedClass, SupportedAppClass.AspNetCoreHttpNet10, StringComparison.Ordinal))
            {
                errors.Add($"{InvalidCategory}:supportedClass");
            }

            var scenarios = new List<HttpScenario>();
            if (!root.TryGetProperty("scenarios", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
            {
                errors.Add($"{InvalidCategory}:scenarios");
            }
            else
            {
                foreach (var element in array.EnumerateArray())
                {
                    var scenario = ParseScenario(element, errors);
                    if (scenario is not null)
                    {
                        scenarios.Add(scenario);
                    }
                }
            }

            if (scenarios.Select(static s => s.Id).Distinct(StringComparer.Ordinal).Count() != scenarios.Count)
            {
                errors.Add($"{InvalidCategory}:duplicate-scenario-id");
            }

            if (errors.Count > 0)
            {
                return (null, errors);
            }

            return (new AcceptanceContract(schema.GetInt32(), supportedClass, scenarios), []);
        }
    }

    private static HttpScenario? ParseScenario(JsonElement element, List<string> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{InvalidCategory}:scenario-not-an-object");
            return null;
        }

        var id = element.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String
            ? idValue.GetString() ?? string.Empty
            : string.Empty;

        var method = element.TryGetProperty("method", out var methodValue) && methodValue.ValueKind == JsonValueKind.String
            ? methodValue.GetString()?.ToUpperInvariant() ?? string.Empty
            : string.Empty;

        var path = element.TryGetProperty("path", out var pathValue) && pathValue.ValueKind == JsonValueKind.String
            ? pathValue.GetString() ?? string.Empty
            : string.Empty;

        if (id.Length == 0 || method.Length == 0 || path.Length == 0)
        {
            errors.Add($"{InvalidCategory}:scenario-missing-id-method-or-path");
            return null;
        }

        if (!path.StartsWith('/'))
        {
            errors.Add($"{UnsupportedFormCategory}:path-must-be-relative:{id}");
            return null;
        }

        if (path.Contains("://", StringComparison.Ordinal))
        {
            errors.Add($"{UnsupportedFormCategory}:absolute-url-not-allowed:{id}");
            return null;
        }

        if (!element.TryGetProperty("expect", out var expect) || expect.ValueKind != JsonValueKind.Object
            || !expect.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Number)
        {
            errors.Add($"{InvalidCategory}:scenario-expect-status:{id}");
            return null;
        }

        JsonElement? requestJson = null;
        if (element.TryGetProperty("request", out var request)
            && request.ValueKind == JsonValueKind.Object
            && request.TryGetProperty("json", out var json))
        {
            requestJson = json.Clone();
        }

        JsonElement? subset = null;
        if (expect.TryGetProperty("jsonSubset", out var jsonSubset)
            && jsonSubset.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            subset = jsonSubset.Clone();
        }

        var capture = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.TryGetProperty("capture", out var captures) && captures.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in captures.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    errors.Add($"{UnsupportedFormCategory}:capture-must-be-a-string-expression:{id}");
                    return null;
                }

                capture[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }

        return new HttpScenario(id, method, path, requestJson, status.GetInt32(), subset, capture);
    }

}

/// <summary>One ordered HTTP scenario.</summary>
public sealed record HttpScenario(
    string Id,
    string Method,
    string Path,
    JsonElement? RequestJson,
    int ExpectedStatus,
    JsonElement? JsonSubset,
    IReadOnlyDictionary<string, string> Capture);

/// <summary>Per-scenario evidence. No secrets; response bodies are reduced to digests and captured names.</summary>
public sealed record ScenarioResult(
    string Id,
    bool Passed,
    int? ObservedStatus,
    string? Reason,
    string? ResponseDigest);

/// <summary>Outcome of executing a frozen contract against a running candidate.</summary>
public sealed record ContractRunResult(
    bool Passed,
    IReadOnlyList<ScenarioResult> Scenarios,
    string ContractDigest)
{
    [JsonIgnore]
    public string FailureSummary => string.Join(
        "; ",
        Scenarios.Where(static s => !s.Passed).Select(static s => $"{s.Id}:{s.Reason}"));
}
