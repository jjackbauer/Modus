using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PackageSplitSerialCollection
{
    public const string Name = "PackageSplitSerial";
}
