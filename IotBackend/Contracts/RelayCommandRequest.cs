namespace IotBackend.Contracts;

/// <summary>Request body untuk <c>POST /api/devices/{deviceId}/relay</c>.</summary>
public sealed class RelayCommandRequest
{
    public required bool State { get; init; }
}
