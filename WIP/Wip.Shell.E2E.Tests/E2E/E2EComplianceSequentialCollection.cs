using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class E2EComplianceSequentialCollection
{
    public const string Name = "E2E compliance sequential";
}
