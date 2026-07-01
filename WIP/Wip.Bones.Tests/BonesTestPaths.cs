namespace Wip.Bones.Tests;

internal static class BonesTestPaths
{
    public static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Modus.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root from test execution directory.");
    }
}