namespace Wip.Sandbox.WorkerClient;

public sealed record SandboxWorkerLaunch(string FileName, IReadOnlyList<string> Arguments);

public static class SandboxWorkerLauncher
{
    public static SandboxWorkerLaunch Resolve(
        string workerPath,
        SandboxWorkerOptions options,
        string dotnetPath,
        bool isLinux = false,
        string? prlimitPath = null)
    {
        if (isLinux && !string.IsNullOrWhiteSpace(prlimitPath))
        {
            var cpuSeconds = Math.Max(30, (int)Math.Ceiling(options.MoveTimeout.TotalSeconds * 10));
            var memoryBytes = Math.Max(options.MaxMemoryBytes, 8L * 1024 * 1024 * 1024);
            return new SandboxWorkerLaunch(prlimitPath,
            [
                "--as=" + memoryBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--nproc=" + Math.Max(4096, options.MaxProcesses * 16).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--cpu=" + cpuSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--",
                dotnetPath,
                workerPath
            ]);
        }

        return new SandboxWorkerLaunch(dotnetPath, [workerPath]);
    }

    public static string ResolveDotnet()
    {
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            var candidate = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return "dotnet";
    }

    public static SandboxWorkerLaunch ResolveOsContained(
        string workerPath,
        SandboxWorkerOptions options,
        string dotnetPath,
        string? linuxSandboxScript)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Windows AppContainer launch is not wired yet; OS containment is Linux-only in this build.");
        }

        if (string.IsNullOrWhiteSpace(linuxSandboxScript) || !File.Exists(linuxSandboxScript))
        {
            throw new FileNotFoundException("Linux sandbox script was not found.", linuxSandboxScript);
        }

        var unshare = FindUnshare()
            ?? throw new PlatformNotSupportedException("unshare was not found; OS containment is unavailable.");

        return new SandboxWorkerLaunch(unshare,
        [
            "--user",
            "--map-root-user",
            "--net",
            "--pid",
            "--mount",
            "--fork",
            "/bin/bash",
            linuxSandboxScript,
            dotnetPath,
            workerPath
        ]);
    }

    public static string? FindUnshare()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        foreach (var candidate in new[] { "/usr/bin/unshare", "/bin/unshare" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string? FindPrlimit()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        foreach (var candidate in new[] { "/usr/bin/prlimit", "/bin/prlimit" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
