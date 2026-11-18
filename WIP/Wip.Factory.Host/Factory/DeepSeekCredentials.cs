using System.Text;

namespace Wip.Factory.Host.Factory;

/// <summary>
/// Reads the configured model provider from the environment or the repository's local secret file. It never
/// prints or logs the key, and it reports "not configured" rather than guessing.
/// </summary>
public static class DeepSeekCredentials
{
    public static (Uri? BaseUrl, string? ApiKey) Load()
    {
        var key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        var url = Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL");

        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(url))
        {
            return (new Uri(url), key);
        }

        foreach (var candidate in CandidateFiles())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var (fileKey, fileUrl) = Parse(File.ReadLines(candidate));
            if (fileKey is not null && fileUrl is not null)
            {
                return (new Uri(fileUrl), fileKey);
            }
        }

        return (null, null);
    }

    private static IEnumerable<string> CandidateFiles()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            yield return Path.Combine(directory.FullName, ".secrets", "deepseek.env");
            directory = directory.Parent;
        }
    }

    private static (string? Key, string? Url) Parse(IEnumerable<string> lines)
    {
        string? key = null;
        string? url = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"').Trim('\'');

            if (string.Equals(name, "DEEPSEEK_API_KEY", StringComparison.Ordinal))
            {
                key = value;
            }
            else if (string.Equals(name, "DEEPSEEK_BASE_URL", StringComparison.Ordinal))
            {
                url = value;
            }
        }

        return (key, url);
    }
}
