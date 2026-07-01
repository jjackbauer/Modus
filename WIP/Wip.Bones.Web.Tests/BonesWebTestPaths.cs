namespace Wip.Bones.Web.Tests;

internal static class BonesWebTestPaths
{
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Modus.slnx"))
                || File.Exists(Path.Combine(directory.FullName, "Modus.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }
}
