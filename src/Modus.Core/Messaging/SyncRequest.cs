namespace Modus.Core.Messaging;

public sealed record SyncRequest
{
	public SyncRequest(
		string Operation,
		bool IsFallbackExplicit,
		SyncFallbackReason FallbackReason = SyncFallbackReason.None,
		string? FallbackReasonCode = null,
		string? CorrelationId = null)
	{
		if (string.IsNullOrWhiteSpace(Operation))
		{
			throw new ArgumentException("Operation is required.", nameof(Operation));
		}

		if (IsFallbackExplicit)
		{
			if (FallbackReason == SyncFallbackReason.None)
			{
				throw new ArgumentException("FallbackReason must be provided for explicit fallback requests.", nameof(FallbackReason));
			}

			if (string.IsNullOrWhiteSpace(FallbackReasonCode))
			{
				throw new ArgumentException("FallbackReasonCode is required for explicit fallback requests.", nameof(FallbackReasonCode));
			}
		}
		else
		{
			if (FallbackReason != SyncFallbackReason.None)
			{
				throw new ArgumentException("FallbackReason must be None when fallback is not explicit.", nameof(FallbackReason));
			}

			if (!string.IsNullOrWhiteSpace(FallbackReasonCode))
			{
				throw new ArgumentException("FallbackReasonCode must be empty when fallback is not explicit.", nameof(FallbackReasonCode));
			}
		}

		this.Operation = Operation;
		this.IsFallbackExplicit = IsFallbackExplicit;
		this.FallbackReason = FallbackReason;
		this.FallbackReasonCode = FallbackReasonCode;
		this.CorrelationId = CorrelationId;
	}

	public string Operation { get; }

	public bool IsFallbackExplicit { get; }

	public SyncFallbackReason FallbackReason { get; }

	public string? FallbackReasonCode { get; }

	public string? CorrelationId { get; }

	public static SyncRequest ForExplicitFallback(
		string operation,
		SyncFallbackReason fallbackReason,
		string fallbackReasonCode,
		string? correlationId = null)
	{
		return new SyncRequest(operation, IsFallbackExplicit: true, fallbackReason, fallbackReasonCode, correlationId);
	}

	public static SyncRequest ForStandardPath(string operation, string? correlationId = null)
	{
		return new SyncRequest(operation, IsFallbackExplicit: false, SyncFallbackReason.None, null, correlationId);
	}
}
