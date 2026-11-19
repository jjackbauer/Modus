using System.Security.Cryptography;
using System.Text;
using Wip.Factory.Core.Io;

namespace Wip.Factory.Host.Factory;

/// <summary>
/// One durable post-merge verification fact (v0.4 FAC-P0-01). It is deliberately separate from the merge
/// operation: an applied merge proves Git changed, while this record proves the exact merged revision passed the
/// build, the existing regression suite and the frozen contract.
/// </summary>
public sealed record ReleaseVerificationRecord(
    string WorkItemId,
    string MergeOperationId,
    string ApprovedHeadSha,
    string BaseSha,
    string MergedSha,
    string? MergedTreeSha,
    string ContractDigest,
    string BuildEvidenceDigest,
    string RegressionEvidenceDigest,
    string ContractEvidenceDigest,
    string ScenarioSummary,
    string VerifierVersion,
    string Outcome,
    string Detail,
    DateTimeOffset VerifiedAtUtc)
{
    public const string VerifiedOutcome = "verified";
    public const string FailedOutcome = "failed";

    public bool Verified => string.Equals(Outcome, VerifiedOutcome, StringComparison.Ordinal);

    /// <summary>Binds the evidence to every fact it depends on, so a stale record cannot bless a new tree.</summary>
    public string RecordDigest => FactoryStageDispatchHandler.Digest(string.Join(
        '\u001f',
        WorkItemId,
        MergeOperationId,
        ApprovedHeadSha,
        BaseSha,
        MergedSha,
        MergedTreeSha ?? string.Empty,
        ContractDigest,
        BuildEvidenceDigest,
        RegressionEvidenceDigest,
        ContractEvidenceDigest,
        ScenarioSummary,
        VerifierVersion,
        Outcome,
        VerifiedAtUtc.ToString("O")));
}

/// <summary>Durable release-verification journal. Evidence is flushed before the release state is published.</summary>
public sealed class ReleaseVerificationStore
{
    private readonly DurableJournal<ReleaseVerificationRecord> _journal;
    private readonly string _lockPath;

    public ReleaseVerificationStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _journal = new DurableJournal<ReleaseVerificationRecord>(path);
        _lockPath = path + ".lock";
    }

    public string Path => _journal.Path;

    public void Append(ReleaseVerificationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var lease = CrossProcessFileLock
            .AcquireAsync(_lockPath, TimeSpan.FromSeconds(30))
            .AsTask()
            .GetAwaiter()
            .GetResult();

        _journal.Append(record);
    }

    /// <summary>The matching verification record for exactly this merged revision and contract, or null.</summary>
    public ReleaseVerificationRecord? Find(
        string workItemId,
        string mergedSha,
        string contractDigest,
        string approvedHeadSha,
        string baseSha)
    {
        using var lease = CrossProcessFileLock
            .AcquireAsync(_lockPath, TimeSpan.FromSeconds(30))
            .AsTask()
            .GetAwaiter()
            .GetResult();

        ReleaseVerificationRecord? found = null;
        foreach (var record in _journal.ReadAll())
        {
            if (string.Equals(record.WorkItemId, workItemId, StringComparison.Ordinal)
                && string.Equals(record.MergedSha, mergedSha, StringComparison.Ordinal)
                && string.Equals(record.ContractDigest, contractDigest, StringComparison.Ordinal)
                && string.Equals(record.ApprovedHeadSha, approvedHeadSha, StringComparison.Ordinal)
                && string.Equals(record.BaseSha, baseSha, StringComparison.Ordinal))
            {
                found = record;
            }
        }

        return found;
    }

    public IReadOnlyList<ReleaseVerificationRecord> ReadAll()
    {
        using var lease = CrossProcessFileLock
            .AcquireAsync(_lockPath, TimeSpan.FromSeconds(30))
            .AsTask()
            .GetAwaiter()
            .GetResult();

        return _journal.ReadAll();
    }
}

/// <summary>What to verify: the exact merged revision, the approved baseline and the frozen contract.</summary>
public sealed record ReleaseVerificationRequest(
    string WorkItemId,
    string Repository,
    string MergedSha,
    string ApprovedHeadSha,
    string BaseSha,
    string MergeOperationId,
    AcceptanceContract Contract);

/// <summary>Outcome of executing the post-merge checks, with a digest for each mandatory evidence class.</summary>
public sealed record ReleaseVerificationResult(
    bool Ok,
    string Detail,
    string BuildEvidenceDigest,
    string RegressionEvidenceDigest,
    string ContractEvidenceDigest,
    string ScenarioSummary,
    string VerifierVersion,
    string? MergedTreeSha)
{
    public static ReleaseVerificationResult Failed(string detail, string verifierVersion)
        => new(false, detail, string.Empty, string.Empty, string.Empty, string.Empty, verifierVersion, null);
}

/// <summary>Runs build, existing regressions and the frozen contract against exactly the merged revision.</summary>
public interface IReleaseVerifier
{
    ValueTask<ReleaseVerificationResult> VerifyAsync(
        ReleaseVerificationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The production verifier. It materialises the recorded merged revision — never the agent worktree — then runs the
/// three mandatory checks. It never trusts an agent assertion or a pre-merge result (v0.4 FAC-P0-01.3).
/// </summary>
public sealed class PostMergeReleaseVerifier : IReleaseVerifier
{
    public const string VerifierVersion = "post-merge-verifier@1";

    private readonly IMergeEngine _merges;
    private readonly ICandidateBuildVerifier _builds;
    private readonly IRegressionTestRunner _regressions;
    private readonly ICandidateHost _candidates;
    private readonly AcceptanceContractRunner _contracts;
    private readonly string _verificationRoot;

    public PostMergeReleaseVerifier(
        IMergeEngine merges,
        ICandidateBuildVerifier builds,
        IRegressionTestRunner regressions,
        ICandidateHost candidates,
        AcceptanceContractRunner contracts,
        string verificationRoot)
    {
        _merges = merges ?? throw new ArgumentNullException(nameof(merges));
        _builds = builds ?? throw new ArgumentNullException(nameof(builds));
        _regressions = regressions ?? throw new ArgumentNullException(nameof(regressions));
        _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        _verificationRoot = verificationRoot;
    }

    public async ValueTask<ReleaseVerificationResult> VerifyAsync(
        ReleaseVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var destination = Path.Combine(
            _verificationRoot,
            "merged-" + request.MergedSha[..Math.Min(12, request.MergedSha.Length)]);

        string revisionPath;
        try
        {
            revisionPath = await _merges
                .MaterialiseAsync(request.Repository, request.MergedSha, destination, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ReleaseVerificationResult.Failed("merged-revision-unavailable:" + exception.Message, VerifierVersion);
        }

        var treeSha = await _merges.ResolveTreeAsync(request.Repository, request.MergedSha, cancellationToken).ConfigureAwait(false);

        var (buildOk, buildDetail) = await _builds.VerifyAsync(revisionPath, cancellationToken).ConfigureAwait(false);
        var buildDigest = FactoryStageDispatchHandler.Digest("build|" + buildOk + "|" + buildDetail);

        if (!buildOk)
        {
            return new ReleaseVerificationResult(
                false,
                "build-failed:" + buildDetail,
                buildDigest,
                string.Empty,
                string.Empty,
                string.Empty,
                VerifierVersion,
                treeSha);
        }

        var regression = await _regressions.RunAsync(revisionPath, cancellationToken).ConfigureAwait(false);
        if (!regression.Passed)
        {
            return new ReleaseVerificationResult(
                false,
                "regression-failed:" + regression.Detail,
                buildDigest,
                regression.ArtifactDigest,
                string.Empty,
                string.Empty,
                VerifierVersion,
                treeSha);
        }

        var session = CandidateSession.None;
        try
        {
            session = await _candidates.StartAsync(revisionPath, cancellationToken).ConfigureAwait(false);
            if (!session.IdentityVerified)
            {
                return new ReleaseVerificationResult(
                    false,
                    "candidate-identity-unverified",
                    buildDigest,
                    regression.ArtifactDigest,
                    string.Empty,
                    string.Empty,
                    VerifierVersion,
                    treeSha);
            }

            var contract = await _contracts
                .RunAsync(request.Contract, session.BaseAddress, cancellationToken)
                .ConfigureAwait(false);

            var scenarios = string.Join(",", contract.Scenarios.Select(static scenario => scenario.Id + "=" + (scenario.Passed ? "pass" : "fail")));
            var contractDigest = FactoryStageDispatchHandler.Digest(
                request.Contract.Digest + "|" + scenarios + "|" + contract.Scenarios.Select(static scenario => scenario.ResponseDigest).Aggregate(string.Empty, static (left, right) => left + "|" + right));

            var detail = contract.Passed
                ? "post-merge-contract-passed"
                : "post-merge-contract-failed:" + contract.FailureSummary;

            return new ReleaseVerificationResult(
                contract.Passed,
                detail,
                buildDigest,
                regression.ArtifactDigest,
                contractDigest,
                scenarios,
                VerifierVersion,
                treeSha);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ReleaseVerificationResult(
                false,
                "post-merge-verification-error:" + exception.Message,
                buildDigest,
                regression.ArtifactDigest,
                string.Empty,
                string.Empty,
                VerifierVersion,
                treeSha);
        }
        finally
        {
            await session.Stop().ConfigureAwait(false);
        }
    }
}

/// <summary>Evidence helpers shared by the release path.</summary>
public static class ReleaseEvidence
{
    public static string Digest(string text)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
