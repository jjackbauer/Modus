namespace Wip.Tholdem.Engine;

/// <summary>
/// Raised when an action is not legal for the current state. <see cref="Reason"/> is a stable,
/// machine-readable token (for example <c>raise-below-min</c> or <c>hand-complete</c>).
/// </summary>
public sealed class IllegalActionException : InvalidOperationException
{
    public IllegalActionException(string reason, string message)
        : base(message)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An illegal-action reason token is required.", nameof(reason));
        }

        Reason = reason;
    }

    public string Reason { get; }
}
