namespace IotBackend.Models;

/// <summary>
/// Representasi payload MQTT masuk dari topic <c>+/pzem</c> (lihat docs/MQTT_CONTRACT.md).
/// Nama field mengikuti JSON firmware (camelCase) — deserialize pakai case-insensitive.
/// </summary>
public sealed class TelemetryPayload
{
    public double VoltageA { get; init; }
    public double VoltageB { get; init; }
    public double FreqA { get; init; }
    public double FreqB { get; init; }

    /// <summary>Waktu dari ESP, format saat ini "yyyy-MM-dd HH:mm:ss" tanpa timezone. Bisa null.</summary>
    public string? Timestamp { get; init; }
}
