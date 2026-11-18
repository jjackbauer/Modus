using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wip.Factory.Host.Factory;

/// <summary>
/// Executes a frozen acceptance contract over real HTTP (v0.3 §4.1, §7.3). It never trusts generated tests,
/// the model's claims or the application's self-report: the only thing that counts is what the socket returns.
/// </summary>
public sealed class AcceptanceContractRunner
{
    public const string StatusMismatch = "status-mismatch";
    public const string SubsetMismatch = "json-subset-mismatch";
    public const string RequestFailed = "request-failed";

    private readonly HttpClient _http;

    public AcceptanceContractRunner(HttpClient? http = null)
        => _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

    public async ValueTask<ContractRunResult> RunAsync(
        AcceptanceContract contract,
        Uri baseAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(baseAddress);

        var captured = new Dictionary<string, string>(StringComparer.Ordinal);
        var results = new List<ScenarioResult>(contract.Scenarios.Count);

        foreach (var scenario in contract.Scenarios)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = Substitute(scenario.Path, captured);
            var uri = new Uri(baseAddress, path);

            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(scenario.Method), uri);
                if (scenario.RequestJson is { } body)
                {
                    request.Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
                }

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                var digest = Digest(text);

                if (status != scenario.ExpectedStatus)
                {
                    results.Add(new ScenarioResult(scenario.Id, false, status,
                        $"{StatusMismatch}: expected {scenario.ExpectedStatus} observed {status}", digest));
                    continue;
                }

                if (scenario.JsonSubset is { } expected)
                {
                    using var actualDocument = TryParse(text);
                    if (actualDocument is null)
                    {
                        results.Add(new ScenarioResult(scenario.Id, false, status, $"{SubsetMismatch}: response was not JSON", digest));
                        continue;
                    }

                    if (!JsonSubset.Matches(actualDocument.RootElement, expected, out var path2))
                    {
                        results.Add(new ScenarioResult(scenario.Id, false, status, $"{SubsetMismatch}:{path2}", digest));
                        continue;
                    }

                    foreach (var (name, expression) in scenario.Capture)
                    {
                        if (JsonSubset.TrySelect(actualDocument.RootElement, expression, out var value))
                        {
                            captured[name] = value;
                        }
                        else
                        {
                            results.Add(new ScenarioResult(scenario.Id, false, status,
                                $"{SubsetMismatch}:capture-not-found:{name}", digest));
                            goto next;
                        }
                    }
                }

                results.Add(new ScenarioResult(scenario.Id, true, status, null, digest));
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                results.Add(new ScenarioResult(scenario.Id, false, null, $"{RequestFailed}:{exception.GetType().Name}", null));
            }

        next: ;
        }

        return new ContractRunResult(results.All(static r => r.Passed), results, contract.Digest);
    }

    private static string Substitute(string path, IReadOnlyDictionary<string, string> captured)
    {
        var result = path;
        foreach (var (name, value) in captured)
        {
            result = result.Replace("{" + name + "}", Uri.EscapeDataString(value), StringComparison.Ordinal);
        }

        return result;
    }

    private static JsonDocument? TryParse(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Digest(string text)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

/// <summary>
/// Structural subset matching (v0.3 §4.1): maps allow extra fields, arrays compare element-wise in order, and
/// scalars must be equal. Unsupported forms are rejected rather than silently skipped.
/// </summary>
public static class JsonSubset
{
    public static bool Matches(JsonElement actual, JsonElement expected, out string path)
    {
        path = "$";

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                if (actual.ValueKind != JsonValueKind.Object)
                {
                    path = "$:expected-object";
                    return false;
                }

                foreach (var property in expected.EnumerateObject())
                {
                    if (!actual.TryGetProperty(property.Name, out var actualProperty))
                    {
                        path = "$." + property.Name + ":missing";
                        return false;
                    }

                    if (!Matches(actualProperty, property.Value, out var nested))
                    {
                        path = "$." + property.Name + nested[1..];
                        return false;
                    }
                }

                return true;

            case JsonValueKind.Array:
                if (actual.ValueKind != JsonValueKind.Array)
                {
                    path = "$:expected-array";
                    return false;
                }

                if (actual.GetArrayLength() != expected.GetArrayLength())
                {
                    path = $"$:length {actual.GetArrayLength()} != {expected.GetArrayLength()}";
                    return false;
                }

                var index = 0;
                var e = expected.EnumerateArray().GetEnumerator();
                var a = actual.EnumerateArray().GetEnumerator();
                while (e.MoveNext() && a.MoveNext())
                {
                    if (!Matches(a.Current, e.Current, out var itemPath))
                    {
                        path = $"$[{index}]" + itemPath[1..];
                        return false;
                    }

                    index++;
                }

                return true;

            case JsonValueKind.String:
                if (actual.ValueKind != JsonValueKind.String
                    || !string.Equals(actual.GetString(), expected.GetString(), StringComparison.Ordinal))
                {
                    path = "$:string";
                    return false;
                }

                return true;

            case JsonValueKind.Number:
                if (actual.ValueKind != JsonValueKind.Number || actual.GetDecimal() != expected.GetDecimal())
                {
                    path = "$:number";
                    return false;
                }

                return true;

            case JsonValueKind.True:
            case JsonValueKind.False:
                return actual.ValueKind == expected.ValueKind;

            case JsonValueKind.Null:
                return actual.ValueKind == JsonValueKind.Null;

            default:
                path = "$:unsupported-expected-form";
                return false;
        }
    }

    /// <summary>Minimal JSONPath subset: <c>$.a.b</c> and <c>$.a[0]</c>.</summary>
    public static bool TrySelect(JsonElement root, string expression, out string value)
    {
        value = string.Empty;

        if (!expression.StartsWith("$.", StringComparison.Ordinal))
        {
            return false;
        }

        var current = root;
        foreach (var segment in expression[2..].Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = segment;
            var index = -1;

            var bracket = segment.IndexOf('[', StringComparison.Ordinal);
            if (bracket > 0 && segment.EndsWith(']'))
            {
                name = segment[..bracket];
                if (!int.TryParse(segment[(bracket + 1)..^1], out index))
                {
                    return false;
                }
            }

            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out var next))
            {
                return false;
            }

            if (index >= 0)
            {
                if (next.ValueKind != JsonValueKind.Array || next.GetArrayLength() <= index)
                {
                    return false;
                }

                next = next[index];
            }

            current = next;
        }

        value = current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() ?? string.Empty,
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => current.GetRawText()
        };

        return true;
    }
}
