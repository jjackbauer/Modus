using System.Globalization;
using Wip.Factory.Host.Factory;

namespace Wip.Factory.ControlApi;

/// <summary>How the factory's implementation stage is driven. The adapter never invents an execution path.</summary>
public enum ImplementationMode
{
    /// <summary>The stage fails with <c>implementation-runner-unavailable</c>.</summary>
    None = 0,
    /// <summary>The real DeepSeek-backed implementation runner.</summary>
    DeepSeek = 1,
    /// <summary>
    /// A deterministic local runner used for engineering verification and offline demonstration. It performs real
    /// worktree changes, a real build and a real commit, but it is not a model and its evidence is labelled as such.
    /// </summary>
    DeterministicDemo = 2
}

/// <summary>Immutable adapter options, resolved once at startup.</summary>
public sealed record ControlApiOptions
{
    public required FactoryControlRoot Factory { get; init; }
    public required string Token { get; init; }
    public required ImplementationMode Implementation { get; init; }
    public required bool ExecutionEnabled { get; init; }
    public required string AdapterVersion { get; init; }
    public required string BindAddress { get; init; }
    public required int MaxServeSeconds { get; init; }
    public required int MaxDiffBytes { get; init; }
    public required int MaxFileBytes { get; init; }
    public required int MaxEventDataLength { get; init; }
    public required string AuditDirectory { get; init; }
    public required Uri? ModelBaseUrl { get; init; }
    public required string? ModelApiKey { get; init; }

    public string InstanceId
    {
        get
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Factory.Root.ToLowerInvariant()));
            return "factory-" + Convert.ToHexString(bytes)[..12].ToLowerInvariant();
        }
    }

    public string Label => "Wip.Factory @ " + Path.GetFileName(Factory.TargetRepository.TrimEnd(Path.DirectorySeparatorChar));

    public static (ControlApiOptions? Options, IReadOnlyList<string> Errors) FromEnvironment(string[] args)
    {
        var errors = new List<string>();

        var repository = Argument(args, "--repo")
            ?? Environment.GetEnvironmentVariable("WIP_FACTORY_REPO");
        var controlRoot = Argument(args, "--control-root")
            ?? Environment.GetEnvironmentVariable("WIP_FACTORY_CONTROL_ROOT");
        var token = Argument(args, "--token")
            ?? Environment.GetEnvironmentVariable("WIP_FACTORY_CONTROL_TOKEN");
        var modeText = Argument(args, "--implementation")
            ?? Environment.GetEnvironmentVariable("WIP_FACTORY_IMPLEMENTATION")
            ?? "auto";
        var bind = Argument(args, "--bind")
            ?? Environment.GetEnvironmentVariable("WIP_CONTROL_BIND")
            ?? "127.0.0.1:5099";

        if (string.IsNullOrWhiteSpace(repository))
        {
            errors.Add("missing-repository: pass --repo <dir> or WIP_FACTORY_REPO");
        }

        if (string.IsNullOrWhiteSpace(token) || token.Length < 24)
        {
            errors.Add("missing-token: pass --token <secret> or WIP_FACTORY_CONTROL_TOKEN (minimum 24 characters)");
        }

        var execution = !string.Equals(
            Environment.GetEnvironmentVariable("WIP_FACTORY_ENABLE_EXECUTION"),
            "0",
            StringComparison.Ordinal);
        if (Argument(args, "--no-execution") is not null)
        {
            execution = false;
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var (root, rootError) = FactoryControlRoot.Resolve(repository!, controlRoot);
        if (root is null)
        {
            return (null, [rootError ?? "control-root-invalid"]);
        }

        var (baseUrl, apiKey) = DeepSeekCredentials.Load();
        var mode = ParseMode(modeText, baseUrl, apiKey, errors);
        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var options = new ControlApiOptions
        {
            Factory = root,
            Token = token!,
            Implementation = mode,
            ExecutionEnabled = execution,
            AdapterVersion = typeof(ControlApiOptions).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            BindAddress = bind,
            MaxServeSeconds = ParseInt(Environment.GetEnvironmentVariable("WIP_CONTROL_MAX_SERVE_SECONDS"), 300, 10, 3600),
            MaxDiffBytes = ParseInt(Environment.GetEnvironmentVariable("WIP_CONTROL_MAX_DIFF_BYTES"), 512 * 1024, 4096, 8 * 1024 * 1024),
            MaxFileBytes = ParseInt(Environment.GetEnvironmentVariable("WIP_CONTROL_MAX_FILE_BYTES"), 256 * 1024, 1024, 4 * 1024 * 1024),
            MaxEventDataLength = ParseInt(Environment.GetEnvironmentVariable("WIP_CONTROL_MAX_EVENT_DATA"), 4000, 256, 100_000),
            AuditDirectory = Path.Combine(root.Root, "control-api"),
            ModelBaseUrl = baseUrl,
            ModelApiKey = apiKey
        };

        return (options, []);
    }

    private static ImplementationMode ParseMode(
        string text,
        Uri? baseUrl,
        string? apiKey,
        List<string> errors)
    {
        var normalized = text.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "none":
                return ImplementationMode.None;
            case "demo":
            case "deterministic-demo":
                return ImplementationMode.DeterministicDemo;
            case "deepseek":
                if (baseUrl is null || apiKey is null)
                {
                    errors.Add("implementation-mode-unavailable: deepseek selected without DEEPSEEK_BASE_URL and DEEPSEEK_API_KEY");
                    return ImplementationMode.None;
                }

                return ImplementationMode.DeepSeek;
            case "auto":
                return baseUrl is not null && apiKey is not null
                    ? ImplementationMode.DeepSeek
                    : ImplementationMode.None;
            default:
                errors.Add("implementation-mode-invalid:" + text);
                return ImplementationMode.None;
        }
    }

    private static string? Argument(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return args.Contains(name, StringComparer.Ordinal) ? "1" : null;
    }

    private static int ParseInt(string? text, int fallback, int minimum, int maximum)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    public string ImplementationRunnerName => Implementation switch
    {
        ImplementationMode.DeepSeek => "deepseek-real-model",
        ImplementationMode.DeterministicDemo => "deterministic-demo",
        _ => "unavailable"
    };
}
