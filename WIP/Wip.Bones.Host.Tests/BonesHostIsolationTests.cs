using Wip.Bones.Host;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesHostIsolationTests
{
    private const string ShellIsolationItem = BonesHostRequirementsChecklistItems.ShellIsolation;

    [Fact]
    [Trait("ChecklistItem", ShellIsolationItem)]
    public void BonesHostIsolation_GivenLoadedHostAssembly_ExpectedNoShellHostTypesResolvable()
    {
        var hostAssembly = typeof(BonesHostApplication).Assembly;

        var shellTypes = hostAssembly.GetTypes()
            .Where(type => type.Namespace is not null
                && (type.Namespace.StartsWith("Wip.ShellHost", StringComparison.Ordinal)
                    || type.Namespace.StartsWith("Wip.Shell", StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(shellTypes);

        var referencedAssemblyNames = hostAssembly.GetReferencedAssemblies()
            .Select(static name => name.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Wip.ShellHost", referencedAssemblyNames);
        Assert.DoesNotContain("Wip.Shell", referencedAssemblyNames);
    }
}