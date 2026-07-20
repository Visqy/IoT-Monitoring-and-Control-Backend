using Dapper;
using IotBackend.Models;
using Npgsql;

namespace IotBackend.Repositories;

/// <summary>Akses tabel <c>devices</c> (master data).</summary>
public sealed class DeviceRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public DeviceRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // LEFT JOIN device_current_state karena devices.status tidak lagi ditulis (single writer
    // buat status ada di device_current_state, lihat docs/DATABASE_SCHEMA.md) — status yang
    // benar dibaca dari sana, bukan dari kolom devices.status yang selalu 'unknown'.
    private const string ListSql = """
        SELECT d.device_id, d.name, d.location, COALESCE(dcs.status, 'unknown') AS status, d.updated_at
        FROM devices d
        LEFT JOIN device_current_state dcs ON dcs.device_id = d.device_id
        ORDER BY d.device_id
        """;

    public async Task<List<DeviceRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<DeviceRecord>(new CommandDefinition(ListSql, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    // Auto-register: dipanggil saat telemetry pertama dari sebuah device masuk (lihat
    // TelemetryService). name/location sengaja dibiarkan NULL — itu data administratif yang
    // diisi manual. status TIDAK disentuh di sini sama sekali (lihat ListSql di atas).
    private const string EnsureRegisteredSql = """
        INSERT INTO devices (device_id, updated_at)
        VALUES (@device_id, NOW())
        ON CONFLICT (device_id) DO NOTHING
        """;

    public async Task EnsureRegisteredAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(EnsureRegisteredSql, new { device_id = deviceId }, cancellationToken: cancellationToken));
    }
}
