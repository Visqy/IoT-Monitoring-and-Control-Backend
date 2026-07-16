using IotBackend.Options;
using Microsoft.Extensions.Options;
using MQTTnet;

namespace IotBackend.Infrastructure;

/// <summary>
/// Membungkus koneksi tunggal ke HiveMQ Cloud (TLS). Didaftarkan sebagai SINGLETON
/// (lihat CLAUDE.md §4) — satu koneksi terkelola untuk publish &amp; subscribe.
///
/// Fase 1: hanya connect (TLS + credentials). Subscribe / publish menyusul di Fase 2+.
///
/// Catatan MQTTnet 5.x: API berbeda dari 4.x — factory sekarang <c>MqttClientFactory</c>
/// dan TLS dikonfigurasi lewat <c>WithTlsOptions(...)</c>, bukan <c>WithTls()</c>.
/// </summary>
public sealed class MqttClientService : IAsyncDisposable
{
    private readonly MqttOptions _options;
    private readonly ILogger<MqttClientService> _logger;
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _clientOptions;

    public MqttClientService(IOptions<MqttOptions> options, ILogger<MqttClientService> logger)
    {
        _options = options.Value;
        _logger = logger;

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();

        _clientOptions = new MqttClientOptionsBuilder()
            .WithClientId(_options.ClientId)
            .WithTcpServer(_options.Host, _options.Port)
            .WithCredentials(_options.Username, _options.Password)
            .WithTlsOptions(o => o.UseTls())
            .WithCleanSession()
            .Build();
    }

    public bool IsConnected => _client.IsConnected;

    /// <summary>
    /// Connect ke broker kalau belum tersambung. Idempotent. Melempar exception kalau gagal
    /// (pemanggil yang memutuskan apakah itu fatal — lihat MqttConnectionHostedService).
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client.IsConnected)
        {
            return;
        }

        _logger.LogInformation(
            "Menyambung ke MQTT broker {Host}:{Port} (TLS) sebagai clientId '{ClientId}'...",
            _options.Host, _options.Port, _options.ClientId);

        var result = await _client.ConnectAsync(_clientOptions, cancellationToken);

        _logger.LogInformation("Hasil connect MQTT: {ResultCode}", result.ResultCode);
    }

    /// <summary>
    /// Daftarkan handler pesan masuk. Dipanggil sekali oleh subscriber sebelum subscribe.
    /// </summary>
    public void OnApplicationMessage(Func<MqttApplicationMessageReceivedEventArgs, Task> handler)
    {
        _client.ApplicationMessageReceivedAsync += handler;
    }

    /// <summary>Subscribe ke satu topic filter (mendukung wildcard <c>+</c>).</summary>
    public async Task SubscribeAsync(string topicFilter, CancellationToken cancellationToken = default)
    {
        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(topicFilter))
            .Build();

        await _client.SubscribeAsync(subscribeOptions, cancellationToken);
        _logger.LogInformation("Subscribed ke topic MQTT {Topic}", topicFilter);
    }

    /// <summary>Publish payload (string) ke satu topic, QoS default (at-most-once).</summary>
    public async Task PublishAsync(string topic, string payload, CancellationToken cancellationToken = default)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .Build();

        await _client.PublishAsync(message, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync();
            }
        }
        finally
        {
            _client.Dispose();
        }
    }
}
