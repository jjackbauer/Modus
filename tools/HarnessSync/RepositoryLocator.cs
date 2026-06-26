namespace HarnessSync;

public static class RepositoryLocator
{
    public static string FindRepositoryRoot(string? startPath = null)
    {
        var current = Path.GetFullPath(startPath ?? Directory.GetCurrentDirectory());
        while (true)
        {
            if (Directory.Exists(Path.Combine(current, "harness")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                throw new InvalidOperationException("Could not locate repository root containing harness/.");
            }

            current = parent.FullName;
        }
    }
}