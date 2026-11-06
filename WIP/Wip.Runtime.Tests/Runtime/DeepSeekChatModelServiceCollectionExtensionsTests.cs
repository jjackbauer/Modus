using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Chat;
using Wip.Runtime;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class DeepSeekChatModelServiceCollectionExtensionsTests
{
    [Fact]
    public void AddDeepSeekChatModel_GivenOptionsInstance_ExpectedResolvesConcreteModelAndSameChatModelInstance()
    {
        var services = new ServiceCollection();
        var options = CreateOptions();

        services.AddDeepSeekChatModel(options);

        using var provider = services.BuildServiceProvider();
        var concrete = provider.GetRequiredService<DeepSeekChatModel>();
        var chatModel = provider.GetRequiredService<IChatModel>();

        Assert.IsType<DeepSeekChatModel>(chatModel);
        Assert.Same(concrete, chatModel);
        Assert.Same(options, provider.GetRequiredService<DeepSeekModelProviderOptions>());
    }

    [Fact]
    public void AddDeepSeekChatModel_GivenOptionsResolvedFromDi_ExpectedResolvesConcreteModelAndSameChatModelInstance()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateOptions());

        services.AddDeepSeekChatModel();

        using var provider = services.BuildServiceProvider();
        var concrete = provider.GetRequiredService<DeepSeekChatModel>();
        var chatModel = provider.GetRequiredService<IChatModel>();

        Assert.Same(concrete, chatModel);
    }

    [Fact]
    public void AddDeepSeekChatModel_GivenPreRegisteredHttpClient_ExpectedPreservesTestHandlerRegistration()
    {
        var services = new ServiceCollection();
        var httpClient = new HttpClient(new StubHandler()) { BaseAddress = new Uri("https://api.deepseek.com") };
        services.AddSingleton(httpClient);

        services.AddDeepSeekChatModel(CreateOptions());

        using var provider = services.BuildServiceProvider();

        Assert.Same(httpClient, provider.GetRequiredService<HttpClient>());
        Assert.NotNull(provider.GetRequiredService<DeepSeekChatModel>());
    }

    private static DeepSeekModelProviderOptions CreateOptions()
        => new(
            baseUrl: new Uri("https://api.deepseek.com"),
            apiKey: "env:DEEPSEEK_API_KEY",
            timeout: TimeSpan.FromSeconds(30));

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}