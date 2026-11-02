using Xunit;

namespace Wip.Bones.Web.Tests;

[CollectionDefinition("StrategyPool")]
public class StrategyPoolTestCollection
{
    // This collection definition ensures all pool-modifying tests run sequentially
    // to avoid contention on the shared StrategyPool.Instance static singleton.
}
