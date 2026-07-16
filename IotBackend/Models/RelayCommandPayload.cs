namespace IotBackend.Models;

/// <summary>Payload yang dipublish backend ke topic <c>{deviceId}/relay/set</c> (perintah, bukan konfirmasi).</summary>
public sealed class RelayCommandPayload
{
    public Guid CommandId { get; init; }
    public required string State { get; init; } // "ON" / "OFF"
    public required string Source { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
}
