namespace Wip.Tholdem.Host;

/// <summary>A command-line usage error: unknown command, unknown option or invalid option value.</summary>
internal sealed class CliUsageException : Exception
{
    public CliUsageException(string message)
        : base(message)
    {
    }
}

/// <summary>A missing or invalid on-disk artifact (strategy, evaluation file or hand record).</summary>
internal sealed class CliArtifactException : Exception
{
    public CliArtifactException(string message)
        : base(message)
    {
    }

    public CliArtifactException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A replay invariant failure: the bytes and the record disagree, or replay diverged.</summary>
internal sealed class CliInvariantException : Exception
{
    public CliInvariantException(string message)
        : base(message)
    {
    }

    public CliInvariantException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
