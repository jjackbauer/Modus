using Xunit;

namespace Wip.Bones.Web.Tests;

[CollectionDefinition("StrategyPool")]
public class StrategyPoolTestCollection
{
    // The pool is a DI service now, but these tests still share one web-host factory per class
    // and publish their pool through its holder; keep them sequential to avoid cross-test
    // requests observing another test's holder pool.
}
