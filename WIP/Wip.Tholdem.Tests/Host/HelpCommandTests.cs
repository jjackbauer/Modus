using Xunit;

namespace Wip.Tholdem.Tests.Host;

public sealed class HelpCommandTests
{
    [Fact]
    public void Run_GivenHelp_ExpectedUsageListsEveryCommandAndExit0()
    {
        var result = HostCliTestSupport.Run("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.Contains("0.1.0", result.Output, StringComparison.Ordinal);
        Assert.Contains("tholdem play", result.Output, StringComparison.Ordinal);
        Assert.Contains("tholdem train", result.Output, StringComparison.Ordinal);
        Assert.Contains("tholdem evaluate", result.Output, StringComparison.Ordinal);
        Assert.Contains("tholdem promote", result.Output, StringComparison.Ordinal);
        Assert.Contains("tholdem replay", result.Output, StringComparison.Ordinal);
        Assert.Contains("tholdem retention", result.Output, StringComparison.Ordinal);
        Assert.Contains("explicit confirmation", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--allow-weak-seed", result.Output, StringComparison.Ordinal);
        Assert.Contains("1099511627776", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_GivenVersion_ExpectedSemanticVersionAndExit0()
    {
        var result = HostCliTestSupport.Run("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0.1.0", result.Output.Trim());
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void Run_GivenUnknownCommand_ExpectedUsageOnErrorAndExit2()
    {
        var result = HostCliTestSupport.Run("bluff");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Output);
        Assert.Contains("usage", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bluff", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_GivenUnknownOption_ExpectedExit2()
    {
        var result = HostCliTestSupport.Run("play", "--seed", "42", "--allow-weak-seed", "--turbo");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Output);
        Assert.Contains("turbo", result.Error, StringComparison.Ordinal);
        Assert.Contains("usage", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
