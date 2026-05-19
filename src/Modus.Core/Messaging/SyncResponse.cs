namespace Modus.Core.Messaging;

public sealed record SyncResponse
{
	public SyncResponse(
		bool Success,
		string Payload,
		SyncResponseStatus? Status = null,
		bool ServedFromFallback = false,
		string? CorrelationId = null)
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

	public string Payload { get; }

	public SyncResponseStatus Status { get; }

	public bool ServedFromFallback { get; }

	public string? CorrelationId { get; }
}
