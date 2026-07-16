namespace IotBackend.Models;

/// <summary>Representasi satu baris tabel <c>devices</c> (master data).</summary>
public sealed class DeviceRecord
{
    public required string DeviceId { get; init; }
    public string? Name { get; init; }
    public string? Location { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
