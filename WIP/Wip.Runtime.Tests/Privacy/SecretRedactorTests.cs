using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Privacy;

public sealed class SecretRedactorTests
{
    [Fact]
    public void Redact_GivenApiKeyAndBearerToken_ExpectedBothRedacted()
    {
        var result = SecretRedactor.Default.Redact(
            "call with sk-abcdef0123456789abcdef and Authorization: Bearer abcdefghijklmnop1234");

        Assert.DoesNotContain("sk-abcdef0123456789abcdef", result.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer abcdefghijklmnop1234", result.Value, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result.Value, StringComparison.Ordinal);
        Assert.True(result.RedactedCount >= 2);
    }

    [Fact]
    public void Redact_GivenOrdinaryText_ExpectedUnchanged()
    {
        const string input = "The repository has three modules and a green test suite.";

        var result = SecretRedactor.Default.Redact(input);

        Assert.Equal(input, result.Value);
        Assert.Equal(0, result.RedactedCount);
    }

    [Fact]
    public void Redact_GivenAssignment_ExpectedNamePreservedAndValueRedacted()
    {
        var result = SecretRedactor.Default.Redact("apiKey=supersecretvalue123");

        Assert.Contains("apiKey=[REDACTED]", result.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("supersecretvalue123", result.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_GivenPrivateKeyBlock_ExpectedRedacted()
    {
        var result = SecretRedactor.Default.Redact(
            "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA\n-----END RSA PRIVATE KEY-----");

        Assert.Equal("[REDACTED]", result.Value);
    }

    [Fact]
    public void Redact_GivenAlreadyRedactedInput_ExpectedIdempotent()
    {
        var first = SecretRedactor.Default.Redact("token=abcdefghijklmnop1234");

        var second = SecretRedactor.Default.Redact(first.Value);

        Assert.Equal(first.Value, second.Value);
    }
}
