using IotBackend.Infrastructure;
using IotBackend.Options;
using IotBackend.Services;
using Microsoft.Extensions.Options;
using MQTTnet;

namespace IotBackend.BackgroundServices;

/// <summary>
/// Hosted service (singleton) yang: connect -> subscribe -> parse topic -> deserialize ->
/// panggil Service yang sesuai lewat scope baru per pesan (CLAUDE.md §4). Tidak inject
/// service scoped langsung ke constructor — pakai IServiceScopeFactory.
///
/// Subscribe ke telemetry (<c>+/pzem</c>) dan konfirmasi relay (<c>+/relay/state</c>).
/// <c>+/status</c> belum ditangani (di luar scope fase saat ini).
/// </summary>
public sealed class MqttSubscriberService : BackgroundService
{
    private readonly MqttClientService _mqtt;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MqttOptions _options;
    private readonly ILogger<MqttSubscriberService> _logger;

    public MqttSubscriberService(
        MqttClientService mqtt,
        IServiceScopeFactory scopeFactory,
        IOptions<MqttOptions> options,
        ILogger<MqttSubscriberService> logger)
    {
        _mqtt = mqtt;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Daftarkan handler sekali sebelum loop (client instance-nya sama walau reconnect).
        _mqtt.OnApplicationMessage(HandleMessageAsync);

        var reconnectDelay = TimeSpan.FromSeconds(Math.Max(1, _options.ReconnectDelaySeconds));

        // Loop supervisor: kalau koneksi belum ada/putus, connect + subscribe ulang. IMqttClient
        // tidak auto-resubscribe setelah reconnect, jadi subscribe wajib diulang tiap connect baru.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_mqtt.IsConnected)
                {
                    await _mqtt.ConnectAsync(stoppingToken);
                    await SubscribeAllAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "MQTT belum tersambung. Coba lagi dalam {Delay} detik.", reconnectDelay.TotalSeconds);
            }

            try
            {
                await Task.Delay(reconnectDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SubscribeAllAsync(CancellationToken cancellationToken)
    {
        await _mqtt.SubscribeAsync(_options.TelemetryTopic, cancellationToken);
        await _mqtt.SubscribeAsync(_options.RelayStateTopic, cancellationToken);
    }

    private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var deviceId = ExtractDeviceId(topic);
        var payloadText = e.ApplicationMessage.ConvertPayloadToString() ?? string.Empty;

        if (string.IsNullOrEmpty(deviceId))
        {
            _logger.LogWarning("Topic '{Topic}' tidak punya segmen device_id, pesan diabaikan.", topic);
            return;
        }

        try
        {
            // Hosted service singleton tidak boleh inject service scoped langsung -> buat scope per pesan.
            using var scope = _scopeFactory.CreateScope();

            if (topic.EndsWith("/pzem", StringComparison.Ordinal))
            {
                var telemetryService = scope.ServiceProvider.GetRequiredService<TelemetryService>();
                await telemetryService.ProcessTelemetryAsync(deviceId, topic, payloadText);
            }
            else if (topic.EndsWith("/relay/state", StringComparison.Ordinal))
            {
                var relayCommandService = scope.ServiceProvider.GetRequiredService<RelayCommandService>();
                await relayCommandService.ProcessRelayStateAsync(deviceId, payloadText);
            }
            else
            {
                _logger.LogWarning("Topic '{Topic}' tidak dikenali, pesan diabaikan.", topic);
            }
        }
        catch (Exception ex)
        {
            // Satu pesan buruk tidak boleh mematikan handler.
            _logger.LogError(ex, "Gagal memproses pesan MQTT dari topic {Topic}.", topic);
        }
    }

    /// <summary>device_id = segmen pertama topic ("terminal1/pzem" -> "terminal1").</summary>
    private static string? ExtractDeviceId(string topic)
    {
        if (string.IsNullOrEmpty(topic))
        {
            return null;
        }

        var slashIndex = topic.IndexOf('/');
        return slashIndex <= 0 ? null : topic[..slashIndex];
    }
}
