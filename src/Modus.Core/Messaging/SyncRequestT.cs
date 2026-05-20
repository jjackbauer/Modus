namespace Modus.Core.Messaging;

using Modus.Core.Plugins;

public sealed record SyncRequest<TPayload>
{
	public SyncRequest(
		OperationName Operation,
		TPayload Payload,
		bool IsFallbackExplicit,
		SyncFallbackReason FallbackReason = SyncFallbackReason.None,
		string? FallbackReasonCode = null,
		CorrelationId? CorrelationId = null)
	{
		if (Payload is null)
		{
			throw new ArgumentNullException(nameof(Payload));
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
		this.Payload = Payload;
		this.IsFallbackExplicit = IsFallbackExplicit;
		this.FallbackReason = FallbackReason;
		this.FallbackReasonCode = FallbackReasonCode;
		this.CorrelationId = CorrelationId;
	}

	public OperationName Operation { get; }

	public TPayload Payload { get; }

	public bool IsFallbackExplicit { get; }

	public SyncFallbackReason FallbackReason { get; }

	public string? FallbackReasonCode { get; }

	public CorrelationId? CorrelationId { get; }

	public static SyncRequest<TPayload> ForStandardPath(
		OperationName operation,
		TPayload payload,
		CorrelationId? correlationId = null)
		=> new SyncRequest<TPayload>(operation, payload, IsFallbackExplicit: false, SyncFallbackReason.None, null, correlationId);

	public static SyncRequest<TPayload> ForExplicitFallback(
		OperationName operation,
		TPayload payload,
		SyncFallbackReason fallbackReason,
		string fallbackReasonCode,
		CorrelationId? correlationId = null)
		=> new SyncRequest<TPayload>(operation, payload, IsFallbackExplicit: true, fallbackReason, fallbackReasonCode, correlationId);
}
