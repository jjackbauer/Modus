namespace Modus.Core.Messaging;

public sealed record SyncResponse<TPayload>
{
	public SyncResponse(
		bool Success,
		TPayload Payload,
		SyncResponseStatus? Status = null,
		bool ServedFromFallback = false,
		CorrelationId? CorrelationId = null)
	{
		if (Payload is null)
		{
			throw new ArgumentNullException(nameof(Payload));
		}

		var resolvedStatus = Status ?? (Success ? SyncResponseStatus.Success : SyncResponseStatus.Rejected);
		if (Success && resolvedStatus != SyncResponseStatus.Success)
		{
			throw new ArgumentException("Successful responses must use Success status.", nameof(Status));
		}

		if (!Success && resolvedStatus == SyncResponseStatus.Success)
		{
			throw new ArgumentException("Non-success responses cannot use Success status.", nameof(Status));
		}

		this.Success = Success;
		this.Payload = Payload;
		this.Status = resolvedStatus;
		this.ServedFromFallback = ServedFromFallback;
		this.CorrelationId = CorrelationId;
	}

	public bool Success { get; }

	public TPayload Payload { get; }

	public SyncResponseStatus Status { get; }

	public bool ServedFromFallback { get; }

	public CorrelationId? CorrelationId { get; }

	public static SyncResponse<TPayload> Ok(
		TPayload payload,
		CorrelationId? correlationId = null)
		=> new SyncResponse<TPayload>(Success: true, Payload: payload, Status: SyncResponseStatus.Success, ServedFromFallback: false, CorrelationId: correlationId);

	public static SyncResponse<TPayload> Reject(
		TPayload payload,
		SyncResponseStatus status,
		CorrelationId? correlationId = null)
		=> new SyncResponse<TPayload>(Success: false, Payload: payload, Status: status, ServedFromFallback: false, CorrelationId: correlationId);

	public TPayload PayloadObject => Payload;
}
