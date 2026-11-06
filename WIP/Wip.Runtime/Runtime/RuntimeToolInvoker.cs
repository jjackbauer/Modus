using System.Reflection;
using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;
using Wip.Abstractions.Privacy;
using Wip.Abstractions.Tools;

namespace Wip.Runtime.Runtime;

/// <summary>
/// Default <see cref="IToolInvoker"/> over capability tool descriptors (WP-3.2). Tool calls arrive as
/// a name plus JSON arguments; the invoker deserializes, enforces permissions, applies a per-tool
/// timeout, truncates observations and turns expected failures into deterministic ToolResults.
/// Recorded tool-call artifacts are redacted (WP5); the returned observation is not.
/// </summary>
public sealed class RuntimeToolInvoker : IToolInvoker
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyDictionary<string, ICapabilityDescriptor> _tools;
    private readonly ToolInvocationOptions _options;
    private readonly IArtifactStore? _artifactStore;

    public RuntimeToolInvoker(
        IServiceProvider serviceProvider,
        IReadOnlyCollection<ICapabilityDescriptor> capabilityDescriptors,
        ToolInvocationOptions? options = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        ArgumentNullException.ThrowIfNull(capabilityDescriptors);

        _tools = capabilityDescriptors
            .Where(static descriptor => descriptor.Kind == CapabilityKind.Tool)
            .GroupBy(static descriptor => descriptor.CapabilityId.Value, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        _options = options ?? new ToolInvocationOptions();
        _artifactStore = serviceProvider.GetService(typeof(IArtifactStore)) as IArtifactStore;
    }

    public async ValueTask<ToolResult> InvokeAsync(
        ToolCall call,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(context);

        if (!_tools.TryGetValue(call.Name, out var descriptor))
        {
            return ToolResult.Error(call.Id, $"unknown-tool: no tool named '{call.Name}' is registered.");
        }

        object? request;
        try
        {
            request = JsonSerializer.Deserialize(call.Arguments.GetRawText(), descriptor.RequestType, JsonOptions);
        }
        catch (JsonException ex)
        {
            return ToolResult.Error(call.Id, $"invalid-arguments: {ex.Message}");
        }

        if (request is null)
        {
            return ToolResult.Error(call.Id, "invalid-arguments: the tool arguments deserialized to null.");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_options.Timeout);

        try
        {
            PermissionGuard.Ensure(_serviceProvider, descriptor, context.SessionId);

            var result = await InvokeCoreAsync(descriptor, request, context, timeoutSource.Token);
            var content = Truncate(Serialize(result));

            if (_options.RecordArtifacts)
            {
                await TryRecordArtifactAsync(call, content, context, cancellationToken);
            }

            return ToolResult.Success(call.Id, content);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ToolResult.Error(
                call.Id,
                $"timeout: tool '{call.Name}' exceeded {_options.Timeout.TotalSeconds:0.###}s.");
        }
        catch (PermissionDeniedException ex)
        {
            return ToolResult.Error(call.Id, $"permission-denied: {ex.Message}");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            if (cancellationToken.IsCancellationRequested && ex.InnerException is OperationCanceledException)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return ToolResult.Error(call.Id, DescribeFailure(call.Name, ex.InnerException));
        }
        catch (Exception ex)
        {
            return ToolResult.Error(call.Id, DescribeFailure(call.Name, ex));
        }
    }

    private static string DescribeFailure(string toolName, Exception exception)
        => exception is OperationCanceledException
            ? $"cancelled: tool '{toolName}' was cancelled."
            : $"tool-error: {exception.GetType().Name}: {exception.Message}";

    private async ValueTask<object?> InvokeCoreAsync(
        ICapabilityDescriptor descriptor,
        object request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        var method = typeof(RuntimeToolInvoker).GetMethod(
            nameof(InvokeTypedAsync),
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var genericMethod = method.MakeGenericMethod(descriptor.RequestType, descriptor.ResultType);
        return await (ValueTask<object?>)genericMethod.Invoke(
            this,
            [descriptor, request, context, cancellationToken])!;
    }

    private async ValueTask<object?> InvokeTypedAsync<TRequest, TResult>(
        ICapabilityDescriptor descriptor,
        object request,
        CapabilityContext context,
        CancellationToken cancellationToken)
        where TRequest : notnull
        where TResult : notnull
    {
        if (request is not TRequest typedRequest)
        {
            throw new InvalidOperationException(
                $"Tool '{descriptor.CapabilityId.Value}' expected '{typeof(TRequest).Name}' but received '{request.GetType().Name}'.");
        }

        var service = _serviceProvider.GetService(descriptor.CapabilityType)
            ?? throw new InvalidOperationException(
                $"Tool '{descriptor.CapabilityId.Value}' is not registered in the service provider.");

        if (service is not ITool<TRequest, TResult> tool)
        {
            throw new InvalidOperationException(
                $"Tool '{descriptor.CapabilityId.Value}' is not resolvable as ITool<{typeof(TRequest).Name}, {typeof(TResult).Name}>.");
        }

        return await tool.ExecuteAsync(typedRequest, context, cancellationToken);
    }

    private string Serialize(object? result)
    {
        if (result is null)
        {
            return "null";
        }

        if (result is string text)
        {
            return text;
        }

        return JsonSerializer.Serialize(result, result.GetType(), JsonOptions);
    }

    private string Truncate(string content)
    {
        if (_options.MaxObservationCharacters <= 0 || content.Length <= _options.MaxObservationCharacters)
        {
            return content;
        }

        return content[.._options.MaxObservationCharacters]
            + $"...[truncated {content.Length - _options.MaxObservationCharacters} chars]";
    }

    private async ValueTask TryRecordArtifactAsync(
        ToolCall call,
        string content,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        if (_artifactStore is null)
        {
            return;
        }

        try
        {
            var redactor = _serviceProvider.GetService(typeof(ISecretRedactor)) as ISecretRedactor
                ?? SecretRedactor.Default;

            var payload = JsonSerializer.Serialize(
                new
                {
                    call.Id,
                    call.Name,
                    Arguments = redactor.Redact(call.Arguments.GetRawText()).Value,
                    Result = redactor.Redact(content).Value,
                },
                JsonOptions);

            await _artifactStore.SaveAsync(
                context.SessionId,
                new ArtifactContent(
                    new ArtifactId($"tool-call-{Guid.NewGuid():N}"),
                    ArtifactKind.Json,
                    $"tool-call-{Sanitize(call.Name)}.json",
                    payload,
                    "RuntimeToolInvoker",
                    "1.0.0",
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch
        {
            // Artifact recording is observability, not part of the tool contract.
        }
    }

    private static string Sanitize(string value)
        => new(value.Select(static character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '-').ToArray());
}