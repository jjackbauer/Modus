using Wip.Bones.ModelProviders.DeepSeek;
using Xunit;

namespace Wip.Bones.ModelProviders.DeepSeek.Tests.Configuration;

public sealed class BonesDeepSeekProviderOptionsTests
{
    private const string ChecklistItem = BonesDeepSeekRequirementsChecklistItems.ProviderOptions;
    private const string NegativePathChecklistItem = BonesDeepSeekRequirementsChecklistItems.NegativePathIsolationTests;

    [Theory]
    [InlineData("deepseek-chat")]
    [InlineData("deepseek-reasoner")]
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
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDeepSeekProviderOptions_GivenRelativeBaseUrl_ExpectedDeterministicValidationFailure()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            BonesDeepSeekProviderOptions.Create(
                baseUrl: "api.deepseek.com",
                model: "deepseek-chat",
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
            "DeepSeek provider configuration is invalid: Model 'deepseek-unsupported' is not supported. Supported models: deepseek-chat, deepseek-reasoner.",
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
                model: "deepseek-chat",
                timeout: TimeSpan.FromSeconds(30),
                apiKeySource: apiKeySource));

        Assert.Equal(
            "DeepSeek provider configuration is invalid: ApiKeySource must use 'env:<VARIABLE_NAME>' format.",
            error.Message);
    }
}
