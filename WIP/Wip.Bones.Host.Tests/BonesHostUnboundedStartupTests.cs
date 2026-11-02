using Microsoft.AspNetCore.Builder;
using Wip.Bones.Host;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesHostUnboundedStartupTests
{
    [Fact]
    public async Task Build_GivenEffectiveZeroMaxGamesPerRunWithoutOptIn_ExpectedStartupRefusal()
    {
        var listenUri = BonesHostTestNetwork.AllocateListenUrl();
        var dataDirectory = BonesHostTestPaths.CreateTempDataDirectory();
        var previousMaxGames = Environment.GetEnvironmentVariable("BONES_MAX_GAMES_PER_RUN");
        var previousAllow = Environment.GetEnvironmentVariable("BONES_ALLOW_UNBOUNDED");
        var previousDoubleUnderscoreAllow = Environment.GetEnvironmentVariable("BonesHost__AllowUnbounded");
        var previousProvider = Environment.GetEnvironmentVariable("BonesHost__ModelProvider");
        var previousDataDirectory = Environment.GetEnvironmentVariable("BonesHost__DataDirectory");
        var previousListenUrl = Environment.GetEnvironmentVariable("BonesHost__ListenUrl");
        var previousAspNetCoreUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

        WebApplication? application = null;
        try
        {
            Environment.SetEnvironmentVariable("BONES_MAX_GAMES_PER_RUN", "0");
            Environment.SetEnvironmentVariable("BONES_ALLOW_UNBOUNDED", null);
            Environment.SetEnvironmentVariable("BonesHost__AllowUnbounded", null);
            Environment.SetEnvironmentVariable("BonesHost__ModelProvider", "stub");
            Environment.SetEnvironmentVariable("BonesHost__DataDirectory", dataDirectory);
            Environment.SetEnvironmentVariable("BonesHost__ListenUrl", listenUri.ToString());
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", listenUri.ToString());

            var exception = Record.Exception(() =>
            {
                application = BonesHostApplication.Build(["--urls", listenUri.ToString()]);
            });

            if (application is not null)
                await application.DisposeAsync();

            var refusal = Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("MaxGamesPerRun", refusal.Message, StringComparison.Ordinal);
            Assert.Contains("AllowUnbounded", refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BONES_MAX_GAMES_PER_RUN", previousMaxGames);
            Environment.SetEnvironmentVariable("BONES_ALLOW_UNBOUNDED", previousAllow);
            Environment.SetEnvironmentVariable("BonesHost__AllowUnbounded", previousDoubleUnderscoreAllow);
            Environment.SetEnvironmentVariable("BonesHost__ModelProvider", previousProvider);
            Environment.SetEnvironmentVariable("BonesHost__DataDirectory", previousDataDirectory);
            Environment.SetEnvironmentVariable("BonesHost__ListenUrl", previousListenUrl);
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", previousAspNetCoreUrls);

            if (Directory.Exists(dataDirectory))
            {
                try
                {
                    Directory.Delete(dataDirectory, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
