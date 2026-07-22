namespace IotBackend.Models;

/// <summary>
/// Satu baris yang akan di-INSERT ke tabel <c>telemetry</c>. <c>received_at</c> tidak ada di sini
/// karena diisi oleh default <c>NOW()</c> di database.
/// </summary>
public sealed class TelemetryRecord
{
    public required string DeviceId { get; init; }
    public required string Topic { get; init; }

    public double VoltageA { get; init; }
    public double VoltageB { get; init; }
    public double CurrentB { get; init; }
    public double PowerB { get; init; }
    public double FrequencyB { get; init; }

    /// <summary>Waktu dari device (nullable — null kalau payload tidak punya / gagal di-parse).</summary>
    public DateTime? DeviceTimestamp { get; init; }

    /// <summary>Payload mentah apa adanya, disimpan ke kolom JSONB untuk debugging.</summary>
    public required string RawPayload { get; init; }
}
