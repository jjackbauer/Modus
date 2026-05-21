namespace Modus.Core.Messaging;

public sealed record SyncResponse
{
	public SyncResponse(
		bool Success,
		string Payload,
		SyncResponseStatus? Status = null,
		bool ServedFromFallback = false,
		CorrelationId? CorrelationId = null)
		: this(
			Success,
			Payload,
			PayloadObject: null,
			Status,
			ServedFromFallback,
			CorrelationId)
	{
	}

	public SyncResponse(
		bool Success,
		string Payload,
		object? PayloadObject,
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
		this.PayloadObject = PayloadObject;
	}

	public SyncResponse(
		bool Success,
		object PayloadObject,
		SyncResponseStatus? Status = null,
		bool ServedFromFallback = false,
		CorrelationId? CorrelationId = null)
		: this(
			Success,
			SerializePayload(PayloadObject),
			PayloadObject,
			Status,
			ServedFromFallback,
			CorrelationId)
	{
	}

	public bool Success { get; }

	public string Payload { get; }

	public SyncResponseStatus Status { get; }

	public bool ServedFromFallback { get; }

	public CorrelationId? CorrelationId { get; }

	public object? PayloadObject { get; }

	private static string SerializePayload(object payloadObject)
	{
		ArgumentNullException.ThrowIfNull(payloadObject);

		return payloadObject as string ?? System.Text.Json.JsonSerializer.Serialize(payloadObject);
	}
}
