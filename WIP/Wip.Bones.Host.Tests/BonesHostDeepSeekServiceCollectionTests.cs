using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Host;
using Wip.Bones.Identifiers;
using Wip.Bones.ModelProviders.DeepSeek;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesHostDeepSeekServiceCollectionTests
{
    private const string RegistrationItem =
        BonesDeepSeekHostRequirementsChecklistItems.BonesHostServiceCollectionDeepSeekRegistration;

    private const string WorkflowModelIdItem =
        BonesDeepSeekHostRequirementsChecklistItems.BonesHostWorkflowModelIdPassthrough;

    [Fact]
    [Trait("ChecklistItem", RegistrationItem)]
    public void BonesHostServiceCollection_GivenModelProviderDeepSeek_ExpectedRegistersDeepSeekAdaptersNotStubs()
    {
        using var provider = BuildServiceProvider(BonesModelProviderKind.DeepSeek);

        var strategyProvider = provider.GetRequiredService<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>();
        var playTurnProvider = provider.GetRequiredService<IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>>();
        var enhancementProvider = provider.GetRequiredService<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>>();

        Assert.IsType<DeepSeekBonesStrategyAuthoringProvider>(strategyProvider);
        Assert.IsType<DeepSeekBonesPlayTurnProvider>(playTurnProvider);
        Assert.IsType<DeepSeekBonesStrategyEnhancementProvider>(enhancementProvider);
    }

    [Fact]
    [Trait("ChecklistItem", RegistrationItem)]
    public void BonesHostServiceCollection_GivenModelProviderStub_ExpectedRegistersStubProvidersOnly()
    {
        using var provider = BuildServiceProvider(BonesModelProviderKind.Stub);

        var strategyProvider = provider.GetRequiredService<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>();
        var playTurnProvider = provider.GetRequiredService<IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>>();
        var enhancementProvider = provider.GetRequiredService<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>>();

        Assert.Contains("StubBonesStrategyModelProvider", strategyProvider.GetType().Name, StringComparison.Ordinal);
        Assert.Contains("StubBonesPlayTurnModelProvider", playTurnProvider.GetType().Name, StringComparison.Ordinal);
        Assert.Contains("StubBonesEnhancementModelProvider", enhancementProvider.GetType().Name, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", WorkflowModelIdItem)]
    public async Task BonesHostServiceCollection_GivenDeepSeekConfig_ExpectedWorkflowUsesConfiguredModelIdInProviderRequests()
    {
        const string configuredModel = "deepseek-reasoner";
        ClearBonesHostEnvironment();
        var dataDirectory = BonesHostTestPaths.CreateTempDataDirectory();
        var options = CreateHostOptions(BonesModelProviderKind.DeepSeek, configuredModel, dataDirectory);
        var sessionStore = new BonesHostSessionStore();
        var iterationContext = sessionStore.BeginIteration(1, options.DefaultParameters, dataDirectory);

        Assert.Contains($"modelId={configuredModel}", iterationContext.TaskDescription, StringComparison.Ordinal);

        var session = new SessionSnapshot(
            SessionId: new SessionId("session-workflow-model-id"),
            WorkflowId: BonesLearningWorkflowIds.WorkflowId,
            State: SessionState.Editing,
            RepositoryPath: iterationContext.RepositoryPath,
            WorktreePath: iterationContext.WorktreePath,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            TaskDescription: iterationContext.TaskDescription);

        Assert.True(BonesLearningWorkflowParameters.TryParse(session, out var parameters));
        Assert.Equal(configuredModel, parameters.ModelId);

        var ponderRequest = new BonesPonderRequest(
            parameters.LearningPlayerId,
            session.RepositoryPath,
            parameters.ModelId!);

        Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", "test-api-key");
        try
        {
            var handler = new CapturingHttpMessageHandler(static _ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "id": "chatcmpl-test",
                          "object": "chat.completion",
                          "model": "deepseek-reasoner",
                          "choices": [
                            {
                              "index": 0,
                              "message": { "role": "assistant", "content": "# Rules\n- test" },
                              "finish_reason": "stop"
                            }
                          ],
                          "usage": { "prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15 }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json"),
                });

            var services = new ServiceCollection();
            services.AddBonesHostServices(options);
            services.AddSingleton(_ => new HttpClient(handler)
            {
                BaseAddress = new Uri("https://api.deepseek.com"),
            });

            await using var provider = services.BuildServiceProvider();
            var strategyProvider = provider.GetRequiredService<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>();
            var response = await strategyProvider.ExecuteAsync(
                new ModelProviderRequest<BonesStrategyAuthoringRequest>(
                    payload: new BonesStrategyAuthoringRequest(
                        [
                            new BonesStrategyAuthoringMessage("system", "system prompt"),
                            new BonesStrategyAuthoringMessage("user", "user prompt"),
                        ],
                        []),
                    modelId: ponderRequest.ModelId,
                    correlationId: "corr-model-id"),
                new CapabilityContext(new SessionId("session-model-id"), iterationContext.WorktreePath),
                CancellationToken.None);

            Assert.Equal("deepseek", response.ProviderId);
            Assert.Equal(configuredModel, response.ModelId);
            Assert.NotNull(handler.LastRequestBody);
            using var payload = JsonDocument.Parse(handler.LastRequestBody!);
            Assert.Equal(configuredModel, payload.RootElement.GetProperty("model").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", null);
            try
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static ServiceProvider BuildServiceProvider(BonesModelProviderKind modelProvider)
    {
        ClearBonesHostEnvironment();
        var dataDirectory = BonesHostTestPaths.CreateTempDataDirectory();
        Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", "test-api-key");
        try
        {
            var services = new ServiceCollection();
            services.AddBonesHostServices(CreateHostOptions(modelProvider, "deepseek-chat", dataDirectory));
            services.AddSingleton(_ => new HttpClient(new CapturingHttpMessageHandler(static _ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                }))
            {
                BaseAddress = new Uri("https://api.deepseek.com"),
            });

            return services.BuildServiceProvider();
        }
        finally
        {
            Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", null);
        }
    }

    private static void ClearBonesHostEnvironment()
    {
        Environment.SetEnvironmentVariable("BonesHost__ModelProvider", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__BaseUrl", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__Model", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__TimeoutSeconds", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__ApiKeySource", null);
    }

    private static BonesHostOptions CreateHostOptions(
        BonesModelProviderKind modelProvider,
        string model,
        string dataDirectory)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BonesHost:DataDirectory"] = dataDirectory,
                ["BonesHost:ListenUrl"] = BonesHostTestNetwork.AllocateListenUrl().ToString(),
                ["BonesHost:ModelProvider"] = modelProvider == BonesModelProviderKind.Stub ? "stub" : "deepseek",
                ["BonesHost:DeepSeek:BaseUrl"] = "https://api.deepseek.com",
                ["BonesHost:DeepSeek:Model"] = model,
                ["BonesHost:DeepSeek:TimeoutSeconds"] = "30",
                ["BonesHost:DeepSeek:ApiKeySource"] = "env:WIP_BONES_HOST_TEST_API_KEY",
            })
            .Build();

        return BonesHostOptions.Bind(configuration);
    }

    private sealed class CapturingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return responder(request);
        }
    }
}