using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

public sealed class BaselineIgnoreTests
{
    [Fact]
    public void Production_GivenFreshRepository_WritesAnIgnoreListThatActuallyIgnoresBuildOutput()
    {
        var ignore = RealModelImplementationRunner.DefaultGitIgnore;

        // A literal backtick-n (from a mis-escaped string) silently ignores nothing; assert real lines.
        var lines = ignore.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains("bin/", lines);
        Assert.Contains("obj/", lines);
        Assert.DoesNotContain('`', ignore);
    }
}