using System.Text;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Evaluation;

/// <summary>The protected evaluation purpose a seed domain belongs to.</summary>
public enum SeedPurpose
{
    Training = 0,
    Selection = 1,
    Holdout = 2,
    Audit = 3,
}

/// <summary>Refused because two protected seed domains share a concrete seed.</summary>
public sealed class SeedDomainOverlapException : InvalidOperationException
{
    public SeedDomainOverlapException(string leftId, string rightId, ulong seed)
        : base($"Seed domains '{leftId}' and '{rightId}' share the concrete seed {seed}.")
    {
        LeftDomainId = leftId;
        RightDomainId = rightId;
        Seed = seed;
    }

    public string LeftDomainId { get; }

    public string RightDomainId { get; }

    public ulong Seed { get; }
}

/// <summary>
/// A protected block of evaluation seeds. Every seed is derived from the domain's base seed, its
/// purpose salt and its one-based block index, so the block is reproducible from data alone. The
/// seeds are materialized once; <see cref="Seeds"/> returns a copy, and the domain is content
/// addressed by its canonical JSON. There is no clock, GUID, culture or ambient randomness.
/// </summary>
public sealed record SeedDomain
{
    private const string Schema = "tholdem.seed-domain.v1";

    private readonly ulong[] seeds;

    public SeedDomain(string domainId, SeedPurpose purpose, ulong baseSeed, int count)
    {
        if (string.IsNullOrWhiteSpace(domainId))
        {
            throw new ArgumentException("A seed domain requires a non-empty identifier.", nameof(domainId));
        }

        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                "A seed domain must contain at least one seed.");
        }

        DomainId = domainId;
        Purpose = purpose;
        BaseSeed = baseSeed;
        Count = count;

        var salt = PurposeSalt(purpose);
        seeds = new ulong[count];
        for (var index = 0; index < count; index++)
        {
            seeds[index] = SeedDerivation.Derive(baseSeed, salt, index + 1, 0, 0);
        }
    }

    public string DomainId { get; }

    public SeedPurpose Purpose { get; }

    public ulong BaseSeed { get; }

    public int Count { get; }

    /// <summary>A defensive copy of the materialized seeds.</summary>
    public IReadOnlyList<ulong> Seeds => (ulong[])seeds.Clone();

    /// <summary>The fixed-key-order, culture-free JSON the domain hash covers.</summary>
    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(128 + (Count * 21));
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, Schema);
        builder.Append(",\"domainId\":");
        CanonicalJsonWriter.Text(builder, DomainId);
        builder.Append(",\"purpose\":");
        CanonicalJsonWriter.Text(builder, Purpose.ToString());
        builder.Append(",\"baseSeed\":");
        CanonicalJsonWriter.Number(builder, BaseSeed);
        builder.Append(",\"count\":");
        CanonicalJsonWriter.Number(builder, Count);
        builder.Append(",\"seeds\":[");
        for (var index = 0; index < seeds.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.Number(builder, seeds[index]);
        }

        builder.Append("]}");
        return builder.ToString();
    }

    /// <summary>The content address of the canonical domain.</summary>
    public string DomainHash => StrategyArtifact.ComputeHash(ToCanonicalJson());

    /// <summary>
    /// Refuses any two domains that share a concrete seed, naming the first overlapping pair in
    /// declaration order and the shared seed.
    /// </summary>
    public static void ValidateDisjoint(IEnumerable<SeedDomain> domains)
    {
        ArgumentNullException.ThrowIfNull(domains);

        var previous = new List<SeedDomain>();
        foreach (var domain in domains)
        {
            ArgumentNullException.ThrowIfNull(domain);
            foreach (var earlier in previous)
            {
                var earlierSeeds = new HashSet<ulong>(earlier.Seeds);
                foreach (var seed in domain.Seeds)
                {
                    if (earlierSeeds.Contains(seed))
                    {
                        throw new SeedDomainOverlapException(
                            earlier.DomainId,
                            domain.DomainId,
                            seed);
                    }
                }
            }

            previous.Add(domain);
        }
    }

    private static int PurposeSalt(SeedPurpose purpose) => purpose switch
    {
        SeedPurpose.Training => 1,
        SeedPurpose.Selection => 2,
        SeedPurpose.Holdout => 3,
        SeedPurpose.Audit => 4,
        _ => throw new ArgumentOutOfRangeException(
            nameof(purpose),
            purpose,
            "Unknown seed purpose."),
    };
}
