using IotBackend.Models;
using Npgsql;

namespace IotBackend.Repositories;

/// <summary>
/// Akses tabel <c>device_current_state</c> (satu baris terbaru per device).
/// </summary>
public sealed class DeviceStateRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public DeviceStateRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // UPSERT dari telemetry. CATATAN: relay_state sengaja TIDAK disertakan di sini supaya
    // nilainya (yang diisi dari +/relay/state, Fase 4) tidak tertimpa jadi null.
    private const string UpsertSql = """
        INSERT INTO device_current_state
            (device_id, status, voltage_a, voltage_b, frequency_a, frequency_b, last_seen, updated_at)
        VALUES
            (@device_id, @status, @voltage_a, @voltage_b, @frequency_a, @frequency_b, @last_seen, NOW())
        ON CONFLICT (device_id) DO UPDATE SET
            status      = EXCLUDED.status,
            voltage_a   = EXCLUDED.voltage_a,
            voltage_b   = EXCLUDED.voltage_b,
            frequency_a = EXCLUDED.frequency_a,
            frequency_b = EXCLUDED.frequency_b,
            last_seen   = EXCLUDED.last_seen,
            updated_at  = NOW()
        """;

    public async Task UpsertFromTelemetryAsync(DeviceCurrentState state, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(UpsertSql);
        command.Parameters.AddWithValue("device_id", state.DeviceId);
        command.Parameters.AddWithValue("status", state.Status);
        command.Parameters.AddWithValue("voltage_a", state.VoltageA);
        command.Parameters.AddWithValue("voltage_b", state.VoltageB);
        command.Parameters.AddWithValue("frequency_a", state.FrequencyA);
        command.Parameters.AddWithValue("frequency_b", state.FrequencyB);
        command.Parameters.AddWithValue("last_seen", state.LastSeen);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string GetByDeviceIdSql = """
        SELECT device_id, status, voltage_a, voltage_b, frequency_a, frequency_b, relay_state, last_seen
        FROM device_current_state
        WHERE device_id = @device_id
        """;

    /// <summary>Null kalau deviceId belum pernah mengirim data sama sekali.</summary>
    public async Task<DeviceCurrentStateRecord?> GetByDeviceIdAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(GetByDeviceIdSql);
        command.Parameters.AddWithValue("device_id", deviceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DeviceCurrentStateRecord
        {
            DeviceId = reader.GetString(0),
            Status = reader.GetString(1),
            VoltageA = reader.IsDBNull(2) ? null : reader.GetDouble(2),
            VoltageB = reader.IsDBNull(3) ? null : reader.GetDouble(3),
            FrequencyA = reader.IsDBNull(4) ? null : reader.GetDouble(4),
            FrequencyB = reader.IsDBNull(5) ? null : reader.GetDouble(5),
            RelayState = reader.IsDBNull(6) ? null : reader.GetBoolean(6),
            LastSeen = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)
        };
    }

    private const string UpdateRelayStateSql = """
        UPDATE device_current_state
        SET relay_state = @relay_state, updated_at = NOW()
        WHERE device_id = @device_id
        """;

    /// <summary>Return jumlah baris ter-update — 0 berarti device belum pernah kirim telemetry.</summary>
    public async Task<int> UpdateRelayStateAsync(string deviceId, bool relayState, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(UpdateRelayStateSql);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("relay_state", relayState);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
