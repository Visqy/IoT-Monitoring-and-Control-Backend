namespace IotBackend.Contracts;

/// <summary>Response untuk <c>GET /api/devices/{deviceId}/state</c>.</summary>
public sealed class DeviceStateResponse
{
    public required string DeviceId { get; init; }
    public required string Status { get; init; }
    public double? VoltageA { get; init; }
    public double? VoltageB { get; init; }
    public double? CurrentB { get; init; }
    public double? PowerB { get; init; }
    public double? FrequencyB { get; init; }
    public bool? RelayState { get; init; }
    public DateTimeOffset? LastSeen { get; init; }
}
