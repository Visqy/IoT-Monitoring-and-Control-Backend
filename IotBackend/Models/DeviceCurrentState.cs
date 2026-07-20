namespace IotBackend.Models;

/// <summary>
/// Data untuk UPSERT ke <c>device_current_state</c> dari telemetry. Sengaja TIDAK memuat
/// <c>relay_state</c> — kolom itu hanya diisi dari <c>+/relay/state</c> (Fase 4), supaya
/// telemetry tidak menimpanya jadi null.
/// </summary>
public sealed class DeviceCurrentState
{
    public required string DeviceId { get; init; }
    public required string Status { get; init; }

    public double VoltageA { get; init; }
    public double VoltageB { get; init; }
    public double FrequencyA { get; init; }
    public double FrequencyB { get; init; }

    public DateTimeOffset LastSeen { get; init; }
}
