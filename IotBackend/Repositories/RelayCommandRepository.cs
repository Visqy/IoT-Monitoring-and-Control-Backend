using Dapper;
using IotBackend.Models;
using Npgsql;

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
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var parameters = new
        {
            command_id = commandId.ToString(),
            device_id = deviceId,
            requested_state = requestedState,
            source
        };

        await connection.ExecuteAsync(new CommandDefinition(InsertPendingSql, parameters, cancellationToken: cancellationToken));
    }

    private const string MarkSentSql = """
        UPDATE relay_commands
        SET status = 'sent', sent_at = NOW(), raw_payload = @raw_payload::jsonb
        WHERE command_id = @command_id
        """;

    public async Task MarkSentAsync(Guid commandId, string rawPayloadJson, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var parameters = new { command_id = commandId.ToString(), raw_payload = rawPayloadJson };
        await connection.ExecuteAsync(new CommandDefinition(MarkSentSql, parameters, cancellationToken: cancellationToken));
    }

    private const string MarkFailedSql = """
        UPDATE relay_commands
        SET status = 'failed', error_message = @error_message
        WHERE command_id = @command_id
        """;

    public async Task MarkFailedAsync(Guid commandId, string errorMessage, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var parameters = new { command_id = commandId.ToString(), error_message = errorMessage };
        await connection.ExecuteAsync(new CommandDefinition(MarkFailedSql, parameters, cancellationToken: cancellationToken));
    }

    // Perubahan relay device-initiated (RFID/boot, commandId null di payload MQTT) -- langsung
    // INSERT baris baru 'executed', bukan UPDATE (tidak ada command yang sudah di-tracking).
    // requested_state = actual_state sengaja sama-sama diisi @actual_state (docs/DATABASE_SCHEMA.md
    // §"Kenapa command_id VARCHAR": tidak ada fase "diminta" terpisah untuk event ini).
    private const string InsertExecutedSql = """
        INSERT INTO relay_commands
            (command_id, device_id, requested_state, actual_state, source, status, requested_at, acknowledged_at, raw_payload)
        VALUES
            (@command_id, @device_id, @actual_state, @actual_state, @source, 'executed', NOW(), @acknowledged_at, @raw_payload::jsonb)
        """;

    public async Task InsertExecutedAsync(
        string commandId, string deviceId, bool actualState, string source,
        DateTimeOffset acknowledgedAt, string rawPayloadJson, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var parameters = new
        {
            command_id = commandId,
            device_id = deviceId,
            actual_state = actualState,
            source,
            acknowledged_at = acknowledgedAt,
            raw_payload = rawPayloadJson
        };

        await connection.ExecuteAsync(new CommandDefinition(InsertExecutedSql, parameters, cancellationToken: cancellationToken));
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
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var parameters = new { command_id = commandId.ToString(), actual_state = actualState };
        return await connection.ExecuteAsync(new CommandDefinition(MarkExecutedSql, parameters, cancellationToken: cancellationToken));
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
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(MarkTimedOutStaleSql, new { cutoff }, cancellationToken: cancellationToken));
    }

    private const string GetByIdSql = """
        SELECT command_id, device_id, requested_state, actual_state, source, status,
               requested_at, sent_at, acknowledged_at, error_message
        FROM relay_commands
        WHERE command_id = @command_id
        """;

    // command_id disimpan VARCHAR di DB (docs/DATABASE_SCHEMA.md) tapi API publik tetap Guid —
    // query ke row-type privat ini dulu (command_id apa adanya sebagai string), baru di-parse
    // manual ke RelayCommandRecord.CommandId supaya tidak bergantung ke konversi implisit Dapper.
    private sealed class RelayCommandRow
    {
        public required string CommandId { get; init; }
        public required string DeviceId { get; init; }
        public bool RequestedState { get; init; }
        public bool? ActualState { get; init; }
        public required string Source { get; init; }
        public required string Status { get; init; }
        public DateTimeOffset RequestedAt { get; init; }
        public DateTimeOffset? SentAt { get; init; }
        public DateTimeOffset? AcknowledgedAt { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public async Task<RelayCommandRecord?> GetByIdAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<RelayCommandRow>(
            new CommandDefinition(GetByIdSql, new { command_id = commandId.ToString() }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        return new RelayCommandRecord
        {
            CommandId = Guid.Parse(row.CommandId),
            DeviceId = row.DeviceId,
            RequestedState = row.RequestedState,
            ActualState = row.ActualState,
            Source = row.Source,
            Status = row.Status,
            RequestedAt = row.RequestedAt,
            SentAt = row.SentAt,
            AcknowledgedAt = row.AcknowledgedAt,
            ErrorMessage = row.ErrorMessage
        };
    }
}
