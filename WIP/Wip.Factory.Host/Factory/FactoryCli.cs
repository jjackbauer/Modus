using System.Globalization;
using System.Text.Json;

namespace Wip.Factory.Host.Factory;

/// <summary>
/// The operator CLI (v0.3 §8). It is a thin adapter over <see cref="FactoryRuntime"/>: no business rules,
/// no interactive prompts, stable exit codes and machine-readable output.
/// </summary>
public static class FactoryCli
{
    public const int Ok = 0;
    public const int Refused = 2;
    public const int Usage = 64;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<int> ExecuteAsync(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);

        var verb = args.Length > 1 ? args[1] : string.Empty;
        var rest = args.Length > 2 ? args[2..] : [];
        var json = HasFlag(rest, "--json");

        return verb switch
        {
            "requirement" => await RequirementAsync(rest, output, error).ConfigureAwait(false),
            "contract" => await ContractAsync(rest, output, error).ConfigureAwait(false),
            "run" => await RunAsync(rest, output, error, json).ConfigureAwait(false),
            "status" => await StatusAsync(rest, output, error, json).ConfigureAwait(false),
            "approve" => await ApproveAsync(rest, output, error).ConfigureAwait(false),
            "serve" => await ServeAsync(rest, output, error).ConfigureAwait(false),
            _ => await FailAsync(error, Usage, "usage: factory requirement|contract|run|status").ConfigureAwait(false)
        };
    }

    private static async Task<int> RequirementAsync(string[] rest, TextWriter output, TextWriter error)
    {
        if (rest.Length == 0 || !string.Equals(rest[0], "submit", StringComparison.Ordinal))
        {
            return await FailAsync(error, Usage, "usage: factory requirement submit --file <req.md> --repo <dir>").ConfigureAwait(false);
        }

        var file = Value(rest, "--file");
        var repo = Value(rest, "--repo");
        var title = Value(rest, "--title");
        var controlRootValue = Value(rest, "--control-root");

        if (file is null || repo is null)
        {
            return await FailAsync(error, Usage, "usage: factory requirement submit --file <req.md> --repo <dir>").ConfigureAwait(false);
        }

        if (!File.Exists(file))
        {
            return await FailAsync(error, Refused, $"requirement-file-not-found:{file}").ConfigureAwait(false);
        }

        var (root, rootError) = FactoryControlRoot.Resolve(repo, controlRootValue);
        if (root is null)
        {
            return await FailAsync(error, Refused, rootError!).ConfigureAwait(false);
        }

        var text = await File.ReadAllTextAsync(file).ConfigureAwait(false);

        // A requirement may point at a structured contract; copy it at intake so nothing external is read later.
        string? contractJson = null;
        var (parsed, _) = RequirementDocument.Parse(text, title);
        if (parsed?.AcceptanceContractReference is { Length: > 0 } reference)
        {
            var contractPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, reference));
            if (!File.Exists(contractPath))
            {
                return await FailAsync(error, Refused, "acceptance-contract-not-found:" + reference).ConfigureAwait(false);
            }

            contractJson = await File.ReadAllTextAsync(contractPath).ConfigureAwait(false);
        }

        var result = await new FactoryRuntime(root).SubmitAsync(text, title, contractJson).ConfigureAwait(false);

        if (!result.Accepted)
        {
            await error.WriteLineAsync("refused: " + string.Join("; ", result.Errors)).ConfigureAwait(false);
            return Refused;
        }

        await output.WriteLineAsync($"work item        : {result.WorkItemId}").ConfigureAwait(false);
        await output.WriteLineAsync($"state            : {result.State}").ConfigureAwait(false);
        await output.WriteLineAsync($"requirement digest: {result.RequirementDigest}").ConfigureAwait(false);
        await output.WriteLineAsync($"control root     : {root.Root}").ConfigureAwait(false);
        await output.WriteLineAsync("next             : factory run --until-idle").ConfigureAwait(false);
        return Ok;
    }

    private static async Task<int> ContractAsync(string[] rest, TextWriter output, TextWriter error)
    {
        var action = rest.Length > 0 ? rest[0] : string.Empty;
        var workItem = Value(rest, "--work-item");
        var digest = Value(rest, "--digest");
        var repo = Value(rest, "--repo") ?? Directory.GetCurrentDirectory();
        var controlRootValue = Value(rest, "--control-root");

        if (workItem is null)
        {
            return await FailAsync(error, Usage, "usage: factory contract show|approve --work-item <id> --repo <dir>").ConfigureAwait(false);
        }

        var (root, rootError) = FactoryControlRoot.Resolve(repo, controlRootValue);
        if (root is null)
        {
            return await FailAsync(error, Refused, rootError!).ConfigureAwait(false);
        }

        var runtime = new FactoryRuntime(root);

        if (string.Equals(action, "show", StringComparison.Ordinal))
        {
            var (proposal, errorText) = await runtime.ContractShowAsync(workItem).ConfigureAwait(false);
            if (proposal is null)
            {
                return await FailAsync(error, Refused, errorText ?? "no-contract-proposal").ConfigureAwait(false);
            }

            await output.WriteLineAsync($"proposal digest : {proposal.ProposalDigest}").ConfigureAwait(false);
            await output.WriteLineAsync($"requirement     : {proposal.RequirementDigest}").ConfigureAwait(false);
            await output.WriteLineAsync($"executable      : {proposal.Executable.ToString().ToLowerInvariant()} (HTTP form lands with the acceptance milestone)").ConfigureAwait(false);
            await output.WriteLineAsync("scenarios:").ConfigureAwait(false);
            foreach (var scenario in proposal.Scenarios)
            {
                await output.WriteLineAsync($"  {scenario.Id}  {scenario.Expectation}").ConfigureAwait(false);
            }

            await output.WriteLineAsync($"next            : factory contract approve --work-item {workItem} --digest {proposal.ProposalDigest}").ConfigureAwait(false);
            return Ok;
        }

        if (string.Equals(action, "approve", StringComparison.Ordinal))
        {
            if (digest is null)
            {
                return await FailAsync(error, Usage, "usage: factory contract approve --work-item <id> --digest <sha256:...>").ConfigureAwait(false);
            }

            var (ok, approveError) = await runtime.ContractApproveAsync(workItem, digest).ConfigureAwait(false);
            if (!ok)
            {
                return await FailAsync(error, Refused, approveError ?? "contract-approve-refused").ConfigureAwait(false);
            }

            await output.WriteLineAsync($"contract frozen : {digest}").ConfigureAwait(false);
            await output.WriteLineAsync("next            : factory run --until-idle").ConfigureAwait(false);
            return Ok;
        }

        return await FailAsync(error, Usage, "usage: factory contract show|approve").ConfigureAwait(false);
    }

    private static async Task<int> ServeAsync(string[] rest, TextWriter output, TextWriter error)
    {
        var workItem = Value(rest, "--work-item");
        var repo = Value(rest, "--repo") ?? Directory.GetCurrentDirectory();
        var controlRootValue = Value(rest, "--control-root");
        var durationText = Value(rest, "--duration-seconds");

        if (workItem is null)
        {
            return await FailAsync(error, Usage, "usage: factory serve --work-item <id> --repo <dir> [--duration-seconds N]").ConfigureAwait(false);
        }

        var (root, rootError) = FactoryControlRoot.Resolve(repo, controlRootValue);
        if (root is null)
        {
            return await FailAsync(error, Refused, rootError!).ConfigureAwait(false);
        }

        var duration = int.TryParse(durationText, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(30);

        var runtime = new FactoryRuntime(root, new RealModelImplementationRunner(root, new Uri("http://127.0.0.1:1/"), "unused"), new DotnetRunCandidateHost(), new DotnetBuildVerifier(), new GitMergeEngine());
        var result = await runtime.ServeAsync(workItem, duration).ConfigureAwait(false);

        if (!result.Ok)
        {
            return await FailAsync(error, Refused, result.Error ?? "serve-failed").ConfigureAwait(false);
        }

        await output.WriteLineAsync($"serving          : {result.Url}").ConfigureAwait(false);
        await output.WriteLineAsync($"process id       : {result.ProcessId}").ConfigureAwait(false);
        await output.WriteLineAsync($"supervised for   : {duration.TotalSeconds}s (process stopped on exit)").ConfigureAwait(false);
        return Ok;
    }

    private static async Task<int> ApproveAsync(string[] rest, TextWriter output, TextWriter error)
    {
        var workItem = Value(rest, "--work-item");
        var digest = Value(rest, "--review-digest");
        var repo = Value(rest, "--repo") ?? Directory.GetCurrentDirectory();
        var controlRootValue = Value(rest, "--control-root");

        if (workItem is null || digest is null)
        {
            return await FailAsync(error, Usage, "usage: factory approve --work-item <id> --review-digest <sha256:...> --repo <dir>").ConfigureAwait(false);
        }

        var (root, rootError) = FactoryControlRoot.Resolve(repo, controlRootValue);
        if (root is null)
        {
            return await FailAsync(error, Refused, rootError!).ConfigureAwait(false);
        }

        var (ok, approveError) = await new FactoryRuntime(root).ApproveMergeAsync(workItem, digest).ConfigureAwait(false);
        if (!ok)
        {
            return await FailAsync(error, Refused, approveError ?? "approve-refused").ConfigureAwait(false);
        }

        await output.WriteLineAsync($"merge authorised : {digest}").ConfigureAwait(false);
        await output.WriteLineAsync("next             : factory run --until-idle").ConfigureAwait(false);
        return Ok;
    }

    private static async Task<int> RunAsync(string[] rest, TextWriter output, TextWriter error, bool json)
    {
        var repo = Value(rest, "--repo") ?? Directory.GetCurrentDirectory();
        var controlRootValue = Value(rest, "--control-root");

        var (root, rootError) = FactoryControlRoot.Resolve(repo, controlRootValue);
        if (root is null)
        {
            return await FailAsync(error, Refused, rootError!).ConfigureAwait(false);
        }

        var runner = TryCreateImplementationRunner(root, error);
        var runtime = new FactoryRuntime(root, runner, new DotnetRunCandidateHost(), new DotnetBuildVerifier(), new GitMergeEngine());
        var (reports, lockError) = await runtime.RunExclusiveAsync().ConfigureAwait(false);
        if (lockError is not null)
        {
            return await FailAsync(error, Refused, lockError).ConfigureAwait(false);
        }
        var statuses = await runtime.StatusAsync().ConfigureAwait(false);

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(new { passes = reports, workItems = statuses }, Json)).ConfigureAwait(false);
            return Ok;
        }

        foreach (var (report, index) in reports.Select(static (r, i) => (r, i)))
        {
            await output.WriteLineAsync(
                $"pass {index + 1}: claimed={report.Claimed} dispatched={report.Dispatched} blocked={report.Blocked} duplicates={report.Duplicates} skipped={report.Skipped} failed={report.Failed}").ConfigureAwait(false);
        }

        await WriteStatusAsync(output, statuses).ConfigureAwait(false);

        var waiting = statuses.Count(static s => string.Equals(s.State, "Proposed", StringComparison.Ordinal));
        if (waiting > 0)
        {
            await output.WriteLineAsync("waiting-for-contract-approval").ConfigureAwait(false);
            return Ok;
        }

        return Ok;
    }

    private static async Task<int> StatusAsync(string[] rest, TextWriter output, TextWriter error, bool json)
    {
        var repo = Value(rest, "--repo") ?? Directory.GetCurrentDirectory();
        var controlRootValue = Value(rest, "--control-root");

        var (root, rootError) = FactoryControlRoot.Resolve(repo, controlRootValue);
        if (root is null)
        {
            return await FailAsync(error, Refused, rootError!).ConfigureAwait(false);
        }

        var statuses = await new FactoryRuntime(root).StatusAsync().ConfigureAwait(false);

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(statuses, Json)).ConfigureAwait(false);
            return Ok;
        }

        await WriteStatusAsync(output, statuses).ConfigureAwait(false);
        return Ok;
    }

    private static async Task WriteStatusAsync(TextWriter output, IReadOnlyList<WorkItemStatus> statuses)
    {
        if (statuses.Count == 0)
        {
            await output.WriteLineAsync("no work items").ConfigureAwait(false);
            return;
        }

        await output.WriteLineAsync("work item        state                 v  requirement digest").ConfigureAwait(false);
        foreach (var status in statuses)
        {
            var digest = status.FrozenContractDigest ?? status.ProposalDigest ?? status.RequirementDigest;
            await output.WriteLineAsync(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-16} {1,-20} {2,-3}{3}",
                status.WorkItemId,
                status.State,
                status.Version,
                digest)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds the real implementation runner from the configured provider. Without credentials the lifecycle
    /// still runs, and the implementation stage fails with a named category rather than pretending.
    /// </summary>
    private static RealModelImplementationRunner? TryCreateImplementationRunner(FactoryControlRoot root, TextWriter error)
    {
        var (baseUrl, apiKey) = DeepSeekCredentials.Load();
        if (baseUrl is null || apiKey is null)
        {
            error.WriteLineAsync("note: no model credentials found; implementation stages will fail with implementation-runner-unavailable").GetAwaiter().GetResult();
            return null;
        }

        return new RealModelImplementationRunner(root, baseUrl, apiKey);
    }

    private static string? Value(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool HasFlag(string[] args, string flag)
        => args.Any(a => string.Equals(a, flag, StringComparison.Ordinal));

    private static async Task<int> FailAsync(TextWriter error, int code, string message)
    {
        await error.WriteLineAsync(message).ConfigureAwait(false);
        return code;
    }
}
