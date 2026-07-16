namespace IotBackend.Contracts;

/// <summary>Response item untuk <c>GET /api/devices</c>.</summary>
public sealed class DeviceSummaryResponse
{
    public required string DeviceId { get; init; }
    public string? Name { get; init; }
    public string? Location { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
