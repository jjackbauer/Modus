using Wip.Tholdem.Agents.Runtime;
using Xunit;

namespace Wip.Tholdem.Tests.Runtime;

/// <summary>
/// Environment variables are process-wide, so the gate test runs outside the parallel suite and
/// restores both variables in <c>finally</c>.
/// </summary>
[CollectionDefinition("tholdem-environment", DisableParallelization = true)]
public sealed class TholdemEnvironmentCollection
{
}

[Collection("tholdem-environment")]
public sealed class LiveModelGateTests
{
    [Fact]
    public void IsEnabled_GivenEnvironmentMatrix_ExpectedReasonNamesMissingVariableAndHidesKey()
    {
        var originalFlag = Environment.GetEnvironmentVariable("THOLDEM_LIVE_MODEL");
        var originalKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("THOLDEM_LIVE_MODEL", null);
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);

            Assert.False(LiveModelGate.IsEnabled(out var missingFlagReason));
            Assert.NotNull(missingFlagReason);
            Assert.Contains("THOLDEM_LIVE_MODEL", missingFlagReason!, StringComparison.Ordinal);

            Environment.SetEnvironmentVariable("THOLDEM_LIVE_MODEL", "1");
            Assert.False(LiveModelGate.IsEnabled(out var missingKeyReason));
            Assert.NotNull(missingKeyReason);
            Assert.Contains("DEEPSEEK_API_KEY", missingKeyReason!, StringComparison.Ordinal);

            var fakeKey = "sk-test-fake-key-0000000000000000";
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", fakeKey);
            Assert.True(LiveModelGate.IsEnabled(out var enabledReason));
            Assert.Null(enabledReason);
            Assert.DoesNotContain(fakeKey, enabledReason ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(fakeKey, missingKeyReason!, StringComparison.Ordinal);
            Assert.DoesNotContain(fakeKey, missingFlagReason!, StringComparison.Ordinal);

            Environment.SetEnvironmentVariable("THOLDEM_LIVE_MODEL", "true");
            Assert.True(LiveModelGate.IsEnabled(out var trueReason));
            Assert.Null(trueReason);

            Environment.SetEnvironmentVariable("THOLDEM_LIVE_MODEL", "0");
            Assert.False(LiveModelGate.IsEnabled(out var zeroReason));
            Assert.NotNull(zeroReason);
            Assert.Contains("THOLDEM_LIVE_MODEL", zeroReason!, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("THOLDEM_LIVE_MODEL", originalFlag);
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", originalKey);
        }
    }
}
