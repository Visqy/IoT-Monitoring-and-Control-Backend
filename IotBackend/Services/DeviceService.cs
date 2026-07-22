using IotBackend.Contracts;
using IotBackend.Repositories;

namespace IotBackend.Services;

/// <summary>
/// Business logic seputar identitas &amp; kondisi terkini device (tabel <c>devices</c> dan
/// <c>device_current_state</c>). Dipakai oleh DevicesController.
/// </summary>
public sealed class DeviceService
{
    private readonly DeviceRepository _deviceRepository;
    private readonly DeviceStateRepository _deviceStateRepository;
    private readonly ILogger<DeviceService> _logger;

    public DeviceService(
        DeviceRepository deviceRepository, DeviceStateRepository deviceStateRepository, ILogger<DeviceService> logger)
    {
        _deviceRepository = deviceRepository;
        _deviceStateRepository = deviceStateRepository;
        _logger = logger;
    }

    public async Task<List<DeviceSummaryResponse>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = await _deviceRepository.ListAsync(cancellationToken);

        return devices.Select(d => new DeviceSummaryResponse
        {
            DeviceId = d.DeviceId,
            Name = d.Name,
            Location = d.Location,
            Status = d.Status,
            UpdatedAt = d.UpdatedAt
        }).ToList();
    }

    /// <summary>Null kalau deviceId belum pernah mengirim data sama sekali (controller -> 404).</summary>
    public async Task<DeviceStateResponse?> GetDeviceStateAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var state = await _deviceStateRepository.GetByDeviceIdAsync(deviceId, cancellationToken);
        if (state is null)
        {
            return null;
        }

        return new DeviceStateResponse
        {
            DeviceId = state.DeviceId,
            Status = state.Status,
            VoltageA = state.VoltageA,
            VoltageB = state.VoltageB,
            CurrentB = state.CurrentB,
            PowerB = state.PowerB,
            EnergyB = state.EnergyB,
            FrequencyB = state.FrequencyB,
            RelayState = state.RelayState,
            LastSeen = state.LastSeen
        };
    }

    /// <summary>
    /// Dipanggil MqttSubscriberService saat pesan <c>{deviceId}/status</c> (LWT) masuk. Payload
    /// plain string (bukan JSON) <c>"online"</c>/<c>"offline"</c>, retained -- lihat
    /// docs/MQTT_CONTRACT.md §"Status device". <c>last_seen</c> cuma dibump untuk "online"
    /// (device benar-benar baru connect) -- "offline" itu LWT dari broker, bukan aktivitas device.
    /// </summary>
    public async Task ProcessStatusMessageAsync(string deviceId, string rawPayload, CancellationToken cancellationToken = default)
    {
        var status = rawPayload.Trim().ToLowerInvariant();
        if (status is not ("online" or "offline"))
        {
            _logger.LogWarning("Payload status dari {DeviceId} tidak dikenali, diabaikan. Raw: {Raw}", deviceId, rawPayload);
            return;
        }

        var lastSeen = status == "online" ? DateTimeOffset.UtcNow : (DateTimeOffset?)null;
        var rowsAffected = await _deviceStateRepository.UpdateStatusAsync(deviceId, status, lastSeen, cancellationToken);
        if (rowsAffected == 0)
        {
            _logger.LogWarning(
                "device_current_state untuk {DeviceId} belum ada (belum pernah kirim telemetry), status '{Status}' tidak tersimpan.",
                deviceId, status);
        }
    }
}
