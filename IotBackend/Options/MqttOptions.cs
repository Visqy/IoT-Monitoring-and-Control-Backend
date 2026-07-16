namespace IotBackend.Options;

/// <summary>
/// Binding untuk section "Mqtt" di konfigurasi. Nilai non-rahasia (Host, Port, topic)
/// boleh di appsettings.json; Username &amp; Password WAJIB lewat User Secrets / env var.
/// </summary>
public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 8883;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string ClientId { get; init; } = "iot-backend-poc";

    public string TelemetryTopic { get; init; } = "+/pzem";
    public string StatusTopic { get; init; } = "+/status";
    public string RelayStateTopic { get; init; } = "+/relay/state";

    /// <summary>Jeda antar percobaan (re)connect saat koneksi putus/belum tersambung.</summary>
    public int ReconnectDelaySeconds { get; init; } = 5;
}
