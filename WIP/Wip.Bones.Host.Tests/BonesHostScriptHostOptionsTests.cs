using Microsoft.Extensions.Configuration;
using Wip.Bones.Engine;
using Wip.Bones.Host;
using Xunit;

namespace Wip.Bones.Host.Tests;

/// <summary>
/// WP-1.6: the ScriptHost:Mode flag binds deterministically, defaults to the safe InProcess mode,
/// and rejects unknown values instead of silently falling back.
/// </summary>
public sealed class BonesHostScriptHostOptionsTests
{
    public BonesHostScriptHostOptionsTests() => ClearScriptHostEnvironment();

    [Fact]
    public void BonesHostOptions_GivenNoScriptHostSection_ExpectedInProcessDefaultAndTrustedOnly()
    {
        var options = Bind([]);

        Assert.Equal(BonesScriptHostMode.InProcess, options.ScriptHost.Mode);
        Assert.True(options.ScriptHost.TrustedOnly);
        Assert.Null(options.ScriptHost.WorkerPath);
    }

    [Fact]
    public void BonesHostOptions_GivenWorkerMode_ExpectedWorkerModeAndWorkerPathBound()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["BonesHost:ScriptHost:Mode"] = "Worker",
            ["BonesHost:ScriptHost:WorkerPath"] = @"C:\wip\Wip.Sandbox.Worker.dll",
        });

        Assert.Equal(BonesScriptHostMode.Worker, options.ScriptHost.Mode);
        Assert.True(options.ScriptHost.TrustedOnly);
        Assert.Equal(@"C:\wip\Wip.Sandbox.Worker.dll", options.ScriptHost.WorkerPath);
    }

    [Fact]
    public void BonesHostOptions_GivenExplicitInProcessMode_ExpectedInProcess()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["BonesHost:ScriptHost:Mode"] = "InProcess",
        });

        Assert.Equal(BonesScriptHostMode.InProcess, options.ScriptHost.Mode);
    }

    [Fact]
    public void BonesHostOptions_GivenWorkerModeCaseInsensitive_ExpectedWorker()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["BonesHost:ScriptHost:Mode"] = "worker",
        });

        Assert.Equal(BonesScriptHostMode.Worker, options.ScriptHost.Mode);
    }

    [Fact]
    public void BonesHostOptions_GivenUnknownScriptHostMode_ExpectedDeterministicStartupValidationFailure()
    {
        // The configuration binder rejects an unknown enum value instead of silently falling back
        // to the InProcess default.
        var exception = Assert.Throws<InvalidOperationException>(() => Bind(new Dictionary<string, string?>
        {
            ["BonesHost:ScriptHost:Mode"] = "Sandbox",
        }));

        Assert.Contains("Sandbox", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(BonesScriptHostMode), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BonesHostOptions_GivenTrustedOnlyFalse_ExpectedBoundForTheRuntimeGuard()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["BonesHost:ScriptHost:TrustedOnly"] = "false",
        });

        Assert.False(options.ScriptHost.TrustedOnly);
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

    private static void ClearScriptHostEnvironment()
    {
        Environment.SetEnvironmentVariable("BonesHost__ScriptHost__Mode", null);
        Environment.SetEnvironmentVariable("BonesHost__ScriptHost__TrustedOnly", null);
        Environment.SetEnvironmentVariable("BonesHost__ScriptHost__WorkerPath", null);
        Environment.SetEnvironmentVariable("BonesHost__ModelProvider", null);
    }
}