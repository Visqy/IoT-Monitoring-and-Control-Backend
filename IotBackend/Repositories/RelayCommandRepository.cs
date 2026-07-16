using IotBackend.Models;
using Npgsql;
using NpgsqlTypes;

namespace IotBackend.Repositories;

/// <summary>Akses tabel <c>relay_commands</c> (tracking command relay + konfirmasi eksekusi).</summary>
public sealed class RelayCommandRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public RelayCommandRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    private const string InsertPendingSql = """
        INSERT INTO relay_commands (command_id, device_id, requested_state, source, status, requested_at)
        VALUES (@command_id, @device_id, @requested_state, @source, 'pending', NOW())
        """;

    public async Task InsertPendingAsync(
        Guid commandId, string deviceId, bool requestedState, string source, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(InsertPendingSql);
        command.Parameters.AddWithValue("command_id", commandId);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("requested_state", requestedState);
        command.Parameters.AddWithValue("source", source);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string MarkSentSql = """
        UPDATE relay_commands
        SET status = 'sent', sent_at = NOW(), raw_payload = @raw_payload
        WHERE command_id = @command_id
        """;

    public async Task MarkSentAsync(Guid commandId, string rawPayloadJson, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(MarkSentSql);
        command.Parameters.AddWithValue("command_id", commandId);
        command.Parameters.Add(new NpgsqlParameter("raw_payload", NpgsqlDbType.Jsonb) { Value = rawPayloadJson });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string MarkFailedSql = """
        UPDATE relay_commands
        SET status = 'failed', error_message = @error_message
        WHERE command_id = @command_id
        """;

    public async Task MarkFailedAsync(Guid commandId, string errorMessage, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(MarkFailedSql);
        command.Parameters.AddWithValue("command_id", commandId);
        command.Parameters.AddWithValue("error_message", errorMessage);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // status <> 'executed' -> konfirmasi pertama yang menang; relay/state duplikat/telat tidak
    // menimpa hasil yang sudah final (lihat state machine di docs/DATABASE_SCHEMA.md).
    private const string MarkExecutedSql = """
        UPDATE relay_commands
        SET status = 'executed', actual_state = @actual_state, acknowledged_at = NOW()
        WHERE command_id = @command_id AND status <> 'executed'
        """;

    /// <summary>Return jumlah baris ter-update — 0 berarti commandId tidak ditemukan.</summary>
    public async Task<int> MarkExecutedAsync(Guid commandId, bool actualState, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(MarkExecutedSql);
        command.Parameters.AddWithValue("command_id", commandId);
        command.Parameters.AddWithValue("actual_state", actualState);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Tandai command yang masih 'sent' tapi tak kunjung dikonfirmasi relay/state melewati batas
    // waktu. Hanya menyentuh status 'sent' — 'executed'/'failed' yang sudah final tidak diubah.
    private const string MarkTimedOutStaleSql = """
        UPDATE relay_commands
        SET status = 'timeout',
            error_message = COALESCE(error_message, 'Tidak ada konfirmasi relay/state dalam batas waktu.')
        WHERE status = 'sent' AND sent_at < @cutoff
        """;

    /// <summary>Return jumlah command yang ditandai timeout pada scan ini.</summary>
    public async Task<int> MarkTimedOutStaleAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(MarkTimedOutStaleSql);
        command.Parameters.AddWithValue("cutoff", cutoff);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string GetByIdSql = """
        SELECT command_id, device_id, requested_state, actual_state, source, status,
               requested_at, sent_at, acknowledged_at, error_message
        FROM relay_commands
        WHERE command_id = @command_id
        """;

    public async Task<RelayCommandRecord?> GetByIdAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(GetByIdSql);
        command.Parameters.AddWithValue("command_id", commandId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RelayCommandRecord
        {
            CommandId = reader.GetFieldValue<Guid>(0),
            DeviceId = reader.GetString(1),
            RequestedState = reader.GetBoolean(2),
            ActualState = reader.IsDBNull(3) ? null : reader.GetBoolean(3),
            Source = reader.GetString(4),
            Status = reader.GetString(5),
            RequestedAt = reader.GetFieldValue<DateTimeOffset>(6),
            SentAt = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            AcknowledgedAt = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9)
        };
    }
}
