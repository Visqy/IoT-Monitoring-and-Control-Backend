namespace IotBackend.Contracts;

/// <summary>Response item untuk <c>GET /api/devices/{deviceId}/telemetry</c>.</summary>
public sealed class TelemetryHistoryResponse
{
    public long Id { get; init; }
    public required string DeviceId { get; init; }
    public double? VoltageA { get; init; }
    public double? VoltageB { get; init; }
    public double? CurrentB { get; init; }
    public double? PowerB { get; init; }
    public double? FrequencyB { get; init; }
    public DateTimeOffset? DeviceTimestamp { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
}
