using Wip.Bones.ModelProviders.DeepSeek;
using Wip.Runtime.Runtime;
using Xunit;
using Wip.Bones.Agents.Configuration;

namespace Wip.Bones.ModelProviders.DeepSeek.Tests.Configuration;

public sealed class BonesDeepSeekProviderOptionsTests
{
    private const string ChecklistItem = BonesDeepSeekRequirementsChecklistItems.ProviderOptions;
    private const string NegativePathChecklistItem = BonesDeepSeekRequirementsChecklistItems.NegativePathIsolationTests;

    [Theory]
    [InlineData("deepseek-flash")]
    [InlineData("deepseek-v4-pro")]
    [InlineData("deepseek-v4-flash")]
    [InlineData("deepseek-v4-flash-vision-exp")]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDeepSeekProviderOptions_GivenValidDeepSeekChatModel_ConstructsWithNormalizedDefaults(string model)
    {
        var options = BonesDeepSeekProviderOptions.Create(
            baseUrl: "https://api.deepseek.com/v1/",
            model: model,
            timeout: TimeSpan.FromSeconds(30),
            apiKeySource: "env:DEEPSEEK_API_KEY");

        Assert.Equal(new Uri("https://api.deepseek.com/v1/", UriKind.Absolute), options.BaseUrl);
        Assert.Equal(model, options.Model.Value);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
        Assert.Equal(BonesDeepSeekApiKeySourceKind.EnvironmentVariable, options.ApiKeySourceKind);
        Assert.Equal("DEEPSEEK_API_KEY", options.ApiKeySourceReference);
        Assert.Equal("env:DEEPSEEK_API_KEY", options.ApiKeySource);
        Assert.Equal(DeepSeekThinkingMode.Disabled, options.Thinking);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDeepSeekProviderOptions_GivenThinkingEnabled_ExpectedPreservedForRequestMapping()
    {
        var options = BonesDeepSeekProviderOptions.Create(
            baseUrl: "https://api.deepseek.com",
            model: "deepseek-v4-pro",
            timeout: TimeSpan.FromSeconds(30),
            apiKeySource: "env:DEEPSEEK_API_KEY",
            thinking: DeepSeekThinkingMode.Enabled);

        Assert.Equal(DeepSeekThinkingMode.Enabled, options.Thinking);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDeepSeekProviderOptions_GivenRelativeBaseUrl_ExpectedDeterministicValidationFailure()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            BonesDeepSeekProviderOptions.Create(
                baseUrl: "api.deepseek.com",
                model: "deepseek-flash",
                timeout: TimeSpan.FromSeconds(30),
                apiKeySource: "env:DEEPSEEK_API_KEY"));

        Assert.Equal(
            "DeepSeek provider configuration is invalid: BaseUrl must be an absolute HTTP or HTTPS URL.",
            error.Message);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", NegativePathChecklistItem)]
    public void BonesDeepSeekProviderOptions_GivenUnsupportedModel_ExpectedDeterministicValidationFailure()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            BonesDeepSeekProviderOptions.Create(
                baseUrl: "https://api.deepseek.com",
                model: "deepseek-unsupported",
                timeout: TimeSpan.FromSeconds(30),
                apiKeySource: "env:DEEPSEEK_API_KEY"));

        Assert.Equal(
            "DeepSeek provider configuration is invalid: Model 'deepseek-unsupported' is not supported. Supported models: deepseek-flash, deepseek-v4-pro, deepseek-v4-flash, deepseek-v4-flash-vision-exp.",
            error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("DEEPSEEK_API_KEY")]
    [InlineData("env:")]
    [InlineData("env: ")]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDeepSeekProviderOptions_GivenInvalidApiKeySource_ExpectedDeterministicValidationFailure(string apiKeySource)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            BonesDeepSeekProviderOptions.Create(
                baseUrl: "https://api.deepseek.com",
                model: "deepseek-flash",
                timeout: TimeSpan.FromSeconds(30),
                apiKeySource: apiKeySource));

        Assert.Equal(
            "DeepSeek provider configuration is invalid: ApiKeySource must use 'env:<VARIABLE_NAME>' format.",
            error.Message);
    }
}
