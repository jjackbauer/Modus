using System.Text;
using System.Text.Json;
using Wip.Factory.ControlApi.Contracts;
using Wip.Factory.ControlApi.Infrastructure;
using Wip.Factory.Wip.Isolation;

namespace Wip.Factory.ControlApi.Infrastructure;

/// <summary>
/// Append-only operator audit for adapter mutations. The factory event log remains the authoritative history of
/// work; this journal records who asked the adapter for what, including requests the factory refused.
/// </summary>
public sealed class ApiAuditStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SecretRedactor _redactor = new();

    public ApiAuditStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = System.IO.Path.Combine(directory, "audit.jsonl");
    }

    public string Path => _path;

    public async Task<AuditEntryDto> AppendAsync(
        CallerIdentity caller,
        string action,
        string subject,
        string result,
        string? detail,
        string? previousVersion,
        string? resultingVersion,
        string? correlationId,
        string? operationId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var entry = new AuditEntryDto(
            AuditId: "audit-" + Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow.ToString("O"),
            Actor: caller.Actor,
            Role: caller.Role.ToString().ToLowerInvariant(),
            Action: action,
            Subject: subject,
            Result: result,
            Detail: Bound(detail),
            PreviousVersion: previousVersion,
            ResultingVersion: resultingVersion,
            CorrelationId: correlationId ?? caller.CorrelationId,
            OperationId: operationId,
            IdempotencyKey: idempotencyKey,
            RemoteAddress: caller.RemoteAddress);

        var line = JsonSerializer.Serialize(entry, ApiJson.Options) + "\n";
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_path, line, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        return entry;
    }

    public async Task<PageDto<AuditEntryDto>> ReadAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new PageDto<AuditEntryDto>([], limit, offset, 0, false);
        }

        var lines = await File.ReadAllLinesAsync(_path, cancellationToken).ConfigureAwait(false);
        var entries = new List<AuditEntryDto>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<AuditEntryDto>(line, ApiJson.Options);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // A torn trailing append was never a committed audit fact.
            }
        }

        var ordered = entries
            .OrderByDescending(static entry => entry.TimestampUtc, StringComparer.Ordinal)
            .ToArray();

        var page = ordered.Skip(offset).Take(limit).ToArray();
        return new PageDto<AuditEntryDto>(page, limit, offset, ordered.Length, offset + page.Length < ordered.Length);
    }

    private string? Bound(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return detail;
        }

        var redacted = _redactor.Redact(detail, out _);
        return redacted.Length <= 4000 ? redacted : redacted[..4000] + "…";
    }
}

/// <summary>A single machine-visible outcome of a command. The HTTP status and body are both recorded.</summary>
public sealed record MutationOutcome(
    int StatusCode,
    object Body,
    string? Detail = null,
    int? PreviousVersion = null,
    int? ResultingVersion = null);

public sealed record IdempotencyRecord(
    string Key,
    string RequestHash,
    string Action,
    string Subject,
    string BodyJson,
    int StatusCode,
    string CreatedAtUtc);

/// <summary>
/// Executes a mutation exactly once per idempotency key. The durable record lives under the control root, so a
/// browser retry, a refresh, or a restarted Rust process cannot repeat a factory side effect.
/// </summary>
public sealed class MutationRunner
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(48);
    private readonly string _directory;
    private readonly ApiAuditStore _audit;
    private readonly SemaphoreSlim _registryGate = new(1, 1);
    private readonly Dictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public MutationRunner(ControlApiOptions options, ApiAuditStore audit)
    {
        _audit = audit;
        _directory = System.IO.Path.Combine(options.AuditDirectory, "idempotency");
        Directory.CreateDirectory(_directory);
    }

    public async Task<IResult> RunAsync(
        HttpContext context,
        CallerIdentity caller,
        string action,
        string subject,
        object? requestBody,
        Func<CancellationToken, Task<MutationOutcome>> operation,
        CancellationToken cancellationToken = default)
    {
        if (!caller.CanWrite)
        {
            throw ApiException.Forbidden("read-only-role", "A read-only viewer may inspect the factory but may not mutate it.");
        }

        var key = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (key.Length == 0)
        {
            throw ApiException.BadRequest("idempotency-key-required", "Every mutation requires an Idempotency-Key header.");
        }

        if (key.Length is < 8 or > 128 || key.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':')))
        {
            throw ApiException.BadRequest("idempotency-key-invalid", "Idempotency-Key must be 8-128 characters of [A-Za-z0-9._:-].");
        }

        var bodyJson = requestBody is null ? "{}" : JsonSerializer.Serialize(requestBody, ApiJson.Options);
        var requestHash = Hash(action + "\n" + subject + "\n" + bodyJson);
        var path = Path.Combine(_directory, Hash(key) + ".json");
        var gate = await LockForAsync(key).ConfigureAwait(false);

        await gate.WaitAsync(context.RequestAborted).ConfigureAwait(false);
        try
        {
            var existing = await TryReadAsync(path, context.RequestAborted).ConfigureAwait(false);
            if (existing is not null && !Expired(existing))
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw ApiException.Conflict(
                        "idempotency-key-reused",
                        "This Idempotency-Key was already used for a different request. Use a new key for a new command.");
                }

                context.Response.Headers["Idempotency-Replayed"] = "true";
                return Results.Content(existing.BodyJson, "application/json; charset=utf-8", Encoding.UTF8, existing.StatusCode);
            }

            if (existing is not null)
            {
                TryDelete(path);
            }

            var operationId = "op-" + Guid.NewGuid().ToString("N");
            MutationOutcome outcome;
            try
            {
                outcome = await operation(context.RequestAborted).ConfigureAwait(false);
            }
            catch (ApiException exception)
            {
                await _audit.AppendAsync(
                    caller, action, subject, "refused", exception.Code + ": " + exception.Message,
                    null, null, caller.CorrelationId, operationId, key, context.RequestAborted).ConfigureAwait(false);
                throw;
            }

            var body = JsonSerializer.Serialize(outcome.Body, ApiJson.Options);
            var record = new IdempotencyRecord(
                key, requestHash, action, subject, body, outcome.StatusCode, DateTimeOffset.UtcNow.ToString("O"));
            await WriteAtomicAsync(path, record, context.RequestAborted).ConfigureAwait(false);

            context.Response.Headers["X-Operation-Id"] = operationId;
            await _audit.AppendAsync(
                caller,
                action,
                subject,
                outcome.StatusCode >= 200 && outcome.StatusCode < 300 ? "succeeded" : "refused",
                outcome.Detail,
                outcome.PreviousVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                outcome.ResultingVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                caller.CorrelationId,
                operationId,
                key,
                context.RequestAborted).ConfigureAwait(false);

            return Results.Json(outcome.Body, ApiJson.Options, statusCode: outcome.StatusCode);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SemaphoreSlim> LockForAsync(string key)
    {
        await _registryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_locks.TryGetValue(key, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _locks[key] = gate;
            }

            return gate;
        }
        finally
        {
            _registryGate.Release();
        }
    }

    private static bool Expired(IdempotencyRecord record)
        => !DateTimeOffset.TryParse(record.CreatedAtUtc, out var created) || DateTimeOffset.UtcNow - created > Retention;

    private static async Task<IdempotencyRecord?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<IdempotencyRecord>(json, ApiJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteAtomicAsync(string path, IdempotencyRecord record, CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(record, ApiJson.Options), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static string Hash(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
