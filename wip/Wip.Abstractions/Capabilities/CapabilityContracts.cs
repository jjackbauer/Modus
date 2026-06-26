using Wip.Abstractions.Identifiers;

namespace Wip.Abstractions.Capabilities;

public enum CapabilityKind
{
    Agent = 1,
    Tool = 2,
    Validator = 3,
    Workflow = 4,
    Policy = 5,
    WorkspaceProvider = 6,
    ArtifactStore = 7,
    ModelProvider = 8,
    ContextProvider = 9,
    Reviewer = 10
}

public sealed record CapabilityContext(SessionId SessionId, string WorktreePath);

public interface ICapability<in TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
    ValueTask<TResult> ExecuteAsync(TRequest request, CapabilityContext context, CancellationToken cancellationToken);
}

public interface IAgent<in TRequest, TResult> : ICapability<TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
}

public interface ITool<in TRequest, TResult> : ICapability<TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
}

public interface IValidator<in TRequest, TResult> : ICapability<TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
}

public sealed record ModelProviderUsage
{
    public ModelProviderUsage(int inputTokens, int outputTokens)
    {
        if (inputTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(inputTokens), "Value must be greater than or equal to zero.");

        if (outputTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(outputTokens), "Value must be greater than or equal to zero.");

        InputTokens = inputTokens;
        OutputTokens = outputTokens;
    }

    public int InputTokens { get; }

    public int OutputTokens { get; }
}

public sealed record ModelProviderRequest<TPayload>
    where TPayload : notnull
{
    public ModelProviderRequest(
        TPayload payload,
        string modelId,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(modelId));

        Payload = payload;
        ModelId = modelId;
        CorrelationId = correlationId;
    }

    public TPayload Payload { get; }

    public string ModelId { get; }

    public string? CorrelationId { get; }
}

public sealed record ModelProviderResponse<TPayload>
    where TPayload : notnull
{
    public ModelProviderResponse(
        TPayload payload,
        string providerId,
        string modelId,
        ModelProviderUsage? usage = null,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(providerId));

        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(modelId));

        Payload = payload;
        ProviderId = providerId;
        ModelId = modelId;
        Usage = usage;
        CorrelationId = correlationId;
    }

    public TPayload Payload { get; }

    public string ProviderId { get; }

    public string ModelId { get; }

    public ModelProviderUsage? Usage { get; }

    public string? CorrelationId { get; }
}

public interface IModelProvider<TRequestPayload, TResultPayload>
    : ICapability<ModelProviderRequest<TRequestPayload>, ModelProviderResponse<TResultPayload>>
    where TRequestPayload : notnull
    where TResultPayload : notnull
{
}

public static class ModelProviderExecutionRuntime
{
    public static ValueTask<ModelProviderResponse<TResultPayload>> ExecuteAsync<TRequestPayload, TResultPayload>(
        IModelProvider<TRequestPayload, TResultPayload> provider,
        object request,
        CapabilityContext context,
        CancellationToken cancellationToken)
        where TRequestPayload : notnull
        where TResultPayload : notnull
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);

        if (request is not ModelProviderRequest<TRequestPayload> typedRequest)
        {
            var actualRequestType = request.GetType().GenericTypeArguments.FirstOrDefault() ?? request.GetType();
            throw new InvalidOperationException(
                $"Model provider request type mismatch. Expected request type '{typeof(TRequestPayload).FullName}' but received '{actualRequestType.FullName}'.");
        }

        return provider.ExecuteAsync(typedRequest, context, cancellationToken);
    }
}
