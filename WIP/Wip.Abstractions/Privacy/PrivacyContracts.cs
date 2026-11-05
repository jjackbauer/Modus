namespace Wip.Abstractions.Privacy;

/// <summary>
/// WP-5.2 data classification. Secret values never leave the process unredacted; prompts and
/// transcripts are Confidential, artifacts carrying credentials are Secret.
/// </summary>
public enum DataClassification
{
    Public = 0,
    Internal = 1,
    Confidential = 2,
    Secret = 3,
}

public sealed record RedactionResult(string Value, int RedactedCount);

/// <summary>
/// Redacts credentials from strings before they are persisted, logged or shown in status output.
/// Implementations must be deterministic and idempotent.
/// </summary>
public interface ISecretRedactor
{
    RedactionResult Redact(string value);
}

/// <summary>
/// Retention policy for session artifacts. <see cref="MaxArtifactAge"/> is the boundary used by the
/// purge command; a null value keeps artifacts indefinitely and is only valid when the policy is
/// disabled.
/// </summary>
public sealed record RetentionPolicy(TimeSpan? MaxArtifactAge, bool Enabled = true)
{
    public static RetentionPolicy Disabled { get; } = new(null, false);

    public static RetentionPolicy Default { get; } = new(TimeSpan.FromDays(30));

    public RetentionPolicy Validate()
    {
        if (Enabled && MaxArtifactAge is null)
        {
            throw new ArgumentException("An enabled retention policy requires a maximum artifact age.", nameof(MaxArtifactAge));
        }

        if (MaxArtifactAge is { } age && age <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxArtifactAge), "Retention age must be greater than zero.");
        }

        return this;
    }
}

public sealed record RetentionPurgeResult(
    int DeletedFiles,
    long DeletedBytes,
    int RetainedFiles,
    DateTimeOffset PurgedAtUtc);

public sealed record RetentionStatus(
    int SessionDirectories,
    int ArtifactFiles,
    long TotalBytes,
    DateTimeOffset? OldestArtifactUtc);
