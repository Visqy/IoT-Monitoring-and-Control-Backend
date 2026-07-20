using IotBackend.Models;
using Npgsql;
using NpgsqlTypes;

namespace IotBackend.Repositories;

/// <summary>
/// Akses tabel <c>telemetry</c> (raw history). SQL parameterized manual via Npgsql,
/// tanpa ORM dan tanpa business logic (lihat CLAUDE.md §3).
/// </summary>
public sealed class TelemetryRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public TelemetryRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    private const string InsertSql = """
        INSERT INTO telemetry
            (device_id, topic, voltage_a, voltage_b, frequency_a, frequency_b, device_timestamp, raw_payload)
        VALUES
            (@device_id, @topic, @voltage_a, @voltage_b, @frequency_a, @frequency_b, @device_timestamp, @raw_payload)
        """;

    public async Task InsertAsync(TelemetryRecord record, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(InsertSql);
        command.Parameters.AddWithValue("device_id", record.DeviceId);
        command.Parameters.AddWithValue("topic", record.Topic);
        command.Parameters.AddWithValue("voltage_a", record.VoltageA);
        command.Parameters.AddWithValue("voltage_b", record.VoltageB);
        command.Parameters.AddWithValue("frequency_a", record.FrequencyA);
        command.Parameters.AddWithValue("frequency_b", record.FrequencyB);
        command.Parameters.AddWithValue("device_timestamp", (object?)record.DeviceTimestamp ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("raw_payload", NpgsqlDbType.Jsonb) { Value = record.RawPayload });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Urut & filter pakai received_at, bukan device_timestamp — device_timestamp bisa
    // null/tidak akurat kalau ESP belum NTP sync (lihat docs/MQTT_CONTRACT.md).
    private const string GetHistorySql = """
        SELECT id, device_id, topic, voltage_a, voltage_b, frequency_a, frequency_b, device_timestamp, received_at
        FROM telemetry
        WHERE device_id = @device_id
          AND (@from IS NULL OR received_at >= @from)
          AND (@to IS NULL OR received_at <= @to)
        ORDER BY received_at DESC
        LIMIT @limit
        """;

    public async Task<List<TelemetryHistoryRecord>> GetHistoryAsync(
        string deviceId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TelemetryHistoryRecord>();

        await using var command = _dataSource.CreateCommand(GetHistorySql);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = (object?)from ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = (object?)to ?? DBNull.Value });
        command.Parameters.AddWithValue("limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TelemetryHistoryRecord
            {
                Id = reader.GetInt64(0),
                DeviceId = reader.GetString(1),
                Topic = reader.GetString(2),
                VoltageA = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                VoltageB = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                FrequencyA = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                FrequencyB = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                DeviceTimestamp = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                ReceivedAt = reader.GetFieldValue<DateTimeOffset>(8)
            });
        }

        return results;
    }
}
