namespace IotBackend.Contracts;

/// <summary>Response 202 untuk <c>POST /api/devices/{deviceId}/relay</c>.</summary>
public sealed class RelayCommandResponse
{
    public Guid CommandId { get; init; }
    public required string DeviceId { get; init; }
    public bool RequestedState { get; init; }
    public required string Status { get; init; }
}
