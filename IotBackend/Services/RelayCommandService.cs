using System.Text.Json;
using IotBackend.Contracts;
using IotBackend.Infrastructure;
using IotBackend.Models;
using IotBackend.Repositories;

namespace IotBackend.Services;

/// <summary>
/// Business rule command relay: generate command_id, kirim command lewat MQTT, dan proses
/// konfirmasi <c>relay/state</c> yang datang belakangan (async, terpisah dari request HTTP).
///
/// PENTING (docs/MQTT_CONTRACT.md): status 'executed' HANYA boleh diset setelah menerima
/// relay/state yang matching commandId-nya — bukan begitu publish MQTT sukses ('sent' ≠ 'executed').
/// </summary>
public sealed class RelayCommandService
{
    private const string CommandSource = "dashboard";

    private static readonly JsonSerializerOptions PublishJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ParseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RelayCommandRepository _relayCommandRepository;
    private readonly DeviceStateRepository _deviceStateRepository;
    private readonly MqttClientService _mqtt;
    private readonly ILogger<RelayCommandService> _logger;

    public RelayCommandService(
        RelayCommandRepository relayCommandRepository,
        DeviceStateRepository deviceStateRepository,
        MqttClientService mqtt,
        ILogger<RelayCommandService> logger)
    {
        _relayCommandRepository = relayCommandRepository;
        _deviceStateRepository = deviceStateRepository;
        _mqtt = mqtt;
        _logger = logger;
    }

    public async Task<RelayCommandResponse> SendCommandAsync(
        string deviceId, bool requestedState, CancellationToken cancellationToken = default)
    {
        var commandId = Guid.NewGuid();

        await _relayCommandRepository.InsertPendingAsync(commandId, deviceId, requestedState, CommandSource, cancellationToken);

        var payload = new RelayCommandPayload
        {
            CommandId = commandId,
            State = ToMqttState(requestedState),
            Source = CommandSource,
            IssuedAt = DateTimeOffset.UtcNow
        };
        var payloadJson = JsonSerializer.Serialize(payload, PublishJsonOptions);

        string status;
        try
        {
            await _mqtt.PublishAsync($"{deviceId}/relay/set", payloadJson, cancellationToken);
            await _relayCommandRepository.MarkSentAsync(commandId, payloadJson, cancellationToken);
            status = "sent";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal publish relay command {CommandId} ke device {DeviceId}.", commandId, deviceId);
            await _relayCommandRepository.MarkFailedAsync(commandId, ex.Message, cancellationToken);
            status = "failed";
        }

        return new RelayCommandResponse
        {
            CommandId = commandId,
            DeviceId = deviceId,
            RequestedState = requestedState,
            Status = status
        };
    }

    public async Task<RelayCommandStatusResponse?> GetCommandAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        var record = await _relayCommandRepository.GetByIdAsync(commandId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        return new RelayCommandStatusResponse
        {
            CommandId = record.CommandId,
            DeviceId = record.DeviceId,
            RequestedState = record.RequestedState,
            ActualState = record.ActualState,
            Status = record.Status,
            RequestedAt = record.RequestedAt,
            SentAt = record.SentAt,
            AcknowledgedAt = record.AcknowledgedAt,
            ErrorMessage = record.ErrorMessage
        };
    }

    /// <summary>Dipanggil MqttSubscriberService saat pesan <c>{deviceId}/relay/state</c> masuk.</summary>
    public async Task ProcessRelayStateAsync(string deviceId, string rawPayload, CancellationToken cancellationToken = default)
    {
        Guid? commandId = null;
        bool? actualState = null;
        string? source = null;

        try
        {
            var payload = JsonSerializer.Deserialize<RelayStatePayload>(rawPayload, ParseJsonOptions);
            if (payload is not null)
            {
                commandId = payload.CommandId;
                actualState = ParseMqttState(payload.State);
                source = payload.Source;
            }
        }
        catch (JsonException)
        {
            // Fallback firmware lama: payload string sederhana "ON"/"OFF" tanpa JSON (docs/MQTT_CONTRACT.md).
            actualState = ParseMqttState(rawPayload);
        }

        if (actualState is null)
        {
            _logger.LogWarning("Payload relay/state dari {DeviceId} tidak dikenali, diabaikan. Raw: {Raw}", deviceId, rawPayload);
            return;
        }

        if (commandId is { } id)
        {
            var rowsAffected = await _relayCommandRepository.MarkExecutedAsync(id, actualState.Value, cancellationToken);
            if (rowsAffected == 0)
            {
                _logger.LogWarning("relay/state dengan commandId {CommandId} tidak match relay_commands manapun.", id);
            }
        }
        else
        {
            // commandId null = perubahan tak ter-tracking (RFID/boot) atau firmware lama (string polos).
            // Tampilkan source biar bisa dibedakan sumber pemicunya.
            var trigger = source ?? "firmware lama/tak diketahui";
            _logger.LogInformation("relay/state dari {DeviceId} dipicu oleh {Source} (tanpa commandId).", deviceId, trigger);
        }

        var stateRowsAffected = await _deviceStateRepository.UpdateRelayStateAsync(deviceId, actualState.Value, cancellationToken);
        if (stateRowsAffected == 0)
        {
            _logger.LogWarning(
                "device_current_state untuk {DeviceId} belum ada (belum pernah kirim telemetry), relay_state tidak tersimpan.",
                deviceId);
        }
    }

    private static string ToMqttState(bool state) => state ? "ON" : "OFF";

    private static bool? ParseMqttState(string? state) => state?.Trim().ToUpperInvariant() switch
    {
        "ON" => true,
        "OFF" => false,
        _ => null
    };
}
