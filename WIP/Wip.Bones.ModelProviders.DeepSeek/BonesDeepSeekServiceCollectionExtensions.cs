using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Wip.Abstractions.Capabilities;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Runtime.Runtime;

namespace Wip.Bones.ModelProviders.DeepSeek;

public static class BonesDeepSeekServiceCollectionExtensions
{
    public static IServiceCollection AddBonesDeepSeekModelProviders(
        this IServiceCollection services,
        BonesDeepSeekProviderOptions options,
        BonesDeepSeekResilienceOptions? resilienceOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton(CreateDeepSeekModelProviderOptions(options));
        services.AddSingleton(static _ => new HttpClient());
        services.AddSingleton(resilienceOptions ?? new BonesDeepSeekResilienceOptions());
        services.AddSingleton<RateLimitAwareDelegatingHandler>();
        services.AddSingleton<BonesDeepSeekResiliencePipeline>();

        // Register the resilience handler as a delegate that wraps HttpClient.SendAsync
        // through the Polly pipeline for DeepSeekModelProvider.
        // IMPORTANT: content bytes must be buffered once because HttpContent streams
        // can only be read once. On retries, we recreate the request from buffered bytes.
        services.AddSingleton<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(sp =>
        {
            var pipeline = sp.GetRequiredService<BonesDeepSeekResiliencePipeline>();
            var httpClient = sp.GetRequiredService<HttpClient>();

            return async (request, cancellationToken) =>
            {
                // Buffer content once — the stream is consumed on read and retries need fresh bytes.
                byte[]? contentBytes = null;
                string? contentType = null;
                if (request.Content is not null)
                {
                    contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                    contentType = request.Content.Headers.ContentType?.ToString();
                }

                var resilienceContext = ResilienceContextPool.Shared.Get(cancellationToken);
                try
                {
                    var result = await pipeline.Pipeline.ExecuteAsync(
                        async ctx =>
                        {
                            var retryRequest = CreateFreshHttpRequestMessage(
                                request.Method,
                                request.RequestUri!,
                                contentBytes,
                                contentType,
                                request.Headers);
                            return await httpClient.SendAsync(
                                retryRequest,
                                HttpCompletionOption.ResponseHeadersRead,
                                ctx.CancellationToken);
                        },
                        resilienceContext);

                    return result;
                }
                finally
                {
                    ResilienceContextPool.Shared.Return(resilienceContext);
                }
            };
        });

        services.AddSingleton(static sp =>
        {
            var httpClient = sp.GetRequiredService<HttpClient>();
            var options = sp.GetRequiredService<DeepSeekModelProviderOptions>();
            var resilienceHandler = sp.GetService<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>();

            return new DeepSeekModelProvider(httpClient, options, resilienceHandler);
        });

        services.AddSingleton<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>, DeepSeekBonesStrategyAuthoringProvider>();
        services.AddSingleton<IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>, DeepSeekBonesPlayTurnProvider>();
        services.AddSingleton<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>, DeepSeekBonesStrategyEnhancementProvider>();

        return services;
    }

    private static DeepSeekModelProviderOptions CreateDeepSeekModelProviderOptions(BonesDeepSeekProviderOptions options) =>
        new(
            baseUrl: options.BaseUrl,
            apiKey: options.ApiKeySource,
            timeout: options.Timeout,
            temperature: options.Temperature);

    private static HttpRequestMessage CreateFreshHttpRequestMessage(
        HttpMethod method,
        Uri requestUri,
        byte[]? contentBytes,
        string? contentType,
        System.Net.Http.Headers.HttpRequestHeaders originalHeaders)
    {
        var request = new HttpRequestMessage(method, requestUri);

        if (contentBytes is not null && contentBytes.Length > 0)
        {
            request.Content = new ByteArrayContent(contentBytes);
            if (!string.IsNullOrWhiteSpace(contentType))
                request.Content.Headers.ContentType =
                    System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        }

        // Copy original request headers (Authorization, Accept, etc.)
        foreach (var header in originalHeaders)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return request;
    }
}
