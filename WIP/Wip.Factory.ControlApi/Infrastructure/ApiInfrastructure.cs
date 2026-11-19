using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Wip.Factory.ControlApi.Infrastructure;

public enum ControlRole
{
    Viewer = 1,
    Operator = 2,
    Admin = 3
}

/// <summary>The authenticated caller of one adapter request. Supplied by the Rust server after its own auth.</summary>
public sealed record CallerIdentity(ControlRole Role, string Actor, string? CorrelationId, string? RemoteAddress, string RoleHeader)
{
    public bool CanWrite => Role is ControlRole.Operator or ControlRole.Admin;
    public bool IsAdmin => Role == ControlRole.Admin;
}

public sealed class ApiException : Exception
{
    public ApiException(int statusCode, string code, string message, string category, bool retryable = false)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Category = category;
        Retryable = retryable;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public string Category { get; }
    public bool Retryable { get; }

    public static ApiException BadRequest(string code, string message)
        => new(StatusCodes.Status400BadRequest, code, message, "validation");

    public static ApiException Forbidden(string code, string message)
        => new(StatusCodes.Status403Forbidden, code, message, "forbidden");

    public static ApiException NotFound(string code, string message)
        => new(StatusCodes.Status404NotFound, code, message, "not-found");

    public static ApiException Conflict(string code, string message)
        => new(StatusCodes.Status409Conflict, code, message, "conflict");

    public static ApiException Unavailable(string code, string message)
        => new(StatusCodes.Status503ServiceUnavailable, code, message, "unavailable", retryable: true);
}

public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public static class ApiIdentity
{
    /// <summary>Reads the service token and role headers. Returns null and writes the refusal when invalid.</summary>
    public static async Task<CallerIdentity?> AuthenticateAsync(
        HttpContext context,
        ControlApiOptions options,
        CancellationToken cancellationToken)
    {
        if (context.Request.Path.Equals("/v1/live", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await WriteRefusalAsync(context, 401, "missing-service-credential", "The control adapter requires a bearer credential.", "unauthenticated", cancellationToken).ConfigureAwait(false);
            return null;
        }

        var supplied = header["Bearer ".Length..].Trim();
        if (!FixedTimeEquals(supplied, options.Token))
        {
            await WriteRefusalAsync(context, 401, "invalid-service-credential", "The supplied credential is not valid.", "unauthenticated", cancellationToken).ConfigureAwait(false);
            return null;
        }

        var roleText = context.Request.Headers["X-Control-Plane-Role"].ToString();
        var role = roleText.Trim().ToLowerInvariant() switch
        {
            "viewer" => ControlRole.Viewer,
            "operator" => ControlRole.Operator,
            "admin" => ControlRole.Admin,
            _ => (ControlRole?)null
        };

        if (role is null)
        {
            await WriteRefusalAsync(context, 403, "missing-caller-role", "The X-Control-Plane-Role header must be viewer, operator or admin.", "forbidden", cancellationToken).ConfigureAwait(false);
            return null;
        }

        var actorRaw = context.Request.Headers["X-Control-Plane-Actor"].ToString();
        var actor = string.IsNullOrWhiteSpace(actorRaw)
            ? "control-plane-service"
            : Uri.UnescapeDataString(actorRaw).Trim();
        actor = Sanitize(actor);
        if (actor.Length == 0)
        {
            actor = "control-plane-service";
        }

        var correlation = context.Request.Headers["X-Correlation-Id"].ToString();
        var identity = new CallerIdentity(
            role.Value,
            actor,
            string.IsNullOrWhiteSpace(correlation) ? null : Sanitize(correlation),
            context.Connection.RemoteIpAddress?.ToString(),
            roleText);

        context.Items[nameof(CallerIdentity)] = identity;
        return identity;
    }

    public static CallerIdentity RequireCaller(HttpContext context)
        => context.Items[nameof(CallerIdentity)] as CallerIdentity
           ?? throw ApiException.Unavailable("caller-identity-missing", "The request was not authenticated by the adapter pipeline.");

    public static void RequireWrite(HttpContext context)
    {
        var caller = RequireCaller(context);
        if (!caller.CanWrite)
        {
            throw ApiException.Forbidden("read-only-role", "A read-only viewer may inspect the factory but may not mutate it.");
        }
    }

    public static void RequireAdmin(HttpContext context)
    {
        var caller = RequireCaller(context);
        if (!caller.IsAdmin)
        {
            throw ApiException.Forbidden("administrator-required", "This action changes execution authority and requires the administrator role.");
        }
    }

    private static async Task WriteRefusalAsync(
        HttpContext context,
        int status,
        string code,
        string message,
        string category,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        var body = new Contracts.ErrorEnvelope(new Contracts.ErrorBody(code, message, category, false));
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, ApiJson.Options), cancellationToken).ConfigureAwait(false);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string Sanitize(string value)
        => new(value.Where(character => !char.IsControl(character)).Take(160).ToArray());
}

public static class ApiSecurity
{
    public static IApplicationBuilder UseAdapterSecurity(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";

            var options = context.RequestServices.GetRequiredService<ControlApiOptions>();
            if (context.Request.Path.Equals("/v1/live", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync("""{"status":"ok"}""").ConfigureAwait(false);
                return;
            }

            var identity = await ApiIdentity.AuthenticateAsync(context, options, context.RequestAborted).ConfigureAwait(false);
            if (identity is null)
            {
                return;
            }

            await next(context).ConfigureAwait(false);
        });

        return app;
    }

    public static IApplicationBuilder UseAdapterErrors(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (ApiException exception)
            {
                await WriteAsync(context, exception.StatusCode, exception.Code, exception.Message, exception.Category, exception.Retryable).ConfigureAwait(false);
            }
            catch (BadHttpRequestException exception)
            {
                await WriteAsync(context, 400, "invalid-request", exception.Message, "validation", false).ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                await WriteAsync(context, 400, "invalid-json", "The request body is not valid JSON: " + exception.Message, "validation", false).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The caller disconnected; there is no response to write.
            }
            catch (Exception exception)
            {
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ControlApi");
                logger.LogError(exception, "Unhandled adapter error for {Method} {Path}", context.Request.Method, context.Request.Path);
                await WriteAsync(context, 500, "internal-error", "The adapter failed to complete the operation.", "internal", true).ConfigureAwait(false);
            }
        });

        return app;
    }

    private static async Task WriteAsync(
        HttpContext context,
        int status,
        string code,
        string message,
        string category,
        bool retryable)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = new Contracts.ErrorEnvelope(new Contracts.ErrorBody(code, message, category, retryable));
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, ApiJson.Options)).ConfigureAwait(false);
    }
}
