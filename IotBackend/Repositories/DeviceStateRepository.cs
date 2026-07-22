using Dapper;
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
            (device_id, status, voltage_a, voltage_b, current_b, power_b, frequency_b, last_seen, updated_at)
        VALUES
            (@device_id, @status, @voltage_a, @voltage_b, @current_b, @power_b, @frequency_b, @last_seen, NOW())
        ON CONFLICT (device_id) DO UPDATE SET
            status      = EXCLUDED.status,
            voltage_a   = EXCLUDED.voltage_a,
            voltage_b   = EXCLUDED.voltage_b,
            current_b   = EXCLUDED.current_b,
            power_b     = EXCLUDED.power_b,
            frequency_b = EXCLUDED.frequency_b,
            last_seen   = EXCLUDED.last_seen,
            updated_at  = NOW()
        """;

    public async Task UpsertFromTelemetryAsync(DeviceCurrentState state, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var parameters = new
        {
            device_id = state.DeviceId,
            status = state.Status,
            voltage_a = state.VoltageA,
            voltage_b = state.VoltageB,
            current_b = state.CurrentB,
            power_b = state.PowerB,
            frequency_b = state.FrequencyB,
            last_seen = state.LastSeen
        };

        await connection.ExecuteAsync(new CommandDefinition(UpsertSql, parameters, cancellationToken: cancellationToken));
    }

    private const string GetByDeviceIdSql = """
        SELECT device_id, status, voltage_a, voltage_b, current_b, power_b, frequency_b, relay_state, last_seen
        FROM device_current_state
        WHERE device_id = @device_id
        """;

    /// <summary>Null kalau deviceId belum pernah mengirim data sama sekali.</summary>
    public async Task<DeviceCurrentStateRecord?> GetByDeviceIdAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<DeviceCurrentStateRecord>(
            new CommandDefinition(GetByDeviceIdSql, new { device_id = deviceId }, cancellationToken: cancellationToken));
    }

    // last_seen ikut dibump di sini juga -- deteksi offline (docs/DATABASE_SCHEMA.md) pakai
    // last_seen dari pesan MQTT apapun, bukan cuma telemetry/status, supaya device yang aktif
    // kirim relay/state tapi tidak kirim status tetap dianggap online oleh sweep.
    private const string UpdateRelayStateSql = """
        UPDATE device_current_state
        SET relay_state = @relay_state, last_seen = NOW(), updated_at = NOW()
        WHERE device_id = @device_id
        """;

    /// <summary>Return jumlah baris ter-update — 0 berarti device belum pernah kirim telemetry.</summary>
    public async Task<int> UpdateRelayStateAsync(string deviceId, bool relayState, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var parameters = new { device_id = deviceId, relay_state = relayState };
        return await connection.ExecuteAsync(new CommandDefinition(UpdateRelayStateSql, parameters, cancellationToken: cancellationToken));
    }

    // last_seen dibump hanya kalau caller mengirim nilai (device "online" beneran connect) --
    // untuk "offline" (LWT dari broker) last_seen TIDAK berubah, lihat DeviceService.ProcessStatusMessageAsync.
    private const string UpdateStatusSql = """
        UPDATE device_current_state
        SET status = @status, last_seen = COALESCE(@last_seen, last_seen), updated_at = NOW()
        WHERE device_id = @device_id
        """;

    /// <summary>Return jumlah baris ter-update — 0 berarti device belum pernah kirim telemetry.</summary>
    public async Task<int> UpdateStatusAsync(
        string deviceId, string status, DateTimeOffset? lastSeen, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var parameters = new { device_id = deviceId, status, last_seen = lastSeen };
        return await connection.ExecuteAsync(new CommandDefinition(UpdateStatusSql, parameters, cancellationToken: cancellationToken));
    }

    // Backstop sweep (docs/DATABASE_SCHEMA.md §"Deteksi online/offline"): device yang masih
    // 'online' tapi last_seen sudah lewat threshold ditandai 'offline'. Hanya menyentuh status
    // 'online' -- device yang sudah 'offline'/'unknown' tidak disentuh ulang tiap tick.
    private const string MarkOfflineStaleSql = """
        UPDATE device_current_state
        SET status = 'offline', updated_at = NOW()
        WHERE status = 'online' AND last_seen < @cutoff
        """;

    /// <summary>Return jumlah device yang ditandai offline pada scan ini.</summary>
    public async Task<int> MarkOfflineStaleAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(MarkOfflineStaleSql, new { cutoff }, cancellationToken: cancellationToken));
    }
}
