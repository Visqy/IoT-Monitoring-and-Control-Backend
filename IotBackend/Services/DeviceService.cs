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

    public DeviceService(DeviceRepository deviceRepository, DeviceStateRepository deviceStateRepository)
    {
        _deviceRepository = deviceRepository;
        _deviceStateRepository = deviceStateRepository;
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
            FrequencyA = state.FrequencyA,
            FrequencyB = state.FrequencyB,
            RelayState = state.RelayState,
            LastSeen = state.LastSeen
        };
    }
}
