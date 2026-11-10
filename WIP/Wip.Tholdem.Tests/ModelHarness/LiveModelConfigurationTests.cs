using System.Text;
using Wip.Tholdem.ModelHarness;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// WP-4: the operator-supplied live configuration and secret file. Every test uses a fake key,
/// an injected environment lookup and a counting handler; no test reads the real secret file or
/// reaches the network.
/// </summary>
public sealed class LiveModelConfigurationTests
{
    private const string FakeKey = "sk-FAKE-operator-key-not-real";

    [Fact]
    public void LiveModelConfiguration_GivenMissingFlag_ExpectedRefusalBeforeAnyRequest()
    {
        using var handler = new CountingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        Func<string, string?> lookup = name =>
            name == LiveModelConfiguration.ApiKeyVariable ? FakeKey : null;

        var exception = Assert.Throws<LlmRefinementRefusalException>(
            () => LiveModelConfiguration.Resolve(lookup));

        Assert.Equal(LlmRefinementRefusalException.LiveModelDisabledCode, exception.Code);
        Assert.Contains(LiveModelConfiguration.FlagVariable, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.SendCount);
        _ = httpClient;
    }

    [Fact]
    public void LiveModelConfiguration_GivenMissingKey_ExpectedRefusalBeforeAnyRequest()
    {
        using var handler = new CountingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        Func<string, string?> lookup = name =>
            name == LiveModelConfiguration.FlagVariable ? "1" : null;

        var exception = Assert.Throws<LlmRefinementRefusalException>(
            () => LiveModelConfiguration.Resolve(lookup));

        Assert.Equal(LlmRefinementRefusalException.LiveModelDisabledCode, exception.Code);
        Assert.Contains(LiveModelConfiguration.ApiKeyVariable, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.SendCount);
        _ = httpClient;
    }

    [Fact]
    public void LiveModelConfiguration_GivenBaseUrlOverride_ExpectedOverrideAndDefault()
    {
        Func<string, string?> overridden = name => name switch
        {
            LiveModelConfiguration.FlagVariable => "TrUe",
            LiveModelConfiguration.ApiKeyVariable => FakeKey,
            LiveModelConfiguration.BaseUrlVariable => "https://example.test/v1",
            _ => null,
        };
        var configuration = LiveModelConfiguration.Resolve(overridden);
        Assert.Equal(new Uri("https://example.test/v1"), configuration.BaseUrl);

        Func<string, string?> absent = name => name switch
        {
            LiveModelConfiguration.FlagVariable => "1",
            LiveModelConfiguration.ApiKeyVariable => FakeKey,
            _ => null,
        };
        Assert.Equal(
            new Uri(LiveModelConfiguration.DefaultBaseUrl),
            LiveModelConfiguration.Resolve(absent).BaseUrl);

        Func<string, string?> relative = name => name switch
        {
            LiveModelConfiguration.FlagVariable => "true",
            LiveModelConfiguration.ApiKeyVariable => FakeKey,
            LiveModelConfiguration.BaseUrlVariable => "not-an-absolute-url",
            _ => null,
        };
        var invalid = Assert.Throws<LlmRefinementRefusalException>(
            () => LiveModelConfiguration.Resolve(relative));
        Assert.Equal("invalid-base-url", invalid.Code);
        Assert.DoesNotContain(FakeKey, invalid.Message, StringComparison.Ordinal);

        Func<string, string?> unsupportedScheme = name => name switch
        {
            LiveModelConfiguration.FlagVariable => "1",
            LiveModelConfiguration.ApiKeyVariable => FakeKey,
            LiveModelConfiguration.BaseUrlVariable => "ftp://example.test",
            _ => null,
        };
        var unsupported = Assert.Throws<LlmRefinementRefusalException>(
            () => LiveModelConfiguration.Resolve(unsupportedScheme));
        Assert.Equal("invalid-base-url", unsupported.Code);
    }

    [Fact]
    public void SecretFile_GivenOperatorFile_ExpectedAllowlistOnlyAndNoValueLeak()
    {
        var directory = AgentTestSupport.CreateTempDirectory("tholdem-secret-file");
        try
        {
            var path = Path.Combine(directory, "deepseek.env");
            File.WriteAllText(
                path,
                "# operator secret\n"
                + "\n"
                + LiveModelConfiguration.ApiKeyVariable + "=\"" + FakeKey + "\"\n"
                + LiveModelConfiguration.BaseUrlVariable + "='https://example.test'\n"
                + "UNRELATED_TOKEN=also-not-real\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var entries = LlmSecretFile.Read(path);
            Assert.Equal(3, entries.Count);
            Assert.Equal(FakeKey, entries[LiveModelConfiguration.ApiKeyVariable]);
            Assert.Equal("https://example.test", entries[LiveModelConfiguration.BaseUrlVariable]);
            Assert.Equal("also-not-real", entries["UNRELATED_TOKEN"]);

            var captured = new Dictionary<string, string>(StringComparer.Ordinal);
            var names = LlmSecretFile.LoadIntoEnvironment(
                path,
                (name, value) => captured[name] = value);

            Assert.Equal(
                new[] { LiveModelConfiguration.ApiKeyVariable, LiveModelConfiguration.BaseUrlVariable },
                names.ToArray());
            Assert.Equal(2, captured.Count);
            Assert.Equal(FakeKey, captured[LiveModelConfiguration.ApiKeyVariable]);
            Assert.Equal("https://example.test", captured[LiveModelConfiguration.BaseUrlVariable]);
            Assert.DoesNotContain("UNRELATED_TOKEN", captured.Keys);
            foreach (var name in names)
            {
                Assert.DoesNotContain(FakeKey, name, StringComparison.Ordinal);
                Assert.DoesNotContain("https://example.test", name, StringComparison.Ordinal);
                Assert.DoesNotContain("also-not-real", name, StringComparison.Ordinal);
            }

            var gitignore = File.ReadAllText(Path.Combine(AgentTestSupport.RepositoryRoot(), ".gitignore"));
            Assert.Contains(".secrets/", gitignore, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SecretFile_GivenMalformedLine_ExpectedStableRefusal()
    {
        var directory = AgentTestSupport.CreateTempDirectory("tholdem-secret-malformed");
        try
        {
            var missingEquals = Path.Combine(directory, "missing-equals.env");
            File.WriteAllText(
                missingEquals,
                LiveModelConfiguration.ApiKeyVariable + "=" + FakeKey + "\nJUST_A_NAME\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var missing = Assert.Throws<FormatException>(() => LlmSecretFile.Read(missingEquals));
            Assert.Contains("line 2", missing.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeKey, missing.Message, StringComparison.Ordinal);

            var duplicate = Path.Combine(directory, "duplicate.env");
            File.WriteAllText(
                duplicate,
                LiveModelConfiguration.ApiKeyVariable + "=first-value\n"
                + LiveModelConfiguration.ApiKeyVariable + "=second-value\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var repeated = Assert.Throws<FormatException>(() => LlmSecretFile.Read(duplicate));
            Assert.Contains("line 2", repeated.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("first-value", repeated.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("second-value", repeated.Message, StringComparison.Ordinal);

            var emptyName = Path.Combine(directory, "empty-name.env");
            File.WriteAllText(
                emptyName,
                "=value-without-a-name\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var empty = Assert.Throws<FormatException>(() => LlmSecretFile.Read(emptyName));
            Assert.Contains("line 1", empty.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("value-without-a-name", empty.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class CountingHttpMessageHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            throw new InvalidOperationException("The counting handler must never send a request.");
        }
    }
}
