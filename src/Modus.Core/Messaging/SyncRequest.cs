namespace Modus.Core.Messaging;

using Modus.Core.Plugins;

public sealed record SyncRequest
{
	public SyncRequest(
		OperationName Operation,
		bool IsFallbackExplicit,
		SyncFallbackReason FallbackReason = SyncFallbackReason.None,
		string? FallbackReasonCode = null,
		CorrelationId? CorrelationId = null)
	{
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

	public OperationName Operation { get; }

	public bool IsFallbackExplicit { get; }

	public SyncFallbackReason FallbackReason { get; }

	public string? FallbackReasonCode { get; }

	public CorrelationId? CorrelationId { get; }

	public static SyncRequest ForExplicitFallback(
		OperationName operation,
		SyncFallbackReason fallbackReason,
		string fallbackReasonCode,
		CorrelationId? correlationId = null)
	{
		return new SyncRequest(operation, IsFallbackExplicit: true, fallbackReason, fallbackReasonCode, correlationId);
	}

	public static SyncRequest ForStandardPath(OperationName operation, CorrelationId? correlationId = null)
	{
		return new SyncRequest(operation, IsFallbackExplicit: false, SyncFallbackReason.None, null, correlationId);
	}
}
