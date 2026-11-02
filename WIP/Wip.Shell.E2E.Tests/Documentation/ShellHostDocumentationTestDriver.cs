using System.Diagnostics;
using Wip.Abstractions.Identifiers;
using Wip.ShellHost.Hosting;

namespace Wip.Shell.E2E.Tests.Documentation;

internal static class ShellHostDocumentationTestDriver
{
    public static async Task<ShellProcessResult> ExecuteScriptAsync(
        IReadOnlyList<string> commands,
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        var shellHostAssemblyPath = typeof(WipShellHost).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(shellHostAssemblyPath)
                ?? throw new InvalidOperationException("Unable to resolve shell host working directory.")
        };
        startInfo.ArgumentList.Add(shellHostAssemblyPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start shell host process.");

        foreach (var command in commands)
        {
            await process.StandardInput.WriteLineAsync(command);
        }

        process.StandardInput.Close();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));

        var stdOutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stdErrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);

        return new ShellProcessResult(
            process.ExitCode,
            await stdOutTask,
            await stdErrTask);
    }

    public static SessionId ExtractSessionId(string stdOut)
    {
        const string prefix = "Session started: ";
        using var reader = new StringReader(stdOut);
        while (reader.ReadLine() is { } line)
        {
            var markerIndex = line.IndexOf(prefix, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                continue;
            }

            var remainder = line[(markerIndex + prefix.Length)..].TrimStart();
            var separatorIndex = remainder.IndexOf(' ');
            var value = separatorIndex >= 0 ? remainder[..separatorIndex] : remainder;
            return new SessionId(value);
        }

        throw new InvalidOperationException($"Could not locate session id in shell output. StdOut: {stdOut}");
    }

    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "Modus.slnx");
            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Modus.slnx.");
    }

    public static string ReadRepositoryFile(string relativePath)
    {
        var filePath = Path.Combine(FindRepositoryRoot(), relativePath);
        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException($"Could not locate repository file '{relativePath}'.");
        }

        return File.ReadAllText(filePath);
    }

    internal sealed record ShellProcessResult(int ExitCode, string StdOut, string StdErr);
}
