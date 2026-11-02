using Microsoft.Extensions.Configuration;
using Wip.Bones.Host;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesHostAllowUnboundedOptionsTests
{
    public BonesHostAllowUnboundedOptionsTests() => ClearBonesHostEnvironment();

    [Fact]
    public void BonesHostOptions_GivenNoAllowUnboundedSetting_ExpectedDefaultFalse()
    {
        var options = Bind(new Dictionary<string, string?>());

        Assert.False(options.AllowUnbounded);
    }

    [Fact]
    public void BonesHostOptions_GivenConfigurationAllowUnboundedTrue_ExpectedOptIn()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["BonesHost:AllowUnbounded"] = "true",
        });

        Assert.True(options.AllowUnbounded);
    }

    [Fact]
    public void BonesHostOptions_GivenDoubleUnderscoreAllowUnboundedTrue_ExpectedOptIn()
    {
        try
        {
            Environment.SetEnvironmentVariable("BonesHost__AllowUnbounded", "true");

            var options = Bind(new Dictionary<string, string?>
            {
                ["BonesHost:AllowUnbounded"] = "false",
            });

            Assert.True(options.AllowUnbounded);
        }
        finally
        {
            ClearBonesHostEnvironment();
        }
    }

    [Fact]
    public void BonesHostOptions_GivenDedicatedAllowUnboundedTrue_ExpectedOptIn()
    {
        try
        {
            Environment.SetEnvironmentVariable("BonesHost__AllowUnbounded", "false");
            Environment.SetEnvironmentVariable("BONES_ALLOW_UNBOUNDED", "true");

            var options = Bind(new Dictionary<string, string?>
            {
                ["BonesHost:AllowUnbounded"] = "false",
            });

            Assert.True(options.AllowUnbounded);
        }
        finally
        {
            ClearBonesHostEnvironment();
        }
    }

    [Fact]
    public void BonesHostOptions_GivenDedicatedAllowUnboundedFalse_ExpectedVeto()
    {
        try
        {
            Environment.SetEnvironmentVariable("BONES_ALLOW_UNBOUNDED", "false");

            var options = Bind(new Dictionary<string, string?>
            {
                ["BonesHost:AllowUnbounded"] = "true",
            });

            Assert.False(options.AllowUnbounded);
        }
        finally
        {
            ClearBonesHostEnvironment();
        }
    }

    private static BonesHostOptions Bind(Dictionary<string, string?> overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["BonesHost:ModelProvider"] = "stub",
        };

        foreach (var (key, value) in overrides)
            settings[key] = value;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return BonesHostOptions.Bind(configuration);
    }

    private static void ClearBonesHostEnvironment()
    {
        Environment.SetEnvironmentVariable("BONES_ALLOW_UNBOUNDED", null);
        Environment.SetEnvironmentVariable("BonesHost__AllowUnbounded", null);
        Environment.SetEnvironmentVariable("BONES_MAX_GAMES_PER_RUN", null);
        Environment.SetEnvironmentVariable("BonesHost__MaxGamesPerRun", null);
        Environment.SetEnvironmentVariable("BonesHost__ModelProvider", null);
    }
}
