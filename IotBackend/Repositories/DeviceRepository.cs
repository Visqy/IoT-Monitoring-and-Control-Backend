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

    private const string ListSql = """
        SELECT device_id, name, location, status, updated_at
        FROM devices
        ORDER BY device_id
        """;

    public async Task<List<DeviceRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<DeviceRecord>();

        await using var command = _dataSource.CreateCommand(ListSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DeviceRecord
            {
                DeviceId = reader.GetString(0),
                Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                Location = reader.IsDBNull(2) ? null : reader.GetString(2),
                Status = reader.GetString(3),
                UpdatedAt = reader.GetFieldValue<DateTimeOffset>(4)
            });
        }

        return results;
    }

    // Auto-register: dipanggil saat telemetry pertama dari sebuah device masuk (lihat
    // TelemetryService). name/location sengaja dibiarkan NULL — itu data administratif
    // yang diisi manual, bukan hasil deteksi otomatis dari telemetry.
    private const string UpsertSql = """
        INSERT INTO devices (device_id, status, updated_at)
        VALUES (@device_id, @status, NOW())
        ON CONFLICT (device_id) DO UPDATE SET
            status     = EXCLUDED.status,
            updated_at = NOW()
        """;

    public async Task UpsertAsync(string deviceId, string status, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(UpsertSql);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("status", status);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
