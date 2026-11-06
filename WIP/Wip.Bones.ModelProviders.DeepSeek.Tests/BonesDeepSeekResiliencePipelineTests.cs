using System.Net;
using System.Text;
using Polly;
using Xunit;
using Wip.Bones.Agents.Configuration;

namespace Wip.Bones.ModelProviders.DeepSeek.Tests;

public sealed class BonesDeepSeekResiliencePipelineTests
{
    [Fact]
    public async Task Pipeline_RetriesOn429()
    {
        var options = new BonesDeepSeekResilienceOptions
        {
            MaxRetries = 3,
            ExponentialBackoffBaseMs = 10,
            MaxConcurrentCalls = 100,
            MaxQueueDepth = 200,
        };

        var rateLimitHandler = new RateLimitAwareDelegatingHandler(options);
        var pipeline = new BonesDeepSeekResiliencePipeline(options, rateLimitHandler);

        var attemptCount = 0;
        var handler = new TestDelegatingHandler(async (request, ct) =>
        {
            Interlocked.Increment(ref attemptCount);
            await Task.Delay(1, ct);
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("rate limited"),
            };
        });

        var result = await pipeline.Pipeline.ExecuteAsync(
            async ct => await handler.SendAsyncPublic(
                new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/v1/test"),
                ct));

        // After max retries (3 retries = 4 total attempts), the pipeline returns the 429 response
        Assert.Equal(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.Equal(4, attemptCount); // 1 initial + 3 retries
    }

    [Fact]
    public async Task Pipeline_RetriesOn503()
    {
        var options = new BonesDeepSeekResilienceOptions
        {
            MaxRetries = 3,
            ExponentialBackoffBaseMs = 10,
            MaxConcurrentCalls = 100,
            MaxQueueDepth = 200,
        };

        var rateLimitHandler = new RateLimitAwareDelegatingHandler(options);
        var pipeline = new BonesDeepSeekResiliencePipeline(options, rateLimitHandler);

        var attemptCount = 0;
        var handler = new TestDelegatingHandler(async (request, ct) =>
        {
            Interlocked.Increment(ref attemptCount);
            await Task.Delay(1, ct);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("service unavailable"),
            };
        });

        var result = await pipeline.Pipeline.ExecuteAsync(
            async ct => await handler.SendAsyncPublic(
                new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/v1/test"),
                ct));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Equal(4, attemptCount);
    }

    [Fact]
    public async Task Pipeline_DoesNotRetryOn400()
    {
        var options = new BonesDeepSeekResilienceOptions
        {
            MaxRetries = 3,
            ExponentialBackoffBaseMs = 10,
            MaxConcurrentCalls = 100,
            MaxQueueDepth = 200,
        };

        var rateLimitHandler = new RateLimitAwareDelegatingHandler(options);
        var pipeline = new BonesDeepSeekResiliencePipeline(options, rateLimitHandler);

        var attemptCount = 0;
        var handler = new TestDelegatingHandler((request, ct) =>
        {
            Interlocked.Increment(ref attemptCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("bad request"),
            });
        });

        var result = await pipeline.Pipeline.ExecuteAsync(
            async ct => await handler.SendAsyncPublic(
                new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/v1/test"),
                ct));

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(1, attemptCount); // No retries on 400
    }

    [Fact]
    public async Task Pipeline_RetriesOnHttpRequestException()
    {
        var options = new BonesDeepSeekResilienceOptions
        {
            MaxRetries = 2,
            ExponentialBackoffBaseMs = 10,
            MaxConcurrentCalls = 100,
            MaxQueueDepth = 200,
        };

        var rateLimitHandler = new RateLimitAwareDelegatingHandler(options);
        var pipeline = new BonesDeepSeekResiliencePipeline(options, rateLimitHandler);

        var attemptCount = 0;
        var handler = new TestDelegatingHandler((request, ct) =>
        {
            Interlocked.Increment(ref attemptCount);
            throw new HttpRequestException("connection error");
        });

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await pipeline.Pipeline.ExecuteAsync(
                async ct => await handler.SendAsyncPublic(
                    new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/v1/test"),
                    ct));
        });

        Assert.Equal(3, attemptCount); // 1 initial + 2 retries
    }

    [Fact]
    public async Task CircuitBreaker_OpensAfterConsecutiveFailures()
    {
        var options = new BonesDeepSeekResilienceOptions
        {
            MaxRetries = 0,
            CircuitBreakThreshold = 3,
            CircuitBreakDurationSeconds = 60,
            MaxConcurrentCalls = 100,
            MaxQueueDepth = 200,
        };

        var rateLimitHandler = new RateLimitAwareDelegatingHandler(options);
        var pipeline = new BonesDeepSeekResiliencePipeline(options, rateLimitHandler);

        Assert.Equal("closed", pipeline.CircuitState);

        var attemptCount = 0;
        var handler = new TestDelegatingHandler((request, ct) =>
        {
            Interlocked.Increment(ref attemptCount);
            throw new HttpRequestException("service down");
        });

        // Cause consecutive failures to open the circuit
        for (var i = 0; i < options.CircuitBreakThreshold + 2; i++)
        {
            try
            {
                await pipeline.Pipeline.ExecuteAsync(
                    async ct => await handler.SendAsyncPublic(
                        new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/v1/test"),
                        ct));
            }
            catch
            {
                // expected
            }
        }

        // Circuit should now be open
        Assert.Equal("open", pipeline.CircuitState);
    }

    [Fact]
    public async Task Bulkhead_RejectsOverflow()
    {
        var options = new BonesDeepSeekResilienceOptions
        {
            MaxRetries = 0,
            MaxConcurrentCalls = 2,
            MaxQueueDepth = 1,
        };

        var rateLimitHandler = new RateLimitAwareDelegatingHandler(options);
        var pipeline = new BonesDeepSeekResiliencePipeline(options, rateLimitHandler);

        var completionSource = new TaskCompletionSource<bool>();

        var handler = new TestDelegatingHandler(async (request, ct) =>
        {
            await completionSource.Task;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        // Fill all concurrent slots + queue
        var tasks = new List<Task>();
        for (var i = 0; i < 3; i++) // 2 concurrent + 1 queue = 3
        {
            tasks.Add(Task.Run(async () =>
            {
                await pipeline.Pipeline.ExecuteAsync(
                    async ct => await handler.SendAsyncPublic(
                        new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/v1/test"),
                        ct));
            }));
        }

        // Let them settle into blocked state
        await Task.Delay(200);

        // Try one more — should fail fast
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await pipeline.Pipeline.ExecuteAsync(
                async ct => await handler.SendAsyncPublic(
                    new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/v1/test"),
                    ct));
        });

        // Clean up
        completionSource.TrySetResult(true);
        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task RateLimitHandler_ReadsResponseHeaders()
    {
        var options = new BonesDeepSeekResilienceOptions
        {
            PreemptiveThrottleThreshold = 5,
            MaxConcurrentCalls = 100,
            MaxQueueDepth = 200,
        };

        var innerHandler = new TestDelegatingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("ratelimit-remaining", "3");
            response.Headers.Add("ratelimit-reset",
                DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeSeconds().ToString());
            response.Headers.Add("retry-after", "5");
            return Task.FromResult(response);
        });

        var delegatingHandler = new RateLimitAwareDelegatingHandler(options)
        {
            InnerHandler = innerHandler,
        };

        // Pass a request through to populate headers
        var invoker = new HttpMessageInvoker(delegatingHandler);
        await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/v1/test"),
            CancellationToken.None);

        var snapshot = delegatingHandler.GetSnapshot();
        Assert.Equal(3, snapshot.Remaining);
        Assert.NotNull(snapshot.ResetAt);
        Assert.NotNull(snapshot.RetryAfterUtc);
    }

    [Fact]
    public async Task ResiliencePipeline_HealthPropertiesReportState()
    {
        var options = new BonesDeepSeekResilienceOptions
        {
            MaxRetries = 0,
            MaxConcurrentCalls = 12,
            MaxQueueDepth = 24,
        };

        var rateLimitHandler = new RateLimitAwareDelegatingHandler(options);
        var pipeline = new BonesDeepSeekResiliencePipeline(options, rateLimitHandler);

        // Verify initial state
        Assert.Equal("closed", pipeline.CircuitState);
        Assert.Equal(0, pipeline.BulkheadActive);
        Assert.Equal(12, pipeline.BulkheadMax);
        Assert.Equal(0.0, pipeline.RollingFailureRate);

        // Rate limit is null until a request passes through
        Assert.Null(pipeline.RateLimitRemaining);
        Assert.Null(pipeline.RateLimitReset);
    }

    private sealed class TestDelegatingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public TestDelegatingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);

        public Task<HttpResponseMessage> SendAsyncPublic(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
